using System.Data;
using Dapper;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Events;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;
    private readonly IEventPublisher? _events;

    public PurchaseService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant, IDbConnection db, IAuditService audit, IEventPublisher? events = null)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
        _audit = audit;
        _events = events;
    }

    public async Task<string> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var poRepo = _unitOfWork.Repository<PurchaseOrder>();
        var poItemRepo = _unitOfWork.Repository<PurchaseOrderItem>();

        var now = DateTime.UtcNow;
        var date = request.PoDate == default ? now.Date : request.PoDate.Date;
        // WO-11: 한글 prefix 통일 (발주서 = 발-)
        var prefix = $"발-{date:yyyyMMdd}-";
        // 작20260428이7 P0-A: EF FindAsync.Count 패턴은 미저장 엔티티 누락 → UNIQUE 충돌. DB MAX 직조회.
        var poNo = await DocumentNumberHelper.NextNumberAsync(
            _db, _currentTenant.TenantId, "purchase_orders", "po_no", prefix, ct);

        var poId = Guid.NewGuid().ToString();
        var po = new PurchaseOrder
        {
            Id = poId,
            PoId = poId,
            TenantId = _currentTenant.TenantId,
            PoNo = poNo,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            PoDate = date,
            ExpectedDate = request.ExpectedDate,
            Status = PurchaseOrderStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo
        };
        await poRepo.AddAsync(po);

        foreach (var item in request.Items)
        {
            var poItem = new PurchaseOrderItem
            {
                Id = Guid.NewGuid().ToString(),
                PoItemId = Guid.NewGuid().ToString(),
                PoId = poId,
                TenantId = _currentTenant.TenantId,
                ItemId = item.ItemId,
                OrderedQty = item.OrderedQty,
                ReceivedQty = 0m,
                UnitPrice = item.UnitPrice,
                SupplyAmount = item.SupplyAmount,
                VatAmount = item.VatAmount,
                WarehouseId = item.WarehouseId,
                ItemStatus = "pending"
            };
            await poItemRepo.AddAsync(poItem);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // 감사로그 — 발주서 생성
        var poAfterJson = $"{{\"po_no\":\"{poNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "purchase_order", poId, afterJson: poAfterJson, ct: ct);

        return poId;
    }

    public async Task<string> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        var receiptRepo = _unitOfWork.Repository<PurchaseReceipt>();
        var receiptItemRepo = _unitOfWork.Repository<PurchaseReceiptItem>();

        var now = DateTime.UtcNow;
        var date = request.ReceiptDate == default ? now.Date : request.ReceiptDate.Date;
        // WO-11: 한글 prefix 통일 (매입처리 = 매-)
        var prefix = $"매-{date:yyyyMMdd}-";
        // 작20260428이7 P0-A: 174건 자동 사슬 채번 충돌 진범. DB MAX 직조회로 EF 캐시 우회.
        var receiptNo = await DocumentNumberHelper.NextNumberAsync(
            _db, _currentTenant.TenantId, "purchase_receipts", "receipt_no", prefix, ct);

        var receiptId = Guid.NewGuid().ToString();
        var receipt = new PurchaseReceipt
        {
            Id = receiptId,
            ReceiptId = receiptId,
            TenantId = _currentTenant.TenantId,
            ReceiptNo = receiptNo,
            PoId = request.PoId,
            PartnerId = request.PartnerId,
            ReceiptDate = date,
            SourceType = string.IsNullOrWhiteSpace(request.PoId) ? "direct" : "from_po",
            Status = PurchaseReceiptStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo
        };
        await receiptRepo.AddAsync(receipt);

        // 봉합 (2026-06-22, 10차 P0-4-REGRESS, 교차검증 발견): P0-4 의 빈 창고 폴백이 PO전환 경로
        //   (ConvertOrderToReceiptAsync)에만 있어, 직접매입(무PO) 경로는 빈 WarehouseId 가 그대로 영속되어
        //   ConfirmReceipt 의 item_stock UPSERT 가 빈 warehouse_id 로 기록 → 유령 창고 재고(P0-4 가 막으려던
        //   증상)가 직접매입에 잔존했다. 동일 폴백을 직접매입 경로에도 적용한다(판매·BOM·PO전환과 동일 전략).
        var emptyWhLines = request.Items.Where(x => string.IsNullOrWhiteSpace(x.WarehouseId)).ToList();
        if (emptyWhLines.Count > 0)
        {
            var defaultWh = await _db.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    """
                    SELECT warehouse_id FROM warehouses
                     WHERE tenant_id = @TenantId AND is_active = 1
                     ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                     LIMIT 1
                    """,
                    new { TenantId = _currentTenant.TenantId },
                    cancellationToken: ct));
            if (string.IsNullOrEmpty(defaultWh))
            {
                throw new InvalidOperationException("등록된 창고가 없습니다.");
            }
            foreach (var line in emptyWhLines)
            {
                line.WarehouseId = defaultWh;
            }
        }

        foreach (var item in request.Items)
        {
            var receiptItem = new PurchaseReceiptItem
            {
                Id = Guid.NewGuid().ToString(),
                ReceiptItemId = Guid.NewGuid().ToString(),
                ReceiptId = receiptId,
                TenantId = _currentTenant.TenantId,
                PoItemId = item.PoItemId,
                ItemId = item.ItemId,
                WarehouseId = item.WarehouseId,
                Qty = item.Qty,
                UnitPrice = item.UnitPrice,
                SupplyAmount = item.SupplyAmount,
                VatAmount = item.VatAmount
            };
            await receiptItemRepo.AddAsync(receiptItem);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // 감사로그 — 매입명세서 생성 (초안)
        var recAfterJson = $"{{\"receipt_no\":\"{receiptNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "purchase_receipt", receiptId, afterJson: recAfterJson, ct: ct);

        return receiptId;
    }

    public async Task ConfirmReceiptAsync(string receiptId, ConfirmReceiptRequest request, CancellationToken ct = default)
    {
        var receiptRepo = _unitOfWork.Repository<PurchaseReceipt>();
        var receiptItemRepo = _unitOfWork.Repository<PurchaseReceiptItem>();
        var poItemRepo = _unitOfWork.Repository<PurchaseOrderItem>();
        var workflowRepo = _unitOfWork.Repository<WorkflowSetting>();
        var ledgerRepo = _unitOfWork.Repository<StockLedger>();

        var receipt = await receiptRepo.GetByIdAsync(receiptId)
            ?? throw new InvalidOperationException("입고 전표를 찾을 수 없습니다.");

        if (receipt.Status != PurchaseReceiptStatus.Draft)
        {
            throw new InvalidOperationException("draft 상태 전표만 확정할 수 있습니다.");
        }

        // 합계 0원 매입은 확정 금지 — journal_lines 의 CHECK 제약
        // (debit>0 AND credit=0) OR (debit=0 AND credit>0) 위반 및 워크플로우 오염 방지(§20).
        if (receipt.TotalAmount + receipt.VatAmount <= 0m)
        {
            throw new InvalidOperationException("합계가 0원인 매입은 확정할 수 없습니다. 품목·수량·단가를 확인해주세요.");
        }

        // 월마감 체크 — 마감된 월의 전표 확정 차단
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, receipt.TenantId, receipt.ReceiptDate, ct);

        var receiptItems = await receiptItemRepo.FindAsync(x => x.ReceiptId == receiptId);

        if (!string.IsNullOrWhiteSpace(receipt.PoId))
        {
            var overReceiptSetting = await workflowRepo.FindAsync(x =>
                x.SettingKey == "purchase.over_receipt_allow" && x.IsActive);
            var overReceiptAllow = overReceiptSetting.FirstOrDefault()?.SettingValue == "true";

            if (!overReceiptAllow)
            {
                foreach (var line in receiptItems.Where(x => !string.IsNullOrWhiteSpace(x.PoItemId)))
                {
                    var poItem = await poItemRepo.GetByIdAsync(line.PoItemId!);
                    if (poItem is null)
                    {
                        throw new InvalidOperationException("매칭된 발주 라인을 찾을 수 없습니다.");
                    }

                    if (poItem.ReceivedQty + line.Qty > poItem.OrderedQty)
                    {
                        throw new InvalidOperationException("발주 잔량을 초과하여 입고할 수 없습니다.");
                    }
                }
            }
        }

        // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): stock_ledger UNIQUE 키 (tenant, source_type, source_id,
        //   item_id, move_type) 단위 유일. 종전엔 입고 라인별로 그대로 AddAsync 해, 한 입고에 같은 품목이
        //   2라인(다른 창고·단가) 들어가면 같은 키가 2번 INSERT → SaveChangesAsync UNIQUE 위반 → 매입 전체
        //   롤백("매입했는데 재고 안 늘어남", 헌법 #20). 판매와 동일하게 item_id 로 합산해 키당 1행만 기록.
        var receiptSourceType = string.IsNullOrWhiteSpace(receipt.PoId) ? "direct_purchase" : "purchase_receipt";
        foreach (var grp in receiptItems.GroupBy(x => x.ItemId))
        {
            var first = grp.First();
            var qtySum = grp.Sum(x => x.Qty);
            var supplySum = grp.Sum(x => x.SupplyAmount);
            var ledger = new StockLedger
            {
                LedgerId = 0,
                TenantId = receipt.TenantId,
                ItemId = grp.Key,
                WarehouseId = first.WarehouseId,
                PartnerId = receipt.PartnerId,
                LedgerDate = receipt.ReceiptDate,
                Ym = receipt.ReceiptDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.In,
                SourceType = receiptSourceType,
                SourceId = receipt.ReceiptId,
                DocNo = receipt.ReceiptNo,
                QtyIn = qtySum,
                QtyOut = 0m,
                UnitCost = qtySum != 0m ? supplySum / qtySum : first.UnitPrice,
                SupplyAmount = supplySum
            };

            await ledgerRepo.AddAsync(ledger);
        }

        if (!string.IsNullOrWhiteSpace(receipt.PoId))
        {
            foreach (var line in receiptItems.Where(x => !string.IsNullOrWhiteSpace(x.PoItemId)))
            {
                var poItem = await poItemRepo.GetByIdAsync(line.PoItemId!);
                if (poItem is null)
                {
                    continue;
                }

                poItem.ReceivedQty += line.Qty;
                if (poItem.ReceivedQty <= 0m)
                {
                    poItem.ItemStatus = "pending";
                }
                else if (poItem.ReceivedQty < poItem.OrderedQty)
                {
                    poItem.ItemStatus = "partial";
                }
                else
                {
                    poItem.ItemStatus = "closed";
                }
                poItemRepo.Update(poItem);
            }
        }

        receipt.Status = PurchaseReceiptStatus.Confirmed;
        receiptRepo.Update(receipt);

        // ── 단일 트랜잭션 (EF + Dapper 공유 · ISharedTransaction) ──
        // SalesService.ConfirmDelivery와 동일 패턴. 중간 실패 시 전체 롤백.
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 1) EF 변경 저장 (stock_ledger INSERT + status='confirmed' + po_items UPDATE)
            await _unitOfWork.SaveChangesAsync(ct);

            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 2) item_stock 증가 (Dapper · 동일 tx)
            // 봉합 (2026-06-22, 10차 avg_cost P2-A): 종전엔 라인별로 avg_cost = @UnitCost(=line.UnitPrice)로
            //   덮어써, 같은 품목 다단가 입고 시 마지막 라인 단가가 평균원가를 덮어씀(가중평균 아님). ledger(위)는
            //   item_id 그룹 가중평균(supplySum/qtySum)이라 불일치. 정확한 이동평균(기존재고 가중)은 과도한
            //   재설계라 범위 밖 — ledger 와 동일한 "그룹 가중평균 단가"를 써 둘의 일관성만 확보(근사).
            //   item_stock 은 (item,warehouse) 단위 행이므로 창고까지 묶어 그룹화.
            foreach (var grp in receiptItems.GroupBy(x => new { x.ItemId, x.WarehouseId }))
            {
                var qtySum = grp.Sum(x => x.Qty);
                var supplySum = grp.Sum(x => x.SupplyAmount);
                var groupUnitCost = qtySum != 0m ? supplySum / qtySum : grp.First().UnitPrice;

                const string upsertStockSql = """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty + @Qty,
                      avg_cost = @UnitCost,
                      last_updated_at = NOW(6)
                    """;

                await conn.ExecuteAsync(new CommandDefinition(
                    upsertStockSql,
                    new
                    {
                        TenantId = receipt.TenantId,
                        ItemId = grp.Key.ItemId,
                        WarehouseId = grp.Key.WarehouseId,
                        Qty = qtySum,
                        UnitCost = groupUnitCost
                    },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 3) monthly_summary 매입 갱신 — 멱등 가드 (작4 P0-4, 동일 tx)
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx,
                tenantId: receipt.TenantId,
                date: receipt.ReceiptDate,
                sourceType: "purchase_receipt_confirmed",
                sourceId: receipt.ReceiptId,
                field: MonthlySummaryGuard.SummaryField.TotalPurchase,
                amount: receipt.TotalAmount,
                ct: ct);

            // 4-A) PO 헤더 status 동기화 — receipt.PoId 가 있을 때만.
            // 모든 라인 closed → 'received' / 일부만 closed → 'partial' / 그 외 'ordered'.
            // §절대원칙 #20 (워크플로우 끊김 금지): item_status 만 갱신하고 헤더가 'draft' 로 남으면
            // 발주서 목록에서 매입전환 버튼 재노출 → 미입고 0 → 400. 헤더까지 함께 옮긴다.
            // EF enum 매핑은 'closed'/'confirmed' 를 쓰지만 DB enum 은 'received'/'ordered' 라 직 SQL 사용.
            if (!string.IsNullOrWhiteSpace(receipt.PoId))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE purchase_orders po
                    LEFT JOIN (
                        SELECT po_id,
                               SUM(CASE WHEN item_status='closed'  THEN 1 ELSE 0 END) AS closed_cnt,
                               SUM(CASE WHEN item_status='partial' THEN 1 ELSE 0 END) AS partial_cnt,
                               COUNT(*) AS total_cnt
                        FROM purchase_order_items
                        WHERE po_id = @PoId
                        GROUP BY po_id
                    ) s ON s.po_id = po.po_id
                    SET po.status = CASE
                                       WHEN s.closed_cnt = s.total_cnt THEN 'received'
                                       WHEN s.closed_cnt > 0 OR s.partial_cnt > 0 THEN 'partial'
                                       ELSE 'ordered'
                                    END,
                        po.updated_at = NOW(6)
                    WHERE po.po_id = @PoId AND po.tenant_id = @TenantId
                    """,
                    new { PoId = receipt.PoId, TenantId = receipt.TenantId },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 4-B) stock_alerts 닫기 — 이번 매입에 포함된 품목의 ordered 알림을 received로 전환
            // 자동사슬로 생성된 발주가 매입 확정되면 알림이 사라져야 반복 발주를 막는다(§20 워크플로우 끊김 금지).
            var confirmedItemIds = receiptItems.Select(x => x.ItemId).Distinct().ToList();
            if (confirmedItemIds.Count > 0)
            {
                var inClause = string.Join(",", confirmedItemIds.Select((_, i) => $"@ItemId{i}"));
                var alertParams = new DynamicParameters();
                alertParams.Add("TenantId", receipt.TenantId);
                for (var i = 0; i < confirmedItemIds.Count; i++)
                    alertParams.Add($"ItemId{i}", confirmedItemIds[i]);

                await conn.ExecuteAsync(new CommandDefinition(
                    $"""
                    UPDATE stock_alerts
                    SET status = 'received', updated_at = NOW(6)
                    WHERE tenant_id = @TenantId
                      AND item_id IN ({inClause})
                      AND status IN ('pending', 'ordered')
                    """,
                    alertParams,
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 5) 회계 자동 기표 (차변 매입+부가세대급금 / 대변 외상매입금)
            // PurchaseReceipt 엔티티에는 EmployeeId가 없으므로 null로 전달 (추후 도메인 확장 시 실제 사원 ID 연결).
            await AutoJournalHelper.RecordPurchaseConfirmAsync(
                conn, dbTx,
                receipt.TenantId,
                receipt.ReceiptId,
                receipt.ReceiptNo,
                receipt.ReceiptDate,
                receipt.PartnerId,
                receipt.TotalAmount,
                receipt.VatAmount,
                null,
                ct);

            // 6) partner_balance 매입 가산 — 트랜잭션 내부에서 처리 (RED-1 보강)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id,
                   total_sales, total_receipt, total_purchase, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId, 0, 0, @Amount, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_purchase  = total_purchase + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = receipt.TenantId, PartnerId = receipt.PartnerId,
                      Amount = receipt.TotalAmount },
                transaction: dbTx, cancellationToken: ct));

            // 7) 전체 커밋
            await tx.CommitAsync(ct);

            // 감사로그
            await _audit.LogAsync("confirm", "purchase_receipt", receiptId, ct: ct);

            // 8) 이벤트 발행 (트랜잭션 밖) — 안전재고 알림 전용
            //    partner_balance.total_purchase 는 트랜잭션 내부(6단계)에서 이미 처리됨 (RED-1 보강).
            //    item_stock·monthly_summary 는 위 트랜잭션에서 이미 처리. 이벤트는 안전재고 알림만 책임.
            if (_events is not null)
            {
                try
                {
                    var evt = new PurchaseConfirmedEvent(
                        TenantId: receipt.TenantId,
                        PoId: receipt.ReceiptId,
                        PartnerId: receipt.PartnerId,
                        TotalAmount: receipt.TotalAmount + receipt.VatAmount,
                        Items: receiptItems.Select(it => new DeliveryItemEvent(
                            ItemId: it.ItemId,
                            Qty: it.Qty,
                            UnitPrice: it.UnitPrice,
                            Amount: it.Qty * it.UnitPrice)).ToList());
                    await _events.PublishAsync("purchase.confirmed", evt, ct);
                }
                catch (Exception evtEx)
                {
                    // 본 거래는 이미 커밋 완료. 이벤트 실패해도 거래는 살린다.
                    await _audit.LogAsync("event_failed", "purchase_receipt", receiptId,
                        reason: $"purchase.confirmed: {evtEx.Message}", ct: ct);
                }
            }
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[PurchaseService] rollback failed: {rbex.Message}"); }
            throw;
        }

        // 결재 트리거 (커밋 이후 실행) — 실패해도 매입 확정 원장은 유효
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "receipt", receipt.ReceiptId, receipt.ReceiptNo,
                $"매입명세서 확정: {receipt.ReceiptNo}",
                receipt.TotalAmount + receipt.VatAmount,
                receipt.TenantId, "system", "확정자", ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 매입명세서 {receipt.ReceiptNo} 결재 트리거 실패: {ex.Message}");
        }
    }

    public async Task<List<PurchaseOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               po.po_id AS PoId,
                               po.po_no AS PoNo,
                               po.po_date AS PoDate,
                               po.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (po.total_amount + po.vat_amount) AS TotalAmount,
                               po.vat_amount AS VatAmount,
                               po.total_amount AS SupplyAmount,
                               po.status AS Status,
                               po.memo AS Memo
                           FROM purchase_orders po
                           LEFT JOIN partners p
                               ON p.partner_id = po.partner_id
                                  AND p.tenant_id = po.tenant_id
                           WHERE po.tenant_id = @TenantId
                             AND po.is_deleted = 0
                             AND po.is_auto = 0
                             AND (@From IS NULL OR po.po_date >= @From)
                             AND (@To IS NULL OR po.po_date <= @To)
                             AND (@Status IS NULL OR po.status = @Status)
                           ORDER BY po.po_date DESC,
                                    po.po_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<PurchaseOrderListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    Status = string.IsNullOrWhiteSpace(status) ? null : status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<List<PurchaseReceiptListDto>> GetReceiptsAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               pr.receipt_id AS ReceiptId,
                               pr.receipt_no AS ReceiptNo,
                               pr.receipt_date AS ReceiptDate,
                               pr.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (pr.total_amount + pr.vat_amount) AS TotalAmount,
                               pr.vat_amount AS VatAmount,
                               pr.total_amount AS SupplyAmount,
                               pr.status AS Status,
                               pr.memo AS Memo
                           FROM purchase_receipts pr
                           LEFT JOIN partners p
                               ON p.partner_id = pr.partner_id
                                  AND p.tenant_id = pr.tenant_id
                           WHERE pr.tenant_id = @TenantId
                             AND pr.status <> 'cancelled'
                             AND (@From IS NULL OR pr.receipt_date >= @From)
                             AND (@To IS NULL OR pr.receipt_date <= @To)
                             AND (@Status IS NULL OR pr.status = @Status)
                           ORDER BY pr.receipt_date DESC,
                                    pr.receipt_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<PurchaseReceiptListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    Status = string.IsNullOrWhiteSpace(status) ? null : status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<(string ReceiptId, string ReceiptNo)> ConvertOrderToReceiptAsync(
        string poId,
        string tenantId,
        CancellationToken ct = default)
    {
        var poRepo = _unitOfWork.Repository<PurchaseOrder>();
        var poItemRepo = _unitOfWork.Repository<PurchaseOrderItem>();

        var po = await poRepo.GetByIdAsync(poId)
            ?? throw new InvalidOperationException("발주서를 찾을 수 없습니다.");

        if (po.TenantId != tenantId)
        {
            throw new InvalidOperationException("발주서를 찾을 수 없습니다.");
        }

        // 이 발주를 참조하는 미확정(draft) 매입명세가 이미 있으면 중복 전환 차단
        var draftExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1) FROM purchase_receipts
            WHERE po_id = @PoId AND tenant_id = @TenantId AND status != 'Confirmed'
            """,
            new { PoId = poId, TenantId = tenantId },
            cancellationToken: ct));
        if (draftExists > 0)
        {
            throw new InvalidOperationException(
                "이 발주에 대한 매입명세(미확정)가 이미 존재합니다. " +
                "기존 매입명세서를 확정하거나 삭제한 후 다시 전환해주세요.");
        }

        var items = await poItemRepo.FindAsync(x => x.PoId == poId);
        var receiptItems = items
            .Where(x => x.OrderedQty - x.ReceivedQty > 0)
            .Select(x => new CreateReceiptItemRequest
            {
                PoItemId = x.PoItemId,
                ItemId = x.ItemId,
                WarehouseId = x.WarehouseId ?? string.Empty,
                Qty = x.OrderedQty - x.ReceivedQty,
                UnitPrice = x.UnitPrice,
                SupplyAmount = (x.OrderedQty - x.ReceivedQty) * x.UnitPrice,
                VatAmount = Math.Round((x.OrderedQty - x.ReceivedQty) * x.UnitPrice * 0.1m, 0)
            }).ToList();

        if (receiptItems.Count == 0)
        {
            // 모든 라인이 이미 입고 완료(received_qty >= ordered_qty)면, 보통은 dd89274 의
            // 자동 매입처리(autoReceive) 로 이미 끝난 PO. 사용자가 발주서 목록에서 또 누른 경우.
            var allClosed = items.Any() && items.All(x => x.OrderedQty - x.ReceivedQty <= 0);
            var msg = allClosed
                ? "이미 매입이 완료된 발주서입니다. 매입명세서 목록에서 확인해 주세요."
                : "전환 가능한 미입고 품목이 없습니다.";
            throw new InvalidOperationException(msg);
        }

        // 창고 Id가 비어 있는 라인은 기본 창고로 채운다.
        var emptyWhItems = receiptItems.Where(x => string.IsNullOrWhiteSpace(x.WarehouseId)).ToList();
        if (emptyWhItems.Count > 0)
        {
            // 봉합 (2026-06-22, 10차 P0-4):
            //   종전 폴백 `defaultWh ?? "MAIN"` 은 실재하지 않는 문자열 "MAIN" 을 warehouse_id 로 기록해
            //   재고(item_stock)·원장(stock_ledger)이 유령 창고에 쌓이며 정합이 깨졌다.
            //   판매(SalesService)·BOM(BomService) 의 기본창고 선택 전략과 통일한다:
            //   wh_code='MAIN'(또는 'WH-MAIN') 을 우선, 그 다음 wh_code 순으로 실제 활성 창고 1행을 고른다.
            //   프로비저닝(CompanyBootstrapController)이 가입 시 'MAIN' 창고를 항상 1개 만들므로 정상 경로에선 반드시 잡힌다.
            //   그래도 정말 없으면 판매처럼 명확한 에러를 던져 유령 id 기록을 원천 차단한다(헌법 #20 정합 차단이 유령 기록보다 안전).
            var defaultWh = await _db.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    """
                    SELECT warehouse_id FROM warehouses
                     WHERE tenant_id = @TenantId AND is_active = 1
                     ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                     LIMIT 1
                    """,
                    new { TenantId = tenantId },
                    cancellationToken: ct));

            if (string.IsNullOrEmpty(defaultWh))
            {
                throw new InvalidOperationException("등록된 창고가 없습니다.");
            }

            foreach (var item in emptyWhItems)
            {
                item.WarehouseId = defaultWh;
            }
        }

        var request = new CreateReceiptRequest
        {
            PoId = poId,
            PartnerId = po.PartnerId,
            ReceiptDate = DateTime.UtcNow.Date,
            Memo = $"발주 {po.PoNo} 에서 전환",
            Items = receiptItems
        };

        var receiptId = await CreateReceiptAsync(request, ct);

        // 생성된 입고 전표의 번호를 조회한다.
        var receiptRepo = _unitOfWork.Repository<PurchaseReceipt>();
        var receipt = await receiptRepo.GetByIdAsync(receiptId);
        var receiptNo = receipt?.ReceiptNo ?? string.Empty;

        return (receiptId, receiptNo);
    }

    public async Task<List<PurchaseReturnListDto>> GetReturnsAsync(
        string tenantId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        if (_db.State != System.Data.ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }

        var sql = """
            SELECT r.return_id AS ReturnId, r.return_no AS ReturnNo, r.return_date AS ReturnDate,
                   r.partner_id AS PartnerId, COALESCE(p.partner_name,'') AS PartnerName,
                   r.total_amount AS TotalAmount, r.vat_amount AS VatAmount,
                   r.status AS Status, r.memo AS Memo
            FROM purchase_returns r
            LEFT JOIN partners p ON p.partner_id = r.partner_id
            WHERE r.tenant_id = @Tid AND r.is_deleted = 0
            """;
        var conditions = new List<string>();
        if (from.HasValue) conditions.Add("AND r.return_date >= @From");
        if (to.HasValue) conditions.Add("AND r.return_date <= @To");
        sql += string.Join(" ", conditions) + " ORDER BY r.return_date DESC, r.return_no DESC";

        var rows = await _db.QueryAsync<PurchaseReturnListDto>(new CommandDefinition(
            sql, new { Tid = tenantId, From = from, To = to }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync(
        string receiptId, string tenantId, CancellationToken ct = default)
    {
        if (_db.State != System.Data.ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync(ct);
            else
                _db.Open();
        }

        // 매입 정보 조회
        var receipt = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT receipt_id, receipt_no, partner_id FROM purchase_receipts WHERE receipt_id=@Id AND tenant_id=@Tid",
            new { Id = receiptId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("매입명세서를 찾을 수 없습니다.");

        // 매입 품목 조회
        var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
            "SELECT item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id FROM purchase_receipt_items WHERE receipt_id=@Id AND tenant_id=@Tid",
            new { Id = receiptId, Tid = tenantId }, cancellationToken: ct))).ToList();

        // 반품 문서번호 채번 — WO-11 한글 prefix
        var today = DateTime.UtcNow.Date;
        var prefix = $"매반-{today:yyyyMMdd}-";
        var cnt = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM purchase_returns WHERE tenant_id=@Tid AND return_no LIKE CONCAT(@Pfx,'%')",
            new { Tid = tenantId, Pfx = prefix }, cancellationToken: ct));
        var returnNo = $"{prefix}{cnt + 1:000}";
        var returnId = Guid.NewGuid().ToString();

        decimal totalAmount = 0, totalVat = 0;
        foreach (var item in items)
        {
            totalAmount += (decimal)item.supply_amount;
            totalVat += (decimal)item.vat_amount;
        }

        // 반품 헤더 생성
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO purchase_returns (return_id, tenant_id, return_no, receipt_id, partner_id,
              return_date, return_type, status, total_amount, vat_amount, memo, created_at, updated_at)
            VALUES (@ReturnId, @Tid, @ReturnNo, @ReceiptId, @PartnerId,
              @ReturnDate, 'purchase_return', 'draft', @Total, @Vat, @Memo, NOW(6), NOW(6))
            """,
            new
            {
                ReturnId = returnId, Tid = tenantId, ReturnNo = returnNo,
                ReceiptId = receiptId, PartnerId = (string)receipt.partner_id,
                ReturnDate = today, Total = totalAmount, Vat = totalVat,
                Memo = $"매입 {(string)receipt.receipt_no} 에서 반품 전환"
            }, cancellationToken: ct));

        // 반품 품목 생성
        foreach (var item in items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_return_items (return_item_id, return_id, tenant_id,
                  item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id)
                VALUES (UUID(), @ReturnId, @Tid, @ItemId, @Qty, @Price, @Supply, @Vat, @Wh)
                """,
                new
                {
                    ReturnId = returnId, Tid = tenantId,
                    ItemId = (string)item.item_id, Qty = (decimal)item.qty,
                    Price = (decimal)item.unit_price, Supply = (decimal)item.supply_amount,
                    Vat = (decimal)item.vat_amount, Wh = (string?)item.warehouse_id
                }, cancellationToken: ct));
        }

        return (returnId, returnNo);
    }

    // ─────────────────────────────────────────────────────────────────────
    // P0 #1 — 매입반품 신규 작성 (헌법 #20 흐름 끊김 봉합)
    // receipt_id 없이도 발행 가능. status='draft' 로 INSERT (헌법 #6 정합).
    // ─────────────────────────────────────────────────────────────────────
    public async Task<(string ReturnId, string ReturnNo)> CreatePurchaseReturnAsync(
        CreatePurchaseReturnRequest request, string tenantId, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(request.PartnerId)) throw new InvalidOperationException("거래처는 필수입니다.");
        if (request.Items is null || request.Items.Count == 0) throw new InvalidOperationException("반품 품목은 1건 이상이어야 합니다.");

        if (_db.State != System.Data.ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync(ct);
            else
                _db.Open();
        }

        var returnDate = request.ReturnDate == default ? DateTime.UtcNow.Date : request.ReturnDate.Date;
        var prefix = $"매반-{returnDate:yyyyMMdd}-";
        var cnt = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM purchase_returns WHERE tenant_id=@Tid AND return_no LIKE CONCAT(@Pfx,'%')",
            new { Tid = tenantId, Pfx = prefix }, cancellationToken: ct));
        var returnNo = $"{prefix}{cnt + 1:000}";
        var returnId = Guid.NewGuid().ToString();

        decimal totalAmount = 0, totalVat = 0;
        foreach (var it in request.Items)
        {
            totalAmount += it.SupplyAmount;
            totalVat += it.VatAmount;
        }

        // 봉합 (2026-06-22, 11차전 반품사유 거짓봉합 교차검증): 종전 INSERT 가 memo 만 저장하고
        //   return_reason·return_reason_memo 컬럼을 빠뜨려, 프론트가 사유를 보내도 DB 에 영구 유실됐다
        //   (clean DDL 2566-2567 에 컬럼·인덱스 존재하나 SQL 미반영). 화면 입력 사유를 정확히 저장한다.
        await _db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO purchase_returns (return_id, tenant_id, return_no, receipt_id, partner_id,
                return_date, return_type, status, total_amount, vat_amount, memo,
                return_reason, return_reason_memo, created_at, updated_at)
              VALUES (@ReturnId, @Tid, @ReturnNo, @ReceiptId, @PartnerId,
                @ReturnDate, 'purchase_return', 'draft', @Total, @Vat, @Memo,
                @ReturnReason, @ReturnReasonMemo, NOW(6), NOW(6))",
            new
            {
                ReturnId = returnId, Tid = tenantId, ReturnNo = returnNo,
                ReceiptId = request.ReceiptId,
                PartnerId = request.PartnerId,
                ReturnDate = returnDate, Total = totalAmount, Vat = totalVat,
                Memo = request.Memo,
                ReturnReason = request.ReturnReason,
                ReturnReasonMemo = request.ReturnReasonMemo
            }, cancellationToken: ct));

        foreach (var it in request.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO purchase_return_items (return_item_id, return_id, tenant_id,
                    item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id)
                  VALUES (UUID(), @ReturnId, @Tid, @ItemId, @Qty, @Price, @Supply, @Vat, @Wh)",
                new
                {
                    ReturnId = returnId, Tid = tenantId,
                    ItemId = it.ItemId, Qty = it.Qty,
                    Price = it.UnitPrice, Supply = it.SupplyAmount,
                    Vat = it.VatAmount, Wh = it.WarehouseId
                }, cancellationToken: ct));
        }

        return (returnId, returnNo);
    }

    // ─────────────────────────────────────────────────────────────────────
    // P0 #1 — draft 상태 매입반품 수정 (confirmed/deleted 수정 절대 금지, 헌법 #6 정합)
    // ─────────────────────────────────────────────────────────────────────
    public async Task UpdatePurchaseReturnAsync(
        string returnId, UpdatePurchaseReturnRequest request, string tenantId, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(request.PartnerId)) throw new InvalidOperationException("거래처는 필수입니다.");
        if (request.Items is null || request.Items.Count == 0) throw new InvalidOperationException("반품 품목은 1건 이상이어야 합니다.");

        if (_db.State != System.Data.ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync(ct);
            else
                _db.Open();
        }

        var current = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT return_id, status FROM purchase_returns WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        var status = (string)current.status;
        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"draft 상태만 수정 가능합니다. (현재: {status})");

        var returnDate = request.ReturnDate == default ? DateTime.UtcNow.Date : request.ReturnDate.Date;
        decimal totalAmount = 0, totalVat = 0;
        foreach (var it in request.Items)
        {
            totalAmount += it.SupplyAmount;
            totalVat += it.VatAmount;
        }

        // 봉합 (2026-06-22, 11차전 반품사유 거짓봉합 교차검증): INSERT 와 동일하게 UPDATE 도 사유 컬럼이
        //   빠져 있어 draft 반품 수정 시 사유가 유실됐다. return_reason·return_reason_memo 를 함께 갱신한다.
        await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE purchase_returns
              SET partner_id=@PartnerId, return_date=@ReturnDate,
                  total_amount=@Total, vat_amount=@Vat, memo=@Memo,
                  return_reason=@ReturnReason, return_reason_memo=@ReturnReasonMemo, updated_at=NOW(6)
              WHERE return_id=@Id AND tenant_id=@Tid AND status='draft'",
            new
            {
                Id = returnId, Tid = tenantId,
                PartnerId = request.PartnerId, ReturnDate = returnDate,
                Total = totalAmount, Vat = totalVat, Memo = request.Memo,
                ReturnReason = request.ReturnReason,
                ReturnReasonMemo = request.ReturnReasonMemo
            }, cancellationToken: ct));

        // 기존 라인 삭제 후 신규 라인 INSERT (헌법 #3 INSERT ONLY 원장과는 별개 — purchase_return_items는 헤더 종속 테이블).
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM purchase_return_items WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct));

        foreach (var it in request.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO purchase_return_items (return_item_id, return_id, tenant_id,
                    item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id)
                  VALUES (UUID(), @ReturnId, @Tid, @ItemId, @Qty, @Price, @Supply, @Vat, @Wh)",
                new
                {
                    ReturnId = returnId, Tid = tenantId,
                    ItemId = it.ItemId, Qty = it.Qty,
                    Price = it.UnitPrice, Supply = it.SupplyAmount,
                    Vat = it.VatAmount, Wh = it.WarehouseId
                }, cancellationToken: ct));
        }
    }

    // 결재 트리거는 ApprovalTriggerHelper.TryCreateApprovalAsync로 통합됨

    // ─────────────────────────────────────────────────────────────────────
    // 매입반품 확정 — status 'draft' → 'confirmed' + 재고원장 REVERSE OUT
    // 원매입(IN)의 역방향으로 OUT 원장을 기록해 재고를 차감하고,
    // item_stock.current_qty 감소 + monthly_summary.total_purchase 차감을
    // 단일 트랜잭션으로 처리한다. (이전 구현은 stock_ledger만 기록하고 실재고를 갱신하지 않았음.)
    // ─────────────────────────────────────────────────────────────────────
    public async Task ConfirmPurchaseReturnAsync(string returnId, string tenantId, string? employeeId, CancellationToken ct = default)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT return_id, partner_id, return_date, status, return_no, total_amount, vat_amount FROM purchase_returns WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        if ((string)header.status != "draft")
        {
            throw new InvalidOperationException("draft 상태만 확정할 수 있습니다.");
        }

        // 월마감 체크
        DateTime rd = (DateTime)header.return_date;
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, rd, ct);

        var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
            "SELECT item_id, qty, unit_price, supply_amount, warehouse_id FROM purchase_return_items WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))).ToList();

        var returnNo = (string)header.return_no;
        var partnerId = (string)header.partner_id;
        var totalAmount = (decimal)header.total_amount;
        var vatAmount = (decimal)header.vat_amount;

        // EF + Dapper 공유 트랜잭션 (매입 확정 패턴과 동일)
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 봉합 (2026-06-23, 6차 전수조사 PUR-RETURN-OVER P2): 종전엔 매입반품 확정 시 수량·음수재고 검사가
            //   전혀 없어, 입고 10개에 반품 100개도 통과해 item_stock 이 음수가 됐다(매출 확정은 SALES-04 음수검사가
            //   있는데 매입반품만 무방비, 헌법 #20 무결성). negative_stock_allow=false 면 반품 전 회사 합산 잔량(서버측
            //   SQL 집계)으로 음수 검사. 반품 OUT 기록 전 시점이라 커밋된 잔량만 봐도 정합(SALES-04 동일 논리).
            var negSetting = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT setting_value FROM workflow_settings WHERE tenant_id=@Tid AND setting_key='stock.negative_stock_allow' AND is_active=1",
                new { Tid = tenantId }, transaction: dbTx, cancellationToken: ct));
            var negativeStockAllow = string.Equals(negSetting, "true", StringComparison.OrdinalIgnoreCase);

            // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): 반품 OUT 원장도 stock_ledger UNIQUE 키
            //   (tenant, source_type=purchase_return, source_id=returnId, item_id, move_type=out) 단위 유일.
            //   종전엔 반품 라인별로 그대로 INSERT 해 같은 품목 2라인이면 키가 2번 찍혀 반품 확정이 차단됐다(헌법 #20).
            //   item_id 로 합산해 키당 1행만 기록·차감. 음수검사도 합산 총량으로 1회 — 더 정확(라인 분할 우회 차단).
            // 봉합 (2026-06-22, 13차 축4 P2 유령창고): 종전 폴백 `?? "wh-main"` 은 실재하지 않는 문자열을
            //   warehouse_id 로 기록해(stock_ledger NOT NULL이라 INSERT는 성공) 반품 OUT·item_stock 차감이
            //   유령 창고로 빠지고 실제 창고 재고가 안 줄었다(헌법 #20). 매입 입고(ConfirmReceipt:650)·판매·BOM
            //   과 동일하게 실제 기본창고(wh_code MAIN 우선)를 조회해 폴백으로 쓴다. 라인 창고가 있으면 그대로.
            var returnDefaultWh = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                """
                SELECT warehouse_id FROM warehouses
                 WHERE tenant_id = @TenantId AND is_active = 1
                 ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                 LIMIT 1
                """,
                new { TenantId = tenantId }, transaction: dbTx, cancellationToken: ct));
            if (string.IsNullOrEmpty(returnDefaultWh))
                throw new InvalidOperationException("활성 창고가 없습니다. 창고를 먼저 등록해주세요.");

            var returnGroups = items
                .GroupBy(it => (string)it.item_id)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Wh = string.IsNullOrEmpty((string?)g.First().warehouse_id) ? returnDefaultWh : (string)g.First().warehouse_id,
                    Qty = g.Sum(x => (decimal)x.qty),
                    Supply = g.Sum(x => (decimal)x.supply_amount),
                    UnitPrice = (decimal)g.First().unit_price
                })
                .ToList();

            if (!negativeStockAllow)
            {
                foreach (var g in returnGroups)
                {
                    var bal = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
                        "SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_ledger WHERE tenant_id=@Tid AND item_id=@ItemId",
                        new { Tid = tenantId, ItemId = g.ItemId }, transaction: dbTx, cancellationToken: ct));
                    if (bal - g.Qty < 0m)
                    {
                        throw new InvalidOperationException("반품 수량이 현재 재고를 초과합니다. 재고를 확인해주세요.");
                    }
                }
            }

            // 1) 재고원장 Reverse OUT INSERT
            foreach (var g in returnGroups)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger
                      (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
                       move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES
                      (@Tid, @ItemId, @Wh, @PartnerId, @EmpId, @Date, @Ym,
                       'out', 'purchase_return', @Rid, @DocNo, 0, @Qty, @UnitPrice, @Supply, '매입반품 (Reverse IN)')
                    """,
                    new
                    {
                        Tid = tenantId,
                        ItemId = g.ItemId,
                        Wh = g.Wh,
                        PartnerId = partnerId,
                        EmpId = employeeId,
                        Date = rd,
                        Ym = rd.ToString("yyyy-MM"),
                        Rid = returnId,
                        DocNo = returnNo,
                        Qty = g.Qty,
                        UnitPrice = g.UnitPrice,
                        Supply = g.Supply
                    },
                    transaction: dbTx,
                    cancellationToken: ct));

                // 2) item_stock 감소 — 없는 레코드도 방어적으로 UPSERT
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty - @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new
                    {
                        TenantId = tenantId,
                        ItemId = g.ItemId,
                        WarehouseId = g.Wh,
                        Qty = g.Qty,
                        UnitCost = g.UnitPrice
                    },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 3) monthly_summary 매입 역산 — MonthlySummaryGuard 멱등 가드 (ConfirmReceiptAsync 대칭)
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx,
                tenantId: tenantId,
                date: rd,
                sourceType: "purchase_return_confirmed",
                sourceId: returnId,
                field: MonthlySummaryGuard.SummaryField.TotalPurchase,
                amount: -totalAmount,
                ct: ct);

            // 4) partner_balance 매입 역산 (반품 확정 시 total_purchase 차감)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id,
                   total_sales, total_receipt, total_purchase, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId, 0, 0, -@Amount, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_purchase  = total_purchase - @Amount,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = tenantId, PartnerId = partnerId, Amount = totalAmount },
                transaction: dbTx,
                cancellationToken: ct));

            // 5) 회계 역분개 — RecordPurchaseConfirmAsync 대칭 (차변 외상매입금 / 대변 매입+부가세대급금)
            if (totalAmount != 0m || vatAmount != 0m)
            {
                await AutoJournalHelper.RecordPurchaseReturnAsync(
                    conn, dbTx!,
                    tenantId,
                    returnId,
                    returnNo,
                    rd,
                    partnerId,
                    totalAmount,
                    vatAmount,
                    employeeId,
                    ct);
            }

            // 6) 상태 전환
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE purchase_returns SET status='confirmed', updated_at=NOW(6) WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId },
                transaction: dbTx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);

            await _audit.LogAsync("confirm", "purchase_return", returnId, ct: ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[PurchaseService] rollback failed: {rbex.Message}"); }
            throw;
        }

        // 결재 트리거 (커밋 이후) — 실패해도 반품 확정 원장은 유효
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "purchase_return", returnId, returnNo,
                $"매입반품 확정: {returnNo}",
                totalAmount + vatAmount,
                tenantId, "system", "확정자", ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 매입반품 {returnNo} 결재 트리거 실패: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매입반품 draft 삭제 — confirmed 상태는 매출취소와 같은 별도 취소 경로가 필요.
    // ─────────────────────────────────────────────────────────────────────
    public async Task DeletePurchaseReturnAsync(string returnId, string tenantId, CancellationToken ct = default)
    {
        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM purchase_returns WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        if (status != "draft")
        {
            throw new InvalidOperationException("draft 상태만 삭제할 수 있습니다. 확정된 반품은 취소 처리가 필요합니다.");
        }

        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM purchase_return_items WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM purchase_returns WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            await _audit.LogAsync("delete", "purchase_return", returnId, ct: ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[PurchaseService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매입명세서 단건 조회 — 목록 클릭 → 편집 화면 로드용.
    // ─────────────────────────────────────────────────────────────────────
    public async Task<PurchaseReceiptDetailDto?> GetReceiptDetailAsync(
        string receiptId, string tenantId, CancellationToken ct = default)
    {
        const string headerSql = """
            SELECT pr.receipt_id  AS ReceiptId,
                   pr.receipt_no  AS ReceiptNo,
                   pr.receipt_date AS ReceiptDate,
                   pr.po_id       AS PoId,
                   pr.partner_id  AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   pr.total_amount AS TotalAmount,
                   pr.vat_amount   AS VatAmount,
                   pr.status       AS Status,
                   pr.memo         AS Memo
              FROM purchase_receipts pr
              LEFT JOIN partners p
                ON p.partner_id = pr.partner_id
               AND p.tenant_id  = pr.tenant_id
             WHERE pr.receipt_id = @Id
               AND pr.tenant_id  = @Tid
            """;

        var header = await _db.QueryFirstOrDefaultAsync<PurchaseReceiptDetailDto>(
            new CommandDefinition(headerSql, new { Id = receiptId, Tid = tenantId }, cancellationToken: ct));
        if (header is null)
        {
            return null;
        }

        const string linesSql = """
            SELECT pri.receipt_item_id AS ReceiptItemId,
                   pri.po_item_id      AS PoItemId,
                   pri.item_id         AS ItemId,
                   COALESCE(i.item_name, '') AS ItemName,
                   COALESCE(i.spec, '')      AS Spec,
                   IFNULL(i.unit, 'EA')      AS Unit,
                   pri.warehouse_id    AS WarehouseId,
                   pri.qty             AS Qty,
                   pri.unit_price      AS UnitPrice,
                   pri.supply_amount   AS SupplyAmount,
                   pri.vat_amount      AS VatAmount
              FROM purchase_receipt_items pri
              LEFT JOIN items i
                ON i.item_id  = pri.item_id
               AND i.tenant_id = pri.tenant_id
             WHERE pri.receipt_id = @Id
               AND pri.tenant_id  = @Tid
             ORDER BY pri.receipt_item_id
            """;

        var lines = await _db.QueryAsync<PurchaseReceiptDetailItemDto>(
            new CommandDefinition(linesSql, new { Id = receiptId, Tid = tenantId }, cancellationToken: ct));
        header.Items = lines.ToList();
        return header;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매입명세서 draft 삭제 — confirmed는 별도 취소(Reverse) 경로 필요.
    // INSERT ONLY 원장(§3) 원칙상 confirmed는 여기서 삭제 금지.
    // ─────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────
    // 발주서 단건 조회 — 목록 클릭 → 편집 화면 로드용.
    // ─────────────────────────────────────────────────────────────────────
    public async Task<PurchaseOrderDetailDto?> GetOrderDetailAsync(
        string poId, string tenantId, CancellationToken ct = default)
    {
        const string headerSql = """
            SELECT po.po_id        AS PoId,
                   po.po_no        AS PoNo,
                   po.po_date      AS PoDate,
                   po.expected_date AS ExpectedDate,
                   po.partner_id   AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   po.total_amount AS TotalAmount,
                   po.vat_amount   AS VatAmount,
                   po.status       AS Status,
                   po.memo         AS Memo
              FROM purchase_orders po
              LEFT JOIN partners p
                ON p.partner_id = po.partner_id
               AND p.tenant_id  = po.tenant_id
             WHERE po.po_id     = @Id
               AND po.tenant_id = @Tid
               AND po.is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<PurchaseOrderDetailDto>(
            new CommandDefinition(headerSql, new { Id = poId, Tid = tenantId }, cancellationToken: ct));
        if (header is null) return null;

        const string linesSql = """
            SELECT poi.po_item_id    AS PoItemId,
                   poi.item_id       AS ItemId,
                   COALESCE(i.item_name, '') AS ItemName,
                   COALESCE(i.spec, '')      AS Spec,
                   IFNULL(i.unit, 'EA')      AS Unit,
                   poi.warehouse_id  AS WarehouseId,
                   poi.ordered_qty   AS OrderedQty,
                   poi.received_qty  AS ReceivedQty,
                   poi.unit_price    AS UnitPrice,
                   poi.supply_amount AS SupplyAmount,
                   poi.vat_amount    AS VatAmount
              FROM purchase_order_items poi
              LEFT JOIN items i
                ON i.item_id   = poi.item_id
               AND i.tenant_id = poi.tenant_id
             WHERE poi.po_id     = @Id
               AND poi.tenant_id = @Tid
             ORDER BY poi.po_item_id
            """;

        var lines = await _db.QueryAsync<PurchaseOrderDetailItemDto>(
            new CommandDefinition(linesSql, new { Id = poId, Tid = tenantId }, cancellationToken: ct));
        header.Items = lines.ToList();
        return header;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 발주서 draft 삭제 — soft delete (is_deleted=1). 매입전환 후에는 삭제 불가.
    // ─────────────────────────────────────────────────────────────────────
    public async Task DeletePurchaseOrderAsync(string poId, string tenantId, CancellationToken ct = default)
    {
        // dynamic 캐스팅(long·byte 변환) 시 InvalidCastException 으로 500 회귀가 발생해
        // 강타입 record 로 교체. is_deleted 는 TINYINT(1) → byte 매핑.
        var row = await _db.QueryFirstOrDefaultAsync<(string Status, byte IsDeleted)?>(new CommandDefinition(
            "SELECT status AS Status, is_deleted AS IsDeleted FROM purchase_orders WHERE po_id=@Id AND tenant_id=@Tid",
            new { Id = poId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("발주서를 찾을 수 없습니다.");

        if (row.IsDeleted == 1)
        {
            throw new InvalidOperationException("이미 삭제된 발주서입니다.");
        }

        // 매입전환된 라인 차단 — 단, 그 매입명세서가 cancelled 상태면 살아있는 입고가
        // 아니므로 차단 대상에서 제외 (사장님 보고 2026-04-26: 매입 삭제 후에도 발주 못 지움).
        // active(=non-cancelled) 매입명세서에 연결된 라인이 received_qty>0 일 때만 차단.
        var activeReceived = await _db.QueryFirstOrDefaultAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
              FROM purchase_receipt_items pri
              JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
             WHERE pri.po_item_id IN (
                     SELECT po_item_id FROM purchase_order_items
                      WHERE po_id=@Id AND tenant_id=@Tid
                   )
               AND pri.tenant_id = @Tid
               AND pr.status <> 'cancelled'
            """,
            new { Id = poId, Tid = tenantId }, cancellationToken: ct));
        if (activeReceived > 0)
        {
            throw new InvalidOperationException("이미 매입전환(입고)된 라인이 있어 삭제할 수 없습니다. 매입명세서를 먼저 취소하거나 반품해주세요.");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE purchase_orders SET is_deleted=1, updated_at=NOW(6) WHERE po_id=@Id AND tenant_id=@Tid",
            new { Id = poId, Tid = tenantId }, cancellationToken: ct));

        await _audit.LogAsync("delete", "purchase_order", poId, ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매입반품 단건 조회 — 목록 → 편집 로드용.
    // ─────────────────────────────────────────────────────────────────────
    public async Task<PurchaseReturnDetailDto?> GetReturnDetailAsync(
        string returnId, string tenantId, CancellationToken ct = default)
    {
        const string headerSql = """
            SELECT r.return_id    AS ReturnId,
                   r.return_no    AS ReturnNo,
                   r.return_date  AS ReturnDate,
                   r.receipt_id   AS ReceiptId,
                   r.partner_id   AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   r.total_amount AS TotalAmount,
                   r.vat_amount   AS VatAmount,
                   r.status       AS Status,
                   r.memo         AS Memo
              FROM purchase_returns r
              LEFT JOIN partners p
                ON p.partner_id = r.partner_id
               AND p.tenant_id  = r.tenant_id
             WHERE r.return_id  = @Id
               AND r.tenant_id  = @Tid
               AND r.is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<PurchaseReturnDetailDto>(
            new CommandDefinition(headerSql, new { Id = returnId, Tid = tenantId }, cancellationToken: ct));
        if (header is null) return null;

        const string linesSql = """
            SELECT rti.return_item_id AS ReturnItemId,
                   rti.item_id        AS ItemId,
                   COALESCE(i.item_name, '') AS ItemName,
                   COALESCE(i.spec, '')      AS Spec,
                   IFNULL(i.unit, 'EA')      AS Unit,
                   rti.warehouse_id   AS WarehouseId,
                   rti.qty            AS Qty,
                   rti.unit_price     AS UnitPrice,
                   rti.supply_amount  AS SupplyAmount,
                   rti.vat_amount     AS VatAmount
              FROM purchase_return_items rti
              LEFT JOIN items i
                ON i.item_id   = rti.item_id
               AND i.tenant_id = rti.tenant_id
             WHERE rti.return_id = @Id
               AND rti.tenant_id = @Tid
             ORDER BY rti.return_item_id
            """;

        var lines = await _db.QueryAsync<PurchaseReturnDetailItemDto>(
            new CommandDefinition(linesSql, new { Id = returnId, Tid = tenantId }, cancellationToken: ct));
        header.Items = lines.ToList();
        return header;
    }

    public async Task DeletePurchaseReceiptAsync(string receiptId, string tenantId, CancellationToken ct = default)
    {
        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM purchase_receipts WHERE receipt_id=@Id AND tenant_id=@Tid",
            new { Id = receiptId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("매입명세서를 찾을 수 없습니다.");

        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("draft 상태만 삭제할 수 있습니다. 확정된 매입은 취소 처리가 필요합니다.");
        }

        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM purchase_receipt_items WHERE receipt_id=@Id AND tenant_id=@Tid",
                new { Id = receiptId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM purchase_receipts WHERE receipt_id=@Id AND tenant_id=@Tid",
                new { Id = receiptId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            await _audit.LogAsync("delete", "purchase_receipt", receiptId, ct: ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[PurchaseService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }
}
