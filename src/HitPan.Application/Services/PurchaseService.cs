using System.Data;
using Dapper;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _db;

    public PurchaseService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant, IDbConnection db)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
    }

    public async Task<string> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var poRepo = _unitOfWork.Repository<PurchaseOrder>();
        var poItemRepo = _unitOfWork.Repository<PurchaseOrderItem>();

        var now = DateTime.UtcNow;
        var date = request.PoDate == default ? now.Date : request.PoDate.Date;
        var prefix = $"PO-{date:yyyyMMdd}-";
        var todayOrders = await poRepo.FindAsync(x => x.PoNo.StartsWith(prefix));
        var poNo = $"{prefix}{todayOrders.Count + 1:000}";

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
        return poId;
    }

    public async Task<string> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        var receiptRepo = _unitOfWork.Repository<PurchaseReceipt>();
        var receiptItemRepo = _unitOfWork.Repository<PurchaseReceiptItem>();

        var now = DateTime.UtcNow;
        var date = request.ReceiptDate == default ? now.Date : request.ReceiptDate.Date;
        var prefix = $"PR-{date:yyyyMMdd}-";
        var todayReceipts = await receiptRepo.FindAsync(x => x.ReceiptNo.StartsWith(prefix));
        var receiptNo = $"{prefix}{todayReceipts.Count + 1:000}";

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

        foreach (var line in receiptItems)
        {
            var ledger = new StockLedger
            {
                LedgerId = 0,
                TenantId = receipt.TenantId,
                ItemId = line.ItemId,
                WarehouseId = line.WarehouseId,
                PartnerId = receipt.PartnerId,
                LedgerDate = receipt.ReceiptDate,
                Ym = receipt.ReceiptDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.In,
                SourceType = string.IsNullOrWhiteSpace(receipt.PoId) ? "direct_purchase" : "purchase_receipt",
                SourceId = receipt.ReceiptId,
                DocNo = receipt.ReceiptNo,
                QtyIn = line.Qty,
                QtyOut = 0m,
                UnitCost = line.UnitPrice,
                SupplyAmount = line.SupplyAmount
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

        await _unitOfWork.SaveChangesAsync(ct);
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
            throw new InvalidOperationException("전환 가능한 미입고 품목이 없습니다.");
        }

        // 창고 Id가 비어 있는 라인은 기본 창고로 채운다.
        foreach (var item in receiptItems.Where(x => string.IsNullOrWhiteSpace(x.WarehouseId)))
        {
            // 기본 창고 조회: warehouses 테이블의 첫 번째 활성 창고를 사용한다.
            var defaultWh = await _db.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    "SELECT warehouse_id FROM warehouses WHERE tenant_id = @TenantId AND is_active = 1 LIMIT 1",
                    new { TenantId = tenantId },
                    cancellationToken: ct));

            item.WarehouseId = defaultWh ?? "MAIN";
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

        // 반품 문서번호 채번
        var today = DateTime.UtcNow.Date;
        var prefix = $"RT-{today:yyyyMMdd}-";
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
}
