using System.Data;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Events;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace HitPan.Application.Services;

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _db;
    private readonly IPartnerService _partnerService;
    private readonly IAuditService _audit;
    private readonly IServiceProvider _services;

    public SalesService(
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant,
        IDbConnection db,
        IPartnerService partnerService,
        IAuditService audit,
        IServiceProvider services,
        // 작(2026-08-13) 단계2: 견적·수주·거래명세서 결재도 같은 결재함에 뜬다.
        INotificationService? notifier = null)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
        _partnerService = partnerService;
        _audit = audit;
        _services = services;
        _notifier = notifier;
    }

    private readonly INotificationService? _notifier;

    public async Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        var orderRepo = _unitOfWork.Repository<SalesOrder>();
        var itemRepo = _unitOfWork.Repository<SalesOrderItem>();

        var date = request.OrderDate == default ? DateTime.UtcNow.Date : request.OrderDate.Date;
        // WO-11: 한글 prefix 통일 (수주서 = 수-)
        var prefix = $"수-{date:yyyyMMdd}-";
        // 작20260428이7 P0-A: EF 캐시 우회 + UNIQUE 충돌 방지 (DB MAX 직조회).
        var orderNo = await DocumentNumberHelper.NextNumberAsync(
            _db, _currentTenant.TenantId, "sales_orders", "order_no", prefix, ct);

        var orderId = Guid.NewGuid().ToString();
        var order = new SalesOrder
        {
            Id = orderId,
            OrderId = orderId,
            TenantId = _currentTenant.TenantId,
            OrderNo = orderNo,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            OrderDate = date,
            DeliveryDate = request.DeliveryDate,
            Status = SalesOrderStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo,

            // 20260825작5: 전표 작성자 기록 (created_by = user_id 체계, 사장님 결재).
            CreatedBy = _currentTenant.UserId
        };
        await orderRepo.AddAsync(order);

        foreach (var line in request.Items)
        {
            await itemRepo.AddAsync(new SalesOrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderItemId = Guid.NewGuid().ToString(),
                OrderId = orderId,
                TenantId = _currentTenant.TenantId,
                ItemId = line.ItemId,
                OrderedQty = line.OrderedQty,
                DeliveredQty = 0m,
                UnitPrice = line.UnitPrice,
                SupplyAmount = line.SupplyAmount,
                VatAmount = line.VatAmount,
                ItemStatus = "pending"
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // 감사로그 — 수주서 생성
        var soAfterJson = $"{{\"order_no\":\"{orderNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "sales_order", orderId, afterJson: soAfterJson, ct: ct);

        return orderId;
    }

    public async Task<(string Id, string DocumentNumber, string? AutoCreatedOrderNo)> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerId))
        {
            throw new InvalidOperationException("거래처를 선택해주세요.");
        }

        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }

        // 폐기 (2026-08-25, 20260825작1, 사장님 결재): 1+1 기획상품(promo) 자동 2배 처리 제거.
        //   사장님 원문: "1+1기획은 할인프로모션인데, 두배가격으로 자동조정된다면
        //   고객사의고객사 입장에서 두개주문하지. 1+1을 구매할 이유가 없고,
        //   히트판 이용고객도 바보가 아닌이상 가격을 그렇게 할 리가 없음."
        //   종전 로직은 Qty 와 함께 SupplyAmount·VatAmount 까지 2배로 만들어(구 :1256-1259)
        //   1+1 인데 값을 두 배 받는 정반대 동작이었다. 할인이 아니라 인상이다.
        //   1+1 은 BOM 으로 구현한다(사장님: "이건 BOM으로 구현 가능해").
        //   배포 전이라 promo 로 등록된 고객 데이터 0건 — 회귀 위험 없음.
        //   ⚠️ item_type='promo' 데이터는 지우지 않는다(#1·#37). 화면 선택지만 없앤다.

        var deliveryRepo = _unitOfWork.Repository<SalesDelivery>();
        var itemRepo = _unitOfWork.Repository<SalesDeliveryItem>();

        // 다창고 정합 봉합(13차 후순위→봉합): 매입·BOM·매입반품과 동일하게 기본창고(MAIN) 우선 선택.
        // 기존 ORDER BY warehouse_id 는 알파벳순이라 다창고 환경에서 MAIN 아닌 창고가 선택되어
        // 판매 재고가 엉뚱한 창고에서 차감되는 비대칭(헌법 #20 워크플로우 정합). 단창고 환경은 동작 불변.
        const string whSql = """
                             SELECT warehouse_id
                             FROM warehouses
                             WHERE tenant_id = @TenantId
                               AND is_active = 1
                             ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                             LIMIT 1
                             """;

        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(whSql, new { TenantId = _currentTenant.TenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(defaultWarehouseId))
        {
            throw new InvalidOperationException("등록된 창고가 없습니다.");
        }

        var date = request.DeliveryDate == default ? DateTime.UtcNow.Date : request.DeliveryDate.Date;
        // WO-11: 한글 prefix 통일 (거래명세서 = 명-)
        var prefix = $"명-{date:yyyyMMdd}-";
        // 작20260428이7 P0-A: EF 캐시 우회 + UNIQUE 충돌 방지 (DB MAX 직조회).
        var deliveryNo = await DocumentNumberHelper.NextNumberAsync(
            _db, _currentTenant.TenantId, "sales_deliveries", "delivery_no", prefix, ct);

        // 다이렉트 판매(수주 없이 바로 거래명세서) → 정합성을 위해 수주 자동생성(closed 상태)
        // 20260825작5: 자동 생성했을 때만 수주번호를 담아 호출자에게 돌려준다.
        // 종전에는 (deliveryId, deliveryNo) 만 반환해 화면이 진짜 수주번호를 알 길이 없었고,
        // 그래서 브레드크럼이 "수-yyyyMMdd-001" 을 문자열로 지어내 항상 -001 로 보였다.
        string? autoCreatedOrderNo = null;
        var linkedOrderId = request.OrderId;
        if (string.IsNullOrWhiteSpace(linkedOrderId))
        {
            // 🔴 20260827작9 W4 — 자동생성 멱등 가드. 사장님: "사슬동작중 중복생성 절대금지".
            //   종전에는 OrderId 가 비었는지"만" 보고 곧바로 수주서를 만들었다.
            //   같은 거래명세서 저장이 두 번 타면(재시도·더블클릭·네트워크 반복) 수주서가 두 장 생겼다.
            //
            //   🔴 이건 자동발주에서 이미 한 번 난 사고와 같은 모양이다 —
            //      "is_auto=1 은 쓰기만 하고 읽지 않았다"(20260825작1 W2-0-B).
            //      표식을 남기기만 하고 되읽지 않으면 멱등이 성립하지 않는다.
            //      그래서 여기서는 is_auto=1 을 **읽어서** 재사용한다.
            //
            //   ⚠️ 재사용 조건을 좁게 잡는다 — 같은 테넌트·거래처·일자의 "자동생성분이면서
            //      아직 거래명세서가 붙지 않은" 수주서만. 이미 다른 명세서가 물린 수주서를
            //      재사용하면 서로 다른 거래가 한 수주서에 얽힌다(그게 더 큰 사고다).
            var reusableOrderId = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                """
                SELECT o.order_id
                  FROM sales_orders o
                 WHERE o.tenant_id  = @Tid
                   AND o.partner_id = @Pid
                   AND o.order_date = @Date
                   AND o.is_auto    = 1
                   AND o.is_deleted = 0
                   AND NOT EXISTS (SELECT 1 FROM sales_deliveries d
                                    WHERE d.order_id  = o.order_id
                                      AND d.tenant_id = o.tenant_id)
                 LIMIT 1
                """,
                new { Tid = _currentTenant.TenantId, Pid = request.PartnerId, Date = date },
                cancellationToken: ct));

            if (reusableOrderId is not null)
            {
                // 이미 만들어 둔 고아 자동수주서가 있다 — 새로 만들지 않고 그것에 붙인다.
                linkedOrderId = reusableOrderId;
                autoCreatedOrderNo = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                    "SELECT order_no FROM sales_orders WHERE order_id=@Oid AND tenant_id=@Tid",
                    new { Oid = reusableOrderId, Tid = _currentTenant.TenantId },
                    cancellationToken: ct));
            }
            else
            {

            var orderRepo2 = _unitOfWork.Repository<SalesOrder>();
            var orderItemRepo2 = _unitOfWork.Repository<SalesOrderItem>();

            var orderPrefix = $"수-{date:yyyyMMdd}-";
            var autoOrderNo = await DocumentNumberHelper.NextNumberAsync(
                _db, _currentTenant.TenantId, "sales_orders", "order_no", orderPrefix, ct);

            autoCreatedOrderNo = autoOrderNo;

            linkedOrderId = Guid.NewGuid().ToString();
            await orderRepo2.AddAsync(new SalesOrder
            {
                Id = linkedOrderId,
                OrderId = linkedOrderId,
                TenantId = _currentTenant.TenantId,
                OrderNo = autoOrderNo,
                PartnerId = request.PartnerId,
                EmployeeId = request.EmployeeId,
                OrderDate = date,
                DeliveryDate = date,
                Status = SalesOrderStatus.Closed,
                TotalAmount = request.Items.Sum(x => x.SupplyAmount),
                VatAmount = request.Items.Sum(x => x.VatAmount),
                Memo = request.Memo,
                IsAuto = true,

                // 20260825작5: 자동 생성 수주서도 작성자를 남긴다 — 거래명세서를 친 사람이 곧 작성자다.
                CreatedBy = _currentTenant.UserId
            });

            foreach (var line in request.Items)
            {
                await orderItemRepo2.AddAsync(new SalesOrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderItemId = Guid.NewGuid().ToString(),
                    OrderId = linkedOrderId,
                    TenantId = _currentTenant.TenantId,
                    ItemId = line.ItemId?.Trim() ?? string.Empty,
                    OrderedQty = line.Qty,
                    DeliveredQty = line.Qty,
                    UnitPrice = line.UnitPrice,
                    SupplyAmount = line.SupplyAmount,
                    VatAmount = line.VatAmount,
                    ItemStatus = "closed"
                });
            }
            } // else — 재사용할 자동수주서가 없어 새로 만든 경우 (W4)
        }

        var deliveryId = Guid.NewGuid().ToString();
        var delivery = new SalesDelivery
        {
            Id = deliveryId,
            DeliveryId = deliveryId,
            TenantId = _currentTenant.TenantId,
            DeliveryNo = deliveryNo,
            OrderId = linkedOrderId,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            DeliveryDate = date,
            SourceType = string.IsNullOrWhiteSpace(request.OrderId) ? "direct" : "from_order",
            Status = SalesDeliveryStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo,

            // 20260825작5: 전표 작성자 기록. 사장님 결재 — created_by 는 user_id 체계로 통일한다.
            // 현황·순위표·분석의 사원별 집계가 이미 e.user_id = created_by 로 조인하고 있어 그 전제를 따른다.
            // employee_id(담당 영업사원)와는 의미가 다르다 — 이 값은 "누가 이 전표를 쳤나"다.
            CreatedBy = _currentTenant.UserId
        };
        await deliveryRepo.AddAsync(delivery);

        foreach (var line in request.Items)
        {
            var warehouseId = string.IsNullOrWhiteSpace(line.WarehouseId) ? defaultWarehouseId : line.WarehouseId;

            var itemId = line.ItemId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemId))
            {
                var name = line.ItemName?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException("품목 ID 또는 품명이 필요합니다.");
                }

                const string itemResolveSql = """
                                              SELECT item_id
                                              FROM items
                                              WHERE tenant_id = @TenantId
                                                AND item_name = @ItemName
                                                AND is_active = 1
                                              ORDER BY item_id
                                              LIMIT 1
                                              """;

                itemId = await _db.QueryFirstOrDefaultAsync<string>(
                             new CommandDefinition(
                                 itemResolveSql,
                                 new { TenantId = _currentTenant.TenantId, ItemName = name },
                                 cancellationToken: ct))
                         ?? throw new InvalidOperationException($"등록된 품목을 찾을 수 없습니다: {name}");
            }

            await itemRepo.AddAsync(new SalesDeliveryItem
            {
                Id = Guid.NewGuid().ToString(),
                DeliveryItemId = Guid.NewGuid().ToString(),
                DeliveryId = deliveryId,
                TenantId = _currentTenant.TenantId,
                OrderItemId = line.OrderItemId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                SupplyAmount = line.SupplyAmount,
                VatAmount = line.VatAmount
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // 감사로그 — 거래명세서 생성 (초안)
        var delAfterJson = $"{{\"delivery_no\":\"{deliveryNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "sales_delivery", deliveryId, afterJson: delAfterJson, ct: ct);

        return (deliveryId, deliveryNo, autoCreatedOrderNo);
    }

    public async Task ConfirmDeliveryAsync(string deliveryId, ConfirmDeliveryRequest request, CancellationToken ct = default)
    {
        var deliveryRepo = _unitOfWork.Repository<SalesDelivery>();
        var deliveryItemRepo = _unitOfWork.Repository<SalesDeliveryItem>();
        var orderItemRepo = _unitOfWork.Repository<SalesOrderItem>();
        var workflowRepo = _unitOfWork.Repository<WorkflowSetting>();
        var ledgerRepo = _unitOfWork.Repository<StockLedger>();

        var delivery = await deliveryRepo.GetByIdAsync(deliveryId)
            ?? throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");
        if (delivery.Status != SalesDeliveryStatus.Draft)
        {
            throw new InvalidOperationException("draft 상태 전표만 확정할 수 있습니다.");
        }

        // 합계 0원 판매는 확정 금지 — journal_lines CHECK 제약 위반 방지(§20 워크플로우 오염 차단).
        if (delivery.TotalAmount + delivery.VatAmount <= 0m)
        {
            throw new InvalidOperationException("합계가 0원인 거래명세서는 확정할 수 없습니다. 품목·수량·단가를 확인해주세요.");
        }

        // 월마감 체크 — 마감된 월의 전표 확정 차단
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, delivery.TenantId, delivery.DeliveryDate, ct);

        var lines = await deliveryItemRepo.FindAsync(x => x.DeliveryId == deliveryId);

        if (!string.IsNullOrWhiteSpace(delivery.OrderId))
        {
            var allowSetting = await workflowRepo.FindAsync(x => x.SettingKey == "sales.over_delivery_allow" && x.IsActive);
            var overDeliveryAllow = allowSetting.FirstOrDefault()?.SettingValue == "true";
            if (!overDeliveryAllow)
            {
                foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x.OrderItemId)))
                {
                    var orderItem = await orderItemRepo.GetByIdAsync(line.OrderItemId!);
                    if (orderItem is null)
                    {
                        throw new InvalidOperationException("매칭된 수주 라인을 찾을 수 없습니다.");
                    }

                    if (orderItem.DeliveredQty + line.Qty > orderItem.OrderedQty)
                    {
                        throw new InvalidOperationException("수주 잔량을 초과하여 출고할 수 없습니다.");
                    }
                }
            }
        }

        var negativeStockSetting = await workflowRepo.FindAsync(x => x.SettingKey == "stock.negative_stock_allow" && x.IsActive);
        var negativeStockAllow = negativeStockSetting.FirstOrDefault()?.SettingValue == "true";

        if (!negativeStockAllow)
        {
            foreach (var line in lines)
            {
                // 사장님 헌법 (2026-04-26): "재고로 판매 흐름이 막히면 안 된다"
                //   - 히트판 타겟 소기업 95%는 창고 1개. 창고 단위 필터로 막으면 판매 못 침.
                //   - 회사 합산(전 창고)으로 가용재고 판단. ledger 'out' 기표는 거래명세서 라인의
                //     warehouse_id 그대로 저장되어 DB 추적 유지(창고담당자·이송 데이터는 베타 이후 정리).
                //   - 다창고 고객용 Picking Strategy / 재고관리 모듈은 정식 버전 작지서로.
                // 봉합 (2026-06-23, 5차 전수조사 SALES-04 P1급): 종전엔 ledgerRepo.FindAsync 로 해당 품목의
                //   stock_ledger 전 행을 메모리로 로드한 뒤 C# Sum 했다. stock_ledger 는 INSERT ONLY(절대원칙 #3)
                //   라 행이 영구 누적되어, 회전 빠른 품목은 수만~수십만 행 → 거래명세서 확정(hot path)마다
                //   전량 로드 = 대형 고객사(헌법 #26 2GB·30년)에서 메모리·지연 폭발. 코드베이스 표준(StockService
                //   :113)대로 서버측 SQL 집계로 교체한다. tenant_id 도 명시(EF 글로벌 필터 의존 제거).
                //   재고 검사는 이 확정의 OUT 원장 추가(아래 foreach) 전 시점이라 커밋된 잔량만 봐도 정합.
                var currentBalance = await _db.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_ledger WHERE tenant_id = @TenantId AND item_id = @ItemId",
                    new { TenantId = delivery.TenantId, ItemId = line.ItemId }, cancellationToken: ct));
                if (currentBalance - line.Qty < 0m)
                {
                    throw new InvalidOperationException("재고가 부족합니다.");
                }
            }
        }

        // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): stock_ledger UNIQUE 키 = (tenant_id, source_type, source_id,
        //   item_id, move_type) — warehouse·라인식별자 없음(품목단위 유일). 종전엔 라인별로 그대로 AddAsync 해,
        //   한 거래명세서에 같은 품목이 2라인(다른 창고·다른 단가) 들어가면 같은 키가 2번 INSERT → SaveChangesAsync
        //   UNIQUE 위반 → 거래 전체 롤백("재고 안 빠짐", 헌법 #20). 표준 데모는 통과하나 실사용 첫날 터지는 잠복형.
        //   봉합: INSERT 전 item_id 로 합산해 키당 1행만 기록(수량·금액 합산). warehouse 는 회사 합산 가용재고
        //   정책(위 317행 주석 — 다창고는 정식 버전)상 원장 키가 아니므로 대표 1개로 기록해도 무결성 손실 없음.
        //   단가는 라인별로 다를 수 있어 금액(SupplyAmount) 합을 그대로 보존하고, UnitCost 는 합산 단가(금액/수량)로 보정.
        foreach (var grp in lines.GroupBy(x => x.ItemId))
        {
            var first = grp.First();
            var qtySum = grp.Sum(x => x.Qty);
            var supplySum = grp.Sum(x => x.SupplyAmount);
            await ledgerRepo.AddAsync(new StockLedger
            {
                TenantId = delivery.TenantId,
                ItemId = grp.Key,
                WarehouseId = first.WarehouseId,
                PartnerId = delivery.PartnerId,
                EmployeeId = delivery.EmployeeId,
                LedgerDate = delivery.DeliveryDate,
                Ym = delivery.DeliveryDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.Out,
                SourceType = "sales_delivery",
                SourceId = delivery.DeliveryId,
                DocNo = delivery.DeliveryNo,
                QtyIn = 0m,
                QtyOut = qtySum,
                UnitCost = qtySum != 0m ? supplySum / qtySum : first.UnitPrice,
                SupplyAmount = supplySum
            });
        }

        // 조립상품(assembly) BOM 폭파 — 자재별 추가 OUT 원장 생성
        // 봉합 (2026-06-23, 5차 전수조사 SALES-03 P2): 완제품 재고 검사(311~)와 달리 조립 자재 소비에는
        //   음수재고 검사가 없어, negative_stock_allow=false 로 설정한 고객의 약속이 자재 경로에서 깨졌다.
        //   negativeStockAllow 를 넘겨 동일하게 검사하도록 한다(헌법 #20 무결성·#25 정확성).
        await ExplodeAssemblyBomAsync(delivery, lines, ledgerRepo, negativeStockAllow, ct);

        if (!string.IsNullOrWhiteSpace(delivery.OrderId))
        {
            foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x.OrderItemId)))
            {
                var orderItem = await orderItemRepo.GetByIdAsync(line.OrderItemId!);
                if (orderItem is null)
                {
                    continue;
                }

                orderItem.DeliveredQty += line.Qty;
                if (orderItem.DeliveredQty <= 0m)
                {
                    orderItem.ItemStatus = "pending";
                }
                else if (orderItem.DeliveredQty < orderItem.OrderedQty)
                {
                    orderItem.ItemStatus = "partial";
                }
                else
                {
                    orderItem.ItemStatus = "closed";
                }
                orderItemRepo.Update(orderItem);
            }
        }

        delivery.Status = SalesDeliveryStatus.Confirmed;
        deliveryRepo.Update(delivery);

        // ── 단일 트랜잭션 (EF + Dapper 공유) ──
        // 브라운킴 지적 듀얼 트랜잭션 해소: EF의 DbContext 트랜잭션을 시작하고
        // Dapper는 DbContext의 실연결·트랜잭션으로 실행 → 중간 실패 시 전체 롤백.
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 1) EF 변경 저장 (stock_ledger INSERT + status='confirmed' + order_items UPDATE)
            await _unitOfWork.SaveChangesAsync(ct);

            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 2) item_stock 차감 (Dapper · 동일 tx)
            foreach (var line in lines)
            {
                const string updateStockSql = """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty - @Qty,
                      last_updated_at = NOW(6)
                    """;

                await conn.ExecuteAsync(new CommandDefinition(
                    updateStockSql,
                    new
                    {
                        TenantId = delivery.TenantId,
                        ItemId = line.ItemId,
                        WarehouseId = line.WarehouseId,
                        Qty = line.Qty,
                        UnitCost = line.UnitPrice
                    },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 2-BOM) 조립 자재 item_stock 차감 — 봉합 (2026-06-23, 6차 전수조사 BOM-STOCK-ASYM P1):
            //   종전엔 ExplodeAssemblyBomAsync 가 자재 OUT 을 stock_ledger 에만 기록하고 item_stock 은
            //   차감하지 않아, 조립상품 판매 시 재고현황(item_stock 읽음)에 자재가 안 빠진 채 영구 부풀려졌다
            //   (헌법 #20). 방금 SaveChangesAsync(line 401)로 커밋된 이 delivery 의 bom_explosion OUT 원장을
            //   ★같은 EF 연결·같은 트랜잭션(conn=GetDbConnection, dbTx)으로★ 읽어 자재 item_stock 을 동일
            //   UPSERT 패턴으로 차감한다. _db(별개 연결)로 읽으면 미커밋 EF 원장이 안 보여 차감 0건(거짓 봉합)이
            //   되므로 반드시 conn+dbTx 로 읽는다(설계팀장 P0 지적). 비조립 상품은 원장 0건 → 차감 0회(회귀 없음).
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                SELECT UUID(), tenant_id, item_id, warehouse_id, -SUM(qty_out), 0, NOW(6)
                FROM stock_ledger
                WHERE tenant_id = @TenantId AND source_id = @DeliveryId AND source_type = 'bom_explosion'
                GROUP BY tenant_id, item_id, warehouse_id
                ON DUPLICATE KEY UPDATE
                  current_qty = current_qty + VALUES(current_qty),
                  last_updated_at = NOW(6)
                """,
                new { TenantId = delivery.TenantId, DeliveryId = delivery.DeliveryId },
                transaction: dbTx,
                cancellationToken: ct));

            // 2-A) 수주서 헤더 status 동기화 — delivery.OrderId 가 있을 때만.
            // §절대원칙 #20 (워크플로우 끊김 금지): item_status 만 갱신하고 헤더가 'draft' 로 남으면
            // 수주 목록에 "임시저장"으로 보임 → 사용자 혼란 + 거래명세서 변환 재시도 시 잔량 0 차단.
            // 4/29 SO-20260428-001 사고 진범. PurchaseService PO 헤더 동기화와 동일 패턴.
            if (!string.IsNullOrWhiteSpace(delivery.OrderId))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE sales_orders so
                    LEFT JOIN (
                        SELECT order_id,
                               SUM(CASE WHEN item_status='closed'  THEN 1 ELSE 0 END) AS closed_cnt,
                               SUM(CASE WHEN item_status='partial' THEN 1 ELSE 0 END) AS partial_cnt,
                               COUNT(*) AS total_cnt
                        FROM sales_order_items
                        WHERE order_id = @OrderId
                        GROUP BY order_id
                    ) s ON s.order_id = so.order_id
                    SET so.status = CASE
                                       WHEN s.closed_cnt = s.total_cnt THEN 'closed'
                                       WHEN s.closed_cnt > 0 OR s.partial_cnt > 0 THEN 'partial'
                                       ELSE 'confirmed'
                                    END,
                        so.updated_at = NOW(6)
                    WHERE so.order_id = @OrderId AND so.tenant_id = @TenantId
                    """,
                    new { OrderId = delivery.OrderId, TenantId = delivery.TenantId },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 3) monthly_summary 매출 갱신 — 멱등 가드 (작4 P0-4, 동일 tx)
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx,
                tenantId: delivery.TenantId,
                date: delivery.DeliveryDate,
                sourceType: "delivery_confirmed",
                sourceId: delivery.DeliveryId,
                field: MonthlySummaryGuard.SummaryField.TotalSales,
                amount: delivery.TotalAmount,
                ct: ct);

            // 4) 회계 자동 기표 (차변 외상매출금 / 대변 매출+부가세예수금)
            await AutoJournalHelper.RecordSalesConfirmAsync(
                conn, dbTx,
                delivery.TenantId,
                delivery.DeliveryId,
                delivery.DeliveryNo,
                delivery.DeliveryDate,
                delivery.PartnerId,
                delivery.TotalAmount,
                delivery.VatAmount,
                delivery.EmployeeId,
                ct);

            // 5) partner_balance 매출 가산 — 트랜잭션 내부에서 처리 (RED-1 보강)
            //    이벤트 외부 발행 실패 시에도 partner_balance 정합성 보장.
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id,
                   total_sales, total_receipt, total_purchase, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId, @Amount, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales     = total_sales + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = delivery.TenantId, PartnerId = delivery.PartnerId,
                      Amount = delivery.TotalAmount },
                transaction: dbTx, cancellationToken: ct));

            // 6) 전체 커밋 — EF + Dapper 쓰기가 원자적으로 확정
            await tx.CommitAsync(ct);

            // 감사로그 (트랜잭션 밖)
            await _audit.LogAsync("confirm", "sales_delivery", deliveryId, ct: ct);

            // 7) 이벤트 발행 (트랜잭션 밖) — 안전재고 알림 전용
            //    partner_balance는 위 트랜잭션에서 이미 처리. 이벤트 실패해도 정합성 영향 없음.
            try
            {
                var events = _services.GetService<IEventPublisher>();
                if (events is not null)
                {
                    var evt = new DeliveryConfirmedEvent(
                        TenantId: delivery.TenantId,
                        DeliveryId: delivery.DeliveryId,
                        PartnerId: delivery.PartnerId,
                        SupplyAmount: delivery.TotalAmount,
                        VatAmount: delivery.VatAmount,
                        TotalAmount: delivery.TotalAmount + delivery.VatAmount,
                        Items: lines.Select(l => new DeliveryItemEvent(
                            ItemId: l.ItemId,
                            Qty: l.Qty,
                            UnitPrice: l.UnitPrice,
                            Amount: l.Qty * l.UnitPrice)).ToList());
                    await events.PublishAsync("delivery.confirmed", evt, ct);
                }
            }
            catch (Exception evtEx)
            {
                await _audit.LogAsync("event_failed", "sales_delivery", deliveryId,
                    reason: $"delivery.confirmed: {evtEx.Message}", ct: ct);
            }
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }

        // 결재 트리거: 결재 설정이 ON이면 결재 문서 자동 생성 (커밋 이후 실행)
        // 결재 트리거 실패는 거래 확정에 영향 없음 — 이미 커밋된 원장은 유효
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "delivery", delivery.DeliveryId, delivery.DeliveryNo,
                $"거래명세서 확정: {delivery.DeliveryNo}",
                delivery.TotalAmount + delivery.VatAmount,
                delivery.TenantId, delivery.EmployeeId ?? "system", "확정자", ct, _notifier);
        }
        catch (Exception ex)
        {
            // 결재 트리거 실패는 원장 무결성과 무관 — 로그만 남기고 무시
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 거래명세서 {delivery.DeliveryNo} 결재 트리거 실패: {ex.Message}");
        }
    }

    public async Task<DeliveryDetailDto?> GetDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               d.delivery_id AS DeliveryId,
                               d.delivery_no AS DeliveryNo,
                               d.delivery_date AS OrderDate,
                               d.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (d.total_amount + d.vat_amount) AS TotalAmount,
                               d.vat_amount AS VatAmount,
                               d.total_amount AS SupplyAmount,
                               d.status AS Status,
                               d.memo AS Memo,
                               CAST(0 AS DECIMAL(15,2)) AS CashAmount,
                               CAST(0 AS DECIMAL(15,2)) AS CardAmount,
                               CAST(0 AS DECIMAL(15,2)) AS DiscountAmount,
                               d.employee_id AS EmployeeId,
                               e.emp_name AS EmployeeName,
                               o.order_no AS LinkedOrderNo,
                               ec.emp_name AS CreatedByName
                           FROM sales_deliveries d
                           LEFT JOIN partners p
                               ON p.partner_id = d.partner_id
                                  AND p.tenant_id = d.tenant_id
                           LEFT JOIN employees e
                               ON e.employee_id = d.employee_id
                                  AND e.tenant_id = d.tenant_id
                           LEFT JOIN sales_orders o
                               ON o.order_id = d.order_id
                                  AND o.tenant_id = d.tenant_id
                           LEFT JOIN employees ec
                               ON ec.user_id = d.created_by
                                  AND ec.tenant_id = d.tenant_id
                           WHERE d.delivery_id = @DeliveryId
                             AND d.tenant_id = @TenantId
                           """;

        var delivery = await _db.QueryFirstOrDefaultAsync<DeliveryDetailDto>(
            new CommandDefinition(sql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct));

        if (delivery is null)
        {
            return null;
        }

        const string itemSql = """
                               SELECT
                                   di.delivery_item_id AS DeliveryItemId,
                                   di.item_id AS ItemId,
                                   it.item_name AS ItemName,
                                   CAST(NULL AS CHAR(100)) AS Spec,
                                   it.unit AS Unit,
                                   di.qty AS Qty,
                                   di.unit_price AS UnitPrice,
                                   di.supply_amount AS Amount,
                                   di.vat_amount AS VatAmount,
                                   di.warehouse_id AS WarehouseId,
                                   CAST(NULL AS CHAR(500)) AS Memo,
                                   0 AS RowNo
                               FROM sales_delivery_items di
                               LEFT JOIN items it
                                   ON it.item_id = di.item_id
                                      AND it.tenant_id = di.tenant_id
                               WHERE di.delivery_id = @DeliveryId
                                 AND di.tenant_id = @TenantId
                               ORDER BY di.delivery_item_id
                               """;

        var items = (await _db.QueryAsync<DeliveryItemDto>(
                new CommandDefinition(itemSql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct)))
            .ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].RowNo = i + 1;
        }

        delivery.Items = items;

        const string balanceSql = """
                                  SELECT COALESCE(receivable_balance, 0)
                                  FROM v_partner_balance
                                  WHERE partner_id = @PartnerId
                                    AND tenant_id = @TenantId
                                  """;

        delivery.PrevReceivable = await _db.QueryFirstOrDefaultAsync<decimal>(
            new CommandDefinition(balanceSql, new { delivery.PartnerId, TenantId = tenantId }, cancellationToken: ct));

        const string todaySql = """
                                SELECT COALESCE(SUM(d.total_amount + d.vat_amount), 0)
                                FROM sales_deliveries d
                                WHERE d.tenant_id = @TenantId
                                  AND d.partner_id = @PartnerId
                                  AND d.delivery_date = CURDATE()
                                  AND d.status <> 'cancelled'
                                """;

        delivery.TodaySales = await _db.QueryFirstOrDefaultAsync<decimal>(
            new CommandDefinition(todaySql, new { TenantId = tenantId, delivery.PartnerId }, cancellationToken: ct));

        delivery.TodayReceipt = 0m;
        return delivery;
    }

    public async Task<List<DeliveryListDto>> GetDeliveriesAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               d.delivery_id AS DeliveryId,
                               d.delivery_no AS DeliveryNo,
                               d.delivery_date AS OrderDate,
                               d.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (d.total_amount + d.vat_amount) AS TotalAmount,
                               d.vat_amount AS VatAmount,
                               d.total_amount AS SupplyAmount,
                               d.status AS Status,
                               d.memo AS Memo,
                               ec.emp_name AS CreatedByName
                           FROM sales_deliveries d
                           LEFT JOIN partners p
                               ON p.partner_id = d.partner_id
                                  AND p.tenant_id = d.tenant_id
                           LEFT JOIN employees ec
                               ON ec.user_id = d.created_by
                                  AND ec.tenant_id = d.tenant_id
                           WHERE d.tenant_id = @TenantId
                             AND (d.is_deleted = 0 OR d.is_deleted IS NULL)
                             AND d.status <> 'cancelled'
                             AND (@From IS NULL OR d.delivery_date >= @From)
                             AND (@To IS NULL OR d.delivery_date <= @To)
                             AND (@PartnerName IS NULL OR p.partner_name LIKE CONCAT('%', @PartnerName, '%'))
                             AND (@Status IS NULL OR d.status = @Status)
                           ORDER BY d.delivery_date DESC,
                                    d.delivery_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<DeliveryListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    PartnerName = partnerName,
                    Status = status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task UpdateDeliveryAsync(
        string deliveryId,
        UpdateDeliveryDto dto,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        const string assertSql = """
                                 SELECT status
                                 FROM sales_deliveries
                                 WHERE delivery_id = @DeliveryId
                                   AND tenant_id = @TenantId
                                 """;

        var status = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(assertSql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct));

        if (status is null)
        {
            throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");
        }

        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("draft 상태 전표만 수정할 수 있습니다.");
        }

        if (dto.Items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }

        // 폐기 (2026-08-25, 20260825작1, 사장님 결재): 1+1 자동 2배 제거 — 생성 경로(:112)와 동일 사유.
        //   두 경로를 함께 뺀다. 한쪽만 빼면 "만들 때와 고칠 때 수량이 달라지는" 더 나쁜 상태가 된다.

        // 다창고 정합 봉합(13차 후순위→봉합): 매입·BOM·매입반품과 동일하게 기본창고(MAIN) 우선 선택.
        // 기존 ORDER BY warehouse_id 는 알파벳순이라 다창고 환경에서 MAIN 아닌 창고가 선택되어
        // 판매 재고가 엉뚱한 창고에서 차감되는 비대칭(헌법 #20 워크플로우 정합). 단창고 환경은 동작 불변.
        const string whSql = """
                             SELECT warehouse_id
                             FROM warehouses
                             WHERE tenant_id = @TenantId
                               AND is_active = 1
                             ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                             LIMIT 1
                             """;

        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(whSql, new { TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(defaultWarehouseId))
        {
            throw new InvalidOperationException("등록된 창고가 없습니다.");
        }

        var supplyAmount = dto.Items.Sum(x => x.Amount);
        var vatAmount = dto.Items.Sum(x => x.VatAmount);

        const string updateSql = """
                                 UPDATE sales_deliveries SET
                                     delivery_date = @OrderDate,
                                     partner_id = @PartnerId,
                                     memo = @Memo,
                                     total_amount = @SupplyAmount,
                                     vat_amount = @VatAmount,
                                     updated_at = NOW(6),
                                     updated_by = @UserId
                                 WHERE delivery_id = @DeliveryId
                                   AND tenant_id = @TenantId
                                   AND status = 'draft'
                                 """;

        // 트랜잭션으로 헤더 UPDATE + 품목 DELETE/INSERT를 원자적으로 묶는다.
        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(updateSql,
                new
                {
                    DeliveryId = deliveryId,
                    TenantId = tenantId,
                    OrderDate = dto.OrderDate.Date,
                    PartnerId = dto.PartnerId,
                    Memo = dto.Memo,
                    SupplyAmount = supplyAmount,
                    VatAmount = vatAmount,
                    UserId = string.IsNullOrEmpty(userId) ? null : userId
                },
                transaction: tx, cancellationToken: ct));

            // 🔴 20260827작10 W1 — 지우기 전에 수주라인 링크를 읽어둔다.
            //   종전엔 라인을 전량 DELETE 하고 order_item_id 에 NULL 을 하드코딩해 다시 넣었다.
            //   ⇒ draft 거래명세서를 한 번만 수정 저장하면 수주라인 링크가 영구히 끊겼다.
            //
            //   🔴 이건 단순 데이터 손실이 아니다 — **가드가 뚫린다.**
            //      DeleteSalesOrderAsync 의 "판매전환된 라인 차단" 가드가 order_item_id 로 판정한다.
            //      링크가 NULL 이 되면 그 COUNT 가 0 이라 **이미 출고된 수주서가 삭제 가능해진다.**
            //
            //   🔴 화면이 다시 보내주는 방식으로는 못 고친다 —
            //      UpdateDeliveryAsync 가 받는 DeliveryItemDto 에는 OrderItemId 가 없고,
            //      조회 SQL 도 order_item_id 를 한 번도 읽지 않는다(실측).
            //      **화면은 받은 적 없는 값을 되돌려줄 수 없다.**
            //      작7 이 delivery_item_id 로 똑같이 겪은 사고다 — 한 계층 위에서 반복됐다.
            //      ⇒ 그래서 **서버가 보존한다.** 화면에 의존하지 않는다.
            var keepOrderItemIds = (await _db.QueryAsync<(string DeliveryItemId, string? OrderItemId)>(
                new CommandDefinition(
                    """
                    SELECT delivery_item_id, order_item_id
                      FROM sales_delivery_items
                     WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId
                       AND order_item_id IS NOT NULL
                    """,
                    new { DeliveryId = deliveryId, TenantId = tenantId },
                    transaction: tx, cancellationToken: ct)))
                .ToDictionary(r => r.DeliveryItemId, r => r.OrderItemId);

            await _db.ExecuteAsync(new CommandDefinition(
                "DELETE FROM sales_delivery_items WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId",
                new { DeliveryId = deliveryId, TenantId = tenantId },
                transaction: tx, cancellationToken: ct));

            foreach (var item in dto.Items)
            {
                // 🔴 W1 — 원래 이 줄이 물고 있던 수주라인을 되붙인다.
                //   화면이 보내온 delivery_item_id 로 되짚는다(그 값은 DTO 에 있다 — 작7 이 뚫어놨다).
                //   ⚠️ 새로 추가된 줄은 delivery_item_id 가 없거나 사전에 없던 값이다 ⇒ NULL 이 정상.
                //      여기서 억지로 채우면 없던 사슬을 지어내는 것이라 더 나쁘다.
                string? keptOrderItemId = null;
                if (!string.IsNullOrWhiteSpace(item.DeliveryItemId))
                {
                    keepOrderItemIds.TryGetValue(item.DeliveryItemId!, out keptOrderItemId);
                }

                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO sales_delivery_items
                        (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id,
                         qty, unit_price, supply_amount, vat_amount)
                    VALUES
                        (@DeliveryItemId, @DeliveryId, @TenantId, @OrderItemId, @ItemId, @WarehouseId,
                         @Qty, @UnitPrice, @SupplyAmount, @VatAmount)
                    """,
                    new
                    {
                        DeliveryItemId = Guid.NewGuid().ToString(),
                        DeliveryId = deliveryId,
                        TenantId = tenantId,
                        OrderItemId = keptOrderItemId,
                        item.ItemId,
                        WarehouseId = defaultWarehouseId,
                        item.Qty,
                        item.UnitPrice,
                        SupplyAmount = item.Amount,
                        item.VatAmount
                    },
                    transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    public async Task DeleteDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default)
    {
        // 사장님 지시 (2026-04-26): 거래명세서는 전자계산서 발행 전이면 삭제 가능.
        //   - 권한은 컨트롤러에서 SalesManager 정책으로 이미 강제됨.
        //   - tax_invoices 에 delivery_id 발행 레코드 있으면 거부 (감사·세무 무결성).
        //   - status='draft'    → cancelled 표시
        //   - status='confirmed' → CancelConfirmedDeliveryAsync 로 Reverse 원장 발행
        //     (재고·원장·회계 모두 복귀, INSERT ONLY 원칙 유지).

        var invoiced = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM tax_invoices WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct));
        if (invoiced > 0)
        {
            throw new InvalidOperationException("전자계산서가 발행된 거래명세서는 삭제할 수 없습니다. 계산서를 먼저 취소해주세요.");
        }

        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM sales_deliveries WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct));
        if (string.IsNullOrEmpty(status)) return; // 이미 없음

        if (string.Equals(status, "confirmed", StringComparison.OrdinalIgnoreCase))
        {
            // 확정된 거래는 Reverse 경로 — 재고·잔액·회계 무결성 유지.
            await CancelConfirmedDeliveryAsync(deliveryId, tenantId, employeeId: null, ct);
            return;
        }

        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE sales_deliveries
                SET status = 'cancelled',
                    updated_at = NOW(6)
                WHERE delivery_id = @DeliveryId
                  AND tenant_id = @TenantId
                  AND status = 'draft'
                """,
                new { DeliveryId = deliveryId, TenantId = tenantId },
                cancellationToken: ct));

        await _audit.LogAsync("delete", "sales_delivery", deliveryId, ct: ct);
    }

    /// <summary>
    /// 확정된 거래명세서 취소 — Reverse 원장 발행으로 재고·잔액 복귀.
    /// 원장은 INSERT ONLY 원칙을 유지하고 move_type='in'의 역행 원장을 새로 기록한다.
    /// 조립상품(BOM 폭파)도 자재 역행 IN으로 복귀시킨다.
    /// </summary>
    public async Task CancelConfirmedDeliveryAsync(string deliveryId, string tenantId, string? employeeId, CancellationToken ct = default)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT delivery_id, delivery_no, partner_id, delivery_date, status, total_amount, vat_amount FROM sales_deliveries WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");

        if ((string)header.status != "confirmed")
        {
            throw new InvalidOperationException("confirmed 상태만 취소할 수 있습니다. (draft은 삭제 사용)");
        }

        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }
        using var tx = _db.BeginTransaction();
        try
        {
            var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
                "SELECT item_id, warehouse_id, qty, unit_price, supply_amount FROM sales_delivery_items WHERE delivery_id=@Id AND tenant_id=@Tid",
                new { Id = deliveryId, Tid = tenantId }, transaction: tx, cancellationToken: ct))).ToList();

            DateTime dd = (DateTime)header.delivery_date;
            string ym = dd.ToString("yyyy-MM");

            // 1) 원본 완제품 OUT의 역행 IN 원장
            // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): 역행 원장도 stock_ledger UNIQUE 키
            //   (tenant, source_type=sales_cancel, source_id=deliveryId, item_id, move_type=in) 단위 유일.
            //   확정과 동일하게 같은 품목 2라인이면 라인별 INSERT 가 키를 2번 찍어 취소 자체가 차단됐다(헌법 #20).
            //   item_id 로 합산해 역행도 키당 1행만 기록(확정 OUT 합산과 대칭).
            var reverseGroups = items
                .GroupBy(it => (string)it.item_id)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Wh = (string)g.First().warehouse_id,
                    Qty = g.Sum(x => (decimal)x.qty),
                    Supply = g.Sum(x => (decimal)x.supply_amount),
                    UnitPrice = (decimal)g.First().unit_price
                });
            foreach (var it in reverseGroups)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger
                      (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
                       move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES
                      (@Tid, @ItemId, @Wh, @PartnerId, @EmpId, @Date, @Ym,
                       'in', 'sales_cancel', @Did, @DocNo, @Qty, 0, @UnitPrice, @Supply, '매출취소 Reverse')
                    """,
                    new
                    {
                        Tid = tenantId,
                        ItemId = it.ItemId,
                        Wh = it.Wh,
                        PartnerId = (string)header.partner_id,
                        EmpId = employeeId,
                        Date = dd, Ym = ym,
                        Did = deliveryId,
                        DocNo = (string)header.delivery_no,
                        Qty = it.Qty,
                        UnitPrice = it.UnitPrice,
                        Supply = it.Supply
                    },
                    transaction: tx, cancellationToken: ct));
            }

            // 2) BOM 폭파 자재의 역행 IN 원장 (조립상품 판매였다면)
            const string bomReverseSql = """
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, employee_id,
                  ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                SELECT @Tid, l.item_id, l.warehouse_id, l.partner_id, @EmpId,
                  @Date, @Ym, 'in', 'bom_explosion_cancel', @Did, @DocNo,
                  l.qty_out, 0, 0, 0, '조립취소 자재복귀'
                FROM stock_ledger l
                WHERE l.source_id=@Did AND l.source_type='bom_explosion' AND l.tenant_id=@Tid
                """;
            await _db.ExecuteAsync(new CommandDefinition(bomReverseSql,
                new
                {
                    Tid = tenantId,
                    EmpId = employeeId,
                    Date = dd, Ym = ym,
                    Did = deliveryId,
                    DocNo = (string)header.delivery_no
                },
                transaction: tx, cancellationToken: ct));

            // 3) item_stock 복귀 — 완제품
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                SELECT UUID(), @Tid, item_id, warehouse_id, qty, unit_price, NOW(6)
                FROM sales_delivery_items WHERE delivery_id=@Did AND tenant_id=@Tid
                ON DUPLICATE KEY UPDATE current_qty = current_qty + VALUES(current_qty), last_updated_at=NOW(6)
                """,
                new { Tid = tenantId, Did = deliveryId },
                transaction: tx, cancellationToken: ct));

            // 3-BOM) item_stock 복귀 — 조립 자재 (봉합 2026-06-23, 6차 BOM-STOCK-ASYM P1 대칭):
            //   확정 시 자재 item_stock 을 차감(2-BOM)하므로, 취소 시에도 반드시 자재 item_stock 을 복귀해야
            //   짝이 맞는다. 누락하면 취소할 때마다 자재가 영구 손실된다(헌법 #20 재위반). 원본 bom_explosion
            //   OUT 원장 기준으로 +SUM(qty_out) 복귀. 이 원장은 확정 시 이미 커밋된 과거 데이터라 _db+tx 로 조회 가능.
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                SELECT UUID(), tenant_id, item_id, warehouse_id, SUM(qty_out), 0, NOW(6)
                FROM stock_ledger
                WHERE tenant_id=@Tid AND source_id=@Did AND source_type='bom_explosion'
                GROUP BY tenant_id, item_id, warehouse_id
                ON DUPLICATE KEY UPDATE current_qty = current_qty + VALUES(current_qty), last_updated_at=NOW(6)
                """,
                new { Tid = tenantId, Did = deliveryId },
                transaction: tx, cancellationToken: ct));

            // 4) 연결된 수금(collections) 무효화 — ref_doc이 이 명세서인 수금 전부
            var voidedCollections = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE collections
                SET is_active=0, updated_at=NOW(6)
                WHERE tenant_id=@Tid AND ref_doc_type='sales_delivery' AND ref_doc_id=@Did AND is_active=1
                """,
                new { Tid = tenantId, Did = deliveryId },
                transaction: tx, cancellationToken: ct));

            // 5) partner_balance 재계산 (매출 차감 + 수금 역산)
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE partner_balance pb
                SET total_sales = COALESCE((SELECT SUM(total_amount) FROM sales_deliveries
                                            WHERE tenant_id=@Tid AND partner_id=@Pid AND status='confirmed'), 0),
                    total_receipt = COALESCE((SELECT SUM(amount) FROM collections
                                              WHERE tenant_id=@Tid AND partner_id=@Pid AND is_active=1
                                                AND ref_doc_type='sales_delivery'), 0),
                    last_updated_at = NOW(6)
                WHERE tenant_id=@Tid AND partner_id=@Pid
                """,
                new { Tid = tenantId, Pid = (string)header.partner_id },
                transaction: tx, cancellationToken: ct));

            // 6) 회계 역분개 — RecordSalesConfirmAsync 대칭 (차변 매출+부가세예수금 / 대변 외상매출금)
            if ((decimal)header.total_amount != 0m || (decimal)header.vat_amount != 0m)
            {
                await AutoJournalHelper.RecordSalesDeliveryCancelAsync(
                    _db, tx,
                    tenantId,
                    deliveryId,
                    (string)header.delivery_no,
                    dd,
                    (string)header.partner_id,
                    (decimal)header.total_amount,
                    (decimal)header.vat_amount,
                    employeeId,
                    ct);
            }

            // 7) monthly_summary 매출 역산 — ConfirmDeliveryAsync TryApplyAsync 대칭 차감
            // 봉합 (2026-06-23, 5차 전수조사 SALES-01 P1):
            //   종전엔 sourceId 를 확정과 동일한 deliveryId 로 호출했다. MonthlySummaryGuard 의 멱등 키는
            //   (tenant_id, source_type, source_id, field_name) UNIQUE 이므로, 확정 때 이미 그 키로
            //   monthly_summary_sources 행이 들어가 있어 취소 호출은 INSERT IGNORE 충돌(inserted==0) →
            //   return false 로 차감 SQL 자체가 실행되지 않았다. 결과: 확정 매출을 취소해도
            //   monthly_summary.total_sales 가 영영 안 줄어 월매출이 부풀려진 채 고정(헌법 #20 무결성 위반).
            //   해법: 취소 역산은 확정과 다른 sourceId("{deliveryId}:cancel")를 써 키를 분리한다.
            //   → -total 차감이 1회 정상 적용되고, 멱등도 유지(이중 취소 시 두번째는 같은 :cancel 키로 충돌·스킵).
            //   확정 측(457행)은 순수 deliveryId 유지 — 양측 키 분리가 봉합의 핵심이므로 동시 변경 금지.
            //   금액 출처: 확정=delivery.TotalAmount, 취소=header.total_amount. confirmed 상태에서
            //   total_amount 를 갱신하는 코드는 없어 두 값은 항상 일치(절대값 대칭 보장).
            await MonthlySummaryGuard.TryApplyAsync(
                _db, tx,
                tenantId: tenantId,
                date: dd,
                sourceType: "delivery_confirmed",
                sourceId: $"{deliveryId}:cancel",
                field: MonthlySummaryGuard.SummaryField.TotalSales,
                amount: -(decimal)header.total_amount,
                ct: ct);

            // 8) 상태 변경
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE sales_deliveries SET status='cancelled', updated_at=NOW(6) WHERE delivery_id=@Id AND tenant_id=@Tid",
                new { Id = deliveryId, Tid = tenantId },
                transaction: tx, cancellationToken: ct));

            tx.Commit();

            await _audit.LogAsync("cancel", "sales_delivery", deliveryId,
                beforeJson: $"{{\"status\":\"confirmed\"}}",
                afterJson: $"{{\"status\":\"cancelled\",\"reverse_ledger\":true}}", ct: ct);
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    public async Task<List<SalesOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               o.order_id AS OrderId,
                               o.order_no AS OrderNo,
                               o.order_date AS OrderDate,
                               o.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (o.total_amount + o.vat_amount) AS TotalAmount,
                               o.vat_amount AS VatAmount,
                               o.total_amount AS SupplyAmount,
                               o.status AS Status,
                               o.memo AS Memo,
                               ec.emp_name AS CreatedByName
                           FROM sales_orders o
                           LEFT JOIN partners p
                               ON p.partner_id = o.partner_id
                                  AND p.tenant_id = o.tenant_id
                           LEFT JOIN employees ec
                               ON ec.user_id = o.created_by
                                  AND ec.tenant_id = o.tenant_id
                           WHERE o.tenant_id = @TenantId
                             AND o.is_auto = 0
                             AND (@From IS NULL OR o.order_date >= @From)
                             AND (@To IS NULL OR o.order_date <= @To)
                             AND (@Status IS NULL OR o.status = @Status)
                           ORDER BY o.order_date DESC,
                                    o.order_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<SalesOrderListDto>(
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

    /// <summary>
    /// 수주서를 거래명세서로 전환한다. 미출고 품목이 없으면 차단한다.
    /// </summary>
    public async Task<(string DeliveryId, string DocumentNumber)> ConvertOrderToDeliveryAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default)
    {
        var orderRepo = _unitOfWork.Repository<SalesOrder>();
        var orderItemRepo = _unitOfWork.Repository<SalesOrderItem>();

        var order = await orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException("수주서를 찾을 수 없습니다.");

        if (order.TenantId != tenantId)
        {
            throw new InvalidOperationException("수주서를 찾을 수 없습니다.");
        }

        // 🔴 20260827작11 W1 — 같은 수주로 거래명세서를 두 번 뽑는 것을 막는다.
        //   사장님: "사슬동작중 중복생성 절대금지".
        //
        //   🔴 왜 아래 `deliveryItems.Count == 0` 만으로는 안 되나 —
        //      그 방어는 delivered_qty 가 올라간 뒤에만 듣는다.
        //      그런데 delivered_qty 는 **확정(ConfirmDeliveryAsync :459)** 에서만 증가한다.
        //      ⇒ 명세서를 만들고 **확정하기 전(draft)** 에 다시 전환하면
        //         delivered_qty 가 아직 0 이라 가드를 그대로 통과한다.
        //         수주 1건으로 명세서 2장이 나오고, 둘 다 확정하면 **재고가 2배 출고**된다.
        //
        //   🔴 매입은 이 사고를 이미 겪고 봉합했다(PurchaseService.cs:726-747):
        //      *"발주 1건으로 매입 2장을 만들 수 있었다(재고 2배 입고)"*.
        //      매출만 그 봉합을 안 받았다 — 같은 구멍이 그대로 남아 있었다.
        //
        //   ⇒ 매입과 대칭으로, **취소분만 빼고 살아있는 명세서가 하나라도 있으면 막는다.**
        //     번호를 알려준다 — 담당자가 무엇을 정리할지 알아야 한다(작8: 막는 것 ≠ 알려주는 것).
        //   ⚠️ 철자 — sales_deliveries 는 'cancelled'(l 둘)다. sales_returns('canceled')와 다르다.
        var existingDeliveryNo = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT delivery_no FROM sales_deliveries
             WHERE order_id = @OrderId AND tenant_id = @TenantId
               AND status <> 'cancelled'
               AND is_deleted = 0
             ORDER BY delivery_no
             LIMIT 1
            """,
            new { OrderId = orderId, TenantId = tenantId },
            cancellationToken: ct));
        if (existingDeliveryNo is not null)
        {
            throw new InvalidOperationException(
                $"이미 거래명세서({existingDeliveryNo})가 발행된 수주서입니다. " +
                "기존 거래명세서를 확인하세요.");
        }

        var items = await orderItemRepo.FindAsync(x => x.OrderId == orderId);
        var deliveryItems = items
            .Where(x => x.OrderedQty - x.DeliveredQty > 0)
            .Select(x => new CreateDeliveryItemRequest
            {
                OrderItemId = x.OrderItemId,
                ItemId = x.ItemId,
                Qty = x.OrderedQty - x.DeliveredQty,
                UnitPrice = x.UnitPrice,
                SupplyAmount = (x.OrderedQty - x.DeliveredQty) * x.UnitPrice,
                VatAmount = Math.Round((x.OrderedQty - x.DeliveredQty) * x.UnitPrice * 0.1m, 0)
            }).ToList();

        if (deliveryItems.Count == 0)
        {
            // 20260827작11 W1 — 매입(PurchaseService.cs:764-771)과 대칭.
            //   전 라인이 이미 출고 완료면 "품목이 없다" 보다 "이미 끝났다" 가 정확하다.
            //   담당자가 뭘 해야 하는지 알려준다.
            var allClosed = items.Any() && items.All(x => x.OrderedQty - x.DeliveredQty <= 0);
            throw new InvalidOperationException(allClosed
                ? "이미 출고가 완료된 수주서입니다. 거래명세서 목록에서 확인해 주세요."
                : "전환 가능한 미출고 품목이 없습니다.");
        }

        var request = new CreateDeliveryRequest
        {
            OrderId = orderId,
            PartnerId = order.PartnerId,
            EmployeeId = order.EmployeeId,
            DeliveryDate = DateTime.UtcNow.Date,
            Memo = $"수주 {order.OrderNo} 에서 전환",
            Items = deliveryItems
        };

        // 20260825작5: 이 경로는 OrderId 를 넘기므로 수주 자동생성이 일어나지 않는다.
        // 세 번째 값(AutoCreatedOrderNo)은 항상 null 이라 기존 계약 그대로 두 값만 돌려준다.
        var (deliveryId, documentNumber, _) = await CreateDeliveryAsync(request, ct);
        return (deliveryId, documentNumber);
    }

    public Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default)
    {
        return _partnerService.SearchPartnersAsync(tenantId, keyword, ct);
    }

    // 결재 트리거는 ApprovalTriggerHelper.TryCreateApprovalAsync로 통합됨

    // ─────────────────────────────────────────────────────────────────────
    // 폐기 (2026-08-25, 20260825작1, 사장님 결재): 1+1 기획상품(promo) 자동 2배 메서드 2개 제거.
    //   ApplyPromoDoubleAsync / ApplyPromoDoubleToUpdateAsync — 호출부(:112·:781)와 함께 뺐다.
    //   사유: qty 뿐 아니라 금액까지 2배로 만들어 "1+1 인데 값이 두 배"가 되는 정반대 동작이었다.
    //   1+1 은 BOM 으로 구현한다. 남겨두면 경고(미사용)가 나므로 함께 제거한다(#19 warnings 0).
    //   ⚠️ DB 의 item_type='promo' 값과 GetTypeLabel 의 라벨은 지우지 않는다(#1·#37) —
    //      과거 데이터가 있으면 화면에서 이름은 읽혀야 한다.

    // ─────────────────────────────────────────────────────────────────────
    // 조립상품 BOM 폭파: assembly 품목 출고 시 BOM 자재별 추가 OUT 원장 생성
    // 완제품 OUT 원장은 유지(추적용), 자재 OUT은 이곳에서 추가 기록.
    // ─────────────────────────────────────────────────────────────────────
    private async Task ExplodeAssemblyBomAsync(
        SalesDelivery delivery,
        IReadOnlyList<SalesDeliveryItem> lines,
        IRepository<StockLedger> ledgerRepo,
        bool negativeStockAllow,
        CancellationToken ct)
    {
        var itemIds = lines.Select(x => x.ItemId).Distinct().ToList();
        if (itemIds.Count == 0) return;

        const string assemblySql = "SELECT item_id FROM items WHERE tenant_id=@TenantId AND item_type='assembly' AND item_id IN @Ids";
        var assemblyIds = (await _db.QueryAsync<string>(
                              new CommandDefinition(assemblySql,
                                  new { TenantId = delivery.TenantId, Ids = itemIds },
                                  cancellationToken: ct))).ToHashSet();
        if (assemblyIds.Count == 0) return;

        const string bomSql = """
            SELECT bi.material_item_id AS MaterialItemId, bi.qty AS BomQty
            FROM bom_headers bh
            JOIN bom_items bi ON bi.bom_id = bh.bom_id
            WHERE bh.tenant_id=@TenantId
              AND bh.product_item_id=@ProductId
              AND bh.is_default=1
              AND bh.is_active=1
            """;

        // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): bom_explosion OUT 원장도 stock_ledger UNIQUE 키
        //   (tenant, source_type=bom_explosion, source_id=deliveryId, item_id, move_type=out) 단위 유일이다.
        //   종전엔 (조립라인 × 자재) 이중 루프로 자재별 OUT 을 라인마다 AddAsync 해, 공통 자재를 쓰는 조립품이
        //   2라인이거나 한 BOM 에 같은 자재가 2줄이면 같은 키가 2번 INSERT → SaveChangesAsync UNIQUE 위반 →
        //   거래 전체 롤백(헌법 #20). 봉합: 자재 소비량을 material_item_id 로 누적 합산한 뒤 자재당 1행만 기록.
        //   음수재고 검사도 합산 총소비량으로 1회 — 라인별 개별 검사가 잔량을 중복 소진 없이 각각 통과시키던 허점도 닫힘.
        var materialConsumption = new Dictionary<string, decimal>();
        var assemblyLines = lines.Where(l => assemblyIds.Contains(l.ItemId)).ToList();
        foreach (var line in assemblyLines)
        {
            var materials = await _db.QueryAsync<(string MaterialItemId, decimal BomQty)>(
                new CommandDefinition(bomSql,
                    new { TenantId = delivery.TenantId, ProductId = line.ItemId },
                    cancellationToken: ct));

            foreach (var m in materials)
            {
                materialConsumption.TryGetValue(m.MaterialItemId, out var acc);
                materialConsumption[m.MaterialItemId] = acc + line.Qty * m.BomQty;
            }
        }
        if (materialConsumption.Count == 0) return;

        // 대표 창고: 조립 라인 중 첫 라인의 창고(완제품 OUT 합산과 동일 — 회사 합산 가용재고 정책, 317행 주석).
        //   materialConsumption.Count>0 이면 자재를 만든 조립 라인이 반드시 존재하므로 assemblyLines 는 비어있지 않다.
        var bomWarehouseId = assemblyLines[0].WarehouseId;
        foreach (var (materialItemId, consumeQty) in materialConsumption)
        {
            // SALES-03 봉합: negative_stock_allow=false 면 자재도 회사 합산 잔량으로 음수재고 검사
            //   (완제품 검사 311~ 와 동일 정책·동일 SQL 집계). 합산 총소비량으로 검사 — 부족하면 확정 차단.
            if (!negativeStockAllow)
            {
                var matBalance = await _db.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_ledger WHERE tenant_id = @TenantId AND item_id = @ItemId",
                    new { TenantId = delivery.TenantId, ItemId = materialItemId }, cancellationToken: ct));
                if (matBalance - consumeQty < 0m)
                {
                    throw new InvalidOperationException("조립 자재 재고가 부족합니다.");
                }
            }

            await ledgerRepo.AddAsync(new StockLedger
            {
                TenantId = delivery.TenantId,
                ItemId = materialItemId,
                WarehouseId = bomWarehouseId,
                PartnerId = delivery.PartnerId,
                EmployeeId = delivery.EmployeeId,
                LedgerDate = delivery.DeliveryDate,
                Ym = delivery.DeliveryDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.Out,
                SourceType = "bom_explosion",
                SourceId = delivery.DeliveryId,
                DocNo = delivery.DeliveryNo,
                QtyIn = 0m,
                QtyOut = consumeQty,
                UnitCost = 0m,
                SupplyAmount = 0m,
                Memo = "조립 자재소비"
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 수주서 단건 조회 — 목록 클릭 → 편집 화면 로드용.
    // ─────────────────────────────────────────────────────────────────────
    public async Task<SalesOrderDetailDto?> GetOrderDetailAsync(
        string orderId, string tenantId, CancellationToken ct = default)
    {
        const string headerSql = """
            SELECT o.order_id     AS OrderId,
                   o.order_no     AS OrderNo,
                   o.order_date   AS OrderDate,
                   o.delivery_date AS DeliveryDate,
                   o.partner_id   AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   o.total_amount AS TotalAmount,
                   o.vat_amount   AS VatAmount,
                   o.status       AS Status,
                   o.memo         AS Memo
              FROM sales_orders o
              LEFT JOIN partners p
                ON p.partner_id = o.partner_id
               AND p.tenant_id  = o.tenant_id
             WHERE o.order_id  = @Id
               AND o.tenant_id = @Tid
               AND o.is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<SalesOrderDetailDto>(
            new CommandDefinition(headerSql, new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        if (header is null) return null;

        const string linesSql = """
            SELECT soi.order_item_id AS OrderItemId,
                   soi.item_id       AS ItemId,
                   COALESCE(i.item_name, '') AS ItemName,
                   COALESCE(i.spec, '')      AS Spec,
                   IFNULL(i.unit, 'EA')      AS Unit,
                   soi.ordered_qty   AS Qty,
                   soi.unit_price    AS UnitPrice,
                   soi.supply_amount AS SupplyAmount,
                   soi.vat_amount    AS VatAmount
              FROM sales_order_items soi
              LEFT JOIN items i
                ON i.item_id   = soi.item_id
               AND i.tenant_id = soi.tenant_id
             WHERE soi.order_id  = @Id
               AND soi.tenant_id = @Tid
             ORDER BY soi.order_item_id
            """;

        var lines = await _db.QueryAsync<SalesOrderDetailItemDto>(
            new CommandDefinition(linesSql, new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        header.Items = lines.ToList();
        return header;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 봉합 (2026-06-22, 11차전 수주재편집): 수주(draft) 헤더/라인 재편집.
    //   10차 P0-1은 신규저장만 api/sales/orders로 봉합했고 수정 경로(PUT)가 부재했다.
    //   그래서 프론트가 PUT api/sales/deliveries로 잘못 흘러 거래명세서 조회 실패
    //   → "거래명세서를 찾을 수 없습니다" 발생. 본 메서드로 수주 수정 경로를 신설.
    //   §절대원칙 #6: draft 상태만 수정 허용. confirmed/partial/closed/cancelled 차단.
    //   UpdateDeliveryAsync와 동일한 트랜잭션·검증 구조(헤더 UPDATE + 라인 DELETE/INSERT).
    // ─────────────────────────────────────────────────────────────────────
    public async Task UpdateOrderAsync(
        string orderId,
        UpdateSalesOrderRequest request,
        string tenantId,
        CancellationToken ct = default)
    {
        // 1) 존재 + draft 검증 (tenant 격리). soft delete된 것도 제외.
        const string assertSql = """
                                 SELECT status
                                 FROM sales_orders
                                 WHERE order_id = @OrderId
                                   AND tenant_id = @TenantId
                                   AND is_deleted = 0
                                 """;

        var status = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(assertSql, new { OrderId = orderId, TenantId = tenantId }, cancellationToken: ct));

        if (status is null)
        {
            throw new InvalidOperationException("수주서를 찾을 수 없습니다.");
        }

        // §절대원칙 #6: 확정·전환된 수주는 수정 차단.
        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("임시저장(draft) 상태 수주서만 수정할 수 있습니다.");
        }

        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }

        var supplyAmount = request.Items.Sum(x => x.SupplyAmount);
        var vatAmount = request.Items.Sum(x => x.VatAmount);
        var orderDate = request.OrderDate == default ? DateTime.UtcNow.Date : request.OrderDate.Date;

        const string updateSql = """
                                 UPDATE sales_orders SET
                                     partner_id   = @PartnerId,
                                     order_date   = @OrderDate,
                                     memo         = @Memo,
                                     total_amount = @SupplyAmount,
                                     vat_amount   = @VatAmount,
                                     updated_at   = NOW(6)
                                 WHERE order_id  = @OrderId
                                   AND tenant_id = @TenantId
                                   AND status    = 'draft'
                                 """;

        // 트랜잭션으로 헤더 UPDATE + 품목 DELETE/INSERT를 원자적으로 묶는다
        // (UpdateDeliveryAsync 패턴 동일).
        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(updateSql,
                new
                {
                    OrderId = orderId,
                    TenantId = tenantId,
                    PartnerId = request.PartnerId,
                    OrderDate = orderDate,
                    Memo = request.Memo,
                    SupplyAmount = supplyAmount,
                    VatAmount = vatAmount
                },
                transaction: tx, cancellationToken: ct));

            // 기존 라인 전체 삭제 후 재INSERT (UpdateDeliveryAsync 라인 처리 패턴).
            await _db.ExecuteAsync(new CommandDefinition(
                "DELETE FROM sales_order_items WHERE order_id = @OrderId AND tenant_id = @TenantId",
                new { OrderId = orderId, TenantId = tenantId },
                transaction: tx, cancellationToken: ct));

            foreach (var item in request.Items)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO sales_order_items
                        (order_item_id, order_id, tenant_id, item_id,
                         ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
                    VALUES
                        (@OrderItemId, @OrderId, @TenantId, @ItemId,
                         @OrderedQty, 0, @UnitPrice, @SupplyAmount, @VatAmount, 'pending')
                    """,
                    new
                    {
                        OrderItemId = Guid.NewGuid().ToString(),
                        OrderId = orderId,
                        TenantId = tenantId,
                        ItemId = item.ItemId,
                        item.OrderedQty,
                        item.UnitPrice,
                        item.SupplyAmount,
                        item.VatAmount
                    },
                    transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] order update rollback failed: {rbex.Message}"); }
            throw;
        }

        // 감사로그 — 수주서 수정
        var soAfterJson = $"{{\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("update", "sales_order", orderId, afterJson: soAfterJson, ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 수주서 draft 삭제 — soft delete. 판매전환된 라인 있으면 차단.
    // ─────────────────────────────────────────────────────────────────────
    public async Task DeleteSalesOrderAsync(string orderId, string tenantId, CancellationToken ct = default)
    {
        var row = await _db.QueryFirstOrDefaultAsync<(string Status, byte IsDeleted)?>(new CommandDefinition(
            "SELECT status AS Status, is_deleted AS IsDeleted FROM sales_orders WHERE order_id=@Id AND tenant_id=@Tid",
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("수주서를 찾을 수 없습니다.");

        if (row.IsDeleted == 1)
        {
            throw new InvalidOperationException("이미 삭제된 수주서입니다.");
        }

        // 판매전환된 라인 차단 — 단, 거래명세서가 cancelled 면 무시(사장님 정책: 삭제=취소).
        var activeDelivered = await _db.QueryFirstOrDefaultAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
              FROM sales_delivery_items di
              JOIN sales_deliveries sd ON sd.delivery_id = di.delivery_id AND sd.tenant_id = di.tenant_id
             WHERE di.order_item_id IN (
                     SELECT order_item_id FROM sales_order_items
                      WHERE order_id=@Id AND tenant_id=@Tid
                   )
               AND di.tenant_id = @Tid
               AND sd.status <> 'cancelled'
            """,
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        if (activeDelivered > 0)
        {
            throw new InvalidOperationException("이미 판매전환(출고)된 라인이 있어 삭제할 수 없습니다. 거래명세서를 먼저 취소해주세요.");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE sales_orders SET is_deleted=1, updated_at=NOW(6) WHERE order_id=@Id AND tenant_id=@Tid",
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct));

        await _audit.LogAsync("delete", "sales_order", orderId, ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 자동발주 후보 조회 — 거래명세서 확정 직후 안전재고 위반 품목 추출.
    // 사장님 지시 (2026-04-26): 판매 반영 시 재고가 안전재고 이하/0 이면
    //   "자동발주 하시겠습니까?" 다이얼로그를 띄울 후보를 내려준다.
    // 조건: 라인 품목 중 auto_order_enabled=1 AND
    //       (item_stock 합계 <= items.safety_stock OR <= 0)
    // ─────────────────────────────────────────────────────────────────────
    public async Task<List<AutoOrderCandidateDto>> GetAutoOrderCandidatesAsync(
        string deliveryId, string tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT
                   i.item_id        AS ItemId,
                   IFNULL(i.item_code,'') AS ItemCode,
                   i.item_name      AS ItemName,
                   COALESCE(s.qty, 0) AS CurrentQty,
                   COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyQty,
                   COALESCE(i.auto_order_qty, 0) AS SuggestedOrderQty,
                   i.auto_order_partner_id AS PartnerId,
                   p.partner_name   AS PartnerName,
                   COALESCE(i.purchase_price, i.cost_price, 0) AS UnitPrice,
                   CASE
                     WHEN COALESCE(s.qty, 0) <= 0 THEN 'out_of_stock'
                     ELSE 'below_safety'
                   END AS Reason,
                   -- 신규 (2026-08-25, 20260825작1 W2): 사슬 판정용.
                   --   후보에서 빼지 않는다 — 반제품도 발주는 나가야 한다(외주 매입 경로).
                   --   막는 것은 매입확정뿐이다.
                   COALESCE(i.item_type, 'material') AS ItemType
              FROM sales_delivery_items di
              JOIN items i
                ON i.item_id = di.item_id AND i.tenant_id = di.tenant_id
              LEFT JOIN (
                   SELECT tenant_id, item_id, SUM(current_qty) AS qty
                     FROM item_stock GROUP BY tenant_id, item_id
              ) s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
              LEFT JOIN partners p
                ON p.partner_id = i.auto_order_partner_id AND p.tenant_id = i.tenant_id
             WHERE di.delivery_id = @DeliveryId
               AND di.tenant_id   = @Tid
               AND IFNULL(i.auto_order_enabled, 0) = 1
               AND (
                     COALESCE(s.qty, 0) <= COALESCE(i.safety_stock, i.safe_stock, 0)
                  OR COALESCE(s.qty, 0) <= 0
                   )
               -- 봉합 (2026-08-21, 20260821작1 W4, 사장님 실측 지적): 멱등 필터.
               --   종전엔 실시간 재고만 봐서 "이미 자동발주한 사실"이 조회에 반영되지 않았다.
               --   발주해도 물건이 입고되기 전까지 재고는 그대로이므로, 다음 거래명세서 확정 때
               --   같은 품목이 또 후보로 떠 중복 발주가 났다. is_auto=1 은 쓰기만 하고 읽지 않았다.
               --   BOM 경로(BomService.OrderAlertAsync)는 stock_alerts.status 로 이미 정상 동작 —
               --   판매 경로만 비어 있었다. 두 경로 동작을 일치시킨다.
               --   status enum 실측(§#13): draft/ordered/partial/received/cancelled.
               --   · draft·ordered·partial = 입고 미완 → 재고 미반영 → 중복 위험 구간이므로 제외
               --   · partial(부분입고)은 잔량이 미결이라 반드시 포함. 빠지면 부분입고에서 중복 재발.
               --   · received 는 재고가 올라가 자연 탈락하므로 별도 제외 불필요
               --   · cancelled·is_deleted 는 제외하지 않는다 — 재발주가 가능해야 한다 (§#20)
               AND NOT EXISTS (
                     SELECT 1
                       FROM purchase_order_items poi
                       JOIN purchase_orders po
                         ON po.po_id = poi.po_id AND po.tenant_id = poi.tenant_id
                      WHERE poi.tenant_id = i.tenant_id
                        AND poi.item_id   = i.item_id
                        AND po.is_auto    = 1
                        AND po.is_deleted = 0
                        AND po.status IN ('draft','ordered','partial')
                   )
            """;

        var rows = await _db.QueryAsync<AutoOrderCandidateDto>(new CommandDefinition(
            sql, new { DeliveryId = deliveryId, Tid = tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 자동발주 즉시 생성 — 사장님 지시 (2026-04-26): 다이얼로그 OK 시
    //   "바로 자동발주가 되어야 정상이지". 공급처별로 묶어 발주서(draft) 1건씩 생성.
    // 공급처 미설정 품목은 스킵 + 사유 반환(워크플로우 §20 끊김 금지).
    // ─────────────────────────────────────────────────────────────────────
    public async Task<List<AutoOrderResultDto>> CreateAutoOrdersAsync(
        IReadOnlyList<AutoOrderCandidateDto> candidates, string tenantId, bool autoReceive = false, CancellationToken ct = default)
    {
        var results = new List<AutoOrderResultDto>();
        if (candidates.Count == 0) return results;

        // 공급처별 그룹핑. 미지정 품목은 별도 실패 결과.
        var noPartner = candidates.Where(c => string.IsNullOrWhiteSpace(c.PartnerId)).ToList();
        if (noPartner.Count > 0)
        {
            results.Add(new AutoOrderResultDto
            {
                Success = false,
                Reason = $"{noPartner.Count}개 품목에 자동발주 공급처 미설정 — 상품마스터에서 지정 필요.",
                ItemIds = noPartner.Select(x => x.ItemId).ToList()
            });
        }

        var groups = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.PartnerId))
            .GroupBy(c => c.PartnerId!);

        var today = DateTime.Today;
        // WO-11: 한글 prefix 통일 (자동발주 = 발-)
        var prefix = $"발-{today:yyyyMMdd}-";

        foreach (var grp in groups)
        {
            var partnerId = grp.Key;
            var lines = grp.ToList();
            var supply = lines.Sum(x => Math.Max(x.SuggestedOrderQty, 1m) * x.UnitPrice);
            var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);

            using var tx = _db.BeginTransaction();
            try
            {
                // 봉합 (2026-06-23, 5차 전수조사 SALES-02): COUNT(*)+1 채번은 소프트삭제(is_deleted) 행을
                //   세서 갭 충돌이 나고, 코드 표준 채번 헬퍼(MAX+1)와 이탈해 있었다. DocumentNumberHelper 로
                //   일원화한다(tx 안에서 채번해 직전 그룹 INSERT 가시성 보장). 동시 HTTP 충돌은 UNIQUE 가 차단.
                var poNo = await DocumentNumberHelper.NextNumberAsync(
                    _db, tenantId, "purchase_orders", "po_no", prefix, ct, transaction: tx);
                var poId = Guid.NewGuid().ToString();

                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO purchase_orders
                      (po_id, tenant_id, po_no, partner_id, po_date, status, total_amount, vat_amount, memo, is_auto, created_at, updated_at)
                    VALUES
                      (@PoId, @Tid, @PoNo, @PartnerId, @PoDate, 'draft', @Supply, @Vat, @Memo, 1, NOW(6), NOW(6))
                    """,
                    new
                    {
                        PoId = poId, Tid = tenantId, PoNo = poNo,
                        PartnerId = partnerId, PoDate = today,
                        Supply = supply, Vat = vat,
                        // 변경 (2026-08-25, 20260825작1 W2-0-B, 사장님 결재): 비고 앞머리에 「자동발주서」.
                        //   종전 "안전재고 자동발주 (판매확정 트리거)" — 「트리거」는 개발용어다.
                        //   목록에서 비고는 잘려 보이므로 앞부분이 살아야 한다.
                        Memo = "자동발주서 — 안전재고 미달(판매확정)"
                    }, transaction: tx, cancellationToken: ct));

                foreach (var line in lines)
                {
                    var qty = line.SuggestedOrderQty > 0 ? line.SuggestedOrderQty : Math.Max(line.SafetyQty - line.CurrentQty, 1m);
                    var lineSupply = qty * line.UnitPrice;
                    var lineVat = Math.Round(lineSupply * 0.1m, 0, MidpointRounding.AwayFromZero);

                    await _db.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO purchase_order_items
                          (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, item_status)
                        VALUES
                          (UUID(), @PoId, @Tid, @ItemId, @Qty, 0, @UnitPrice, @Supply, @Vat, 'pending')
                        """,
                        new
                        {
                            PoId = poId, Tid = tenantId, ItemId = line.ItemId,
                            Qty = qty, UnitPrice = line.UnitPrice,
                            Supply = lineSupply, Vat = lineVat
                        }, transaction: tx, cancellationToken: ct));
                }

                tx.Commit();
                await _audit.LogAsync("create", "purchase_order", poId,
                    afterJson: $"{{\"source\":\"auto_order\",\"po_no\":\"{poNo}\",\"item_count\":{lines.Count}}}",
                    ct: ct);

                var resultRow = new AutoOrderResultDto
                {
                    Success = true,
                    PoId = poId,
                    PoNo = poNo,
                    PartnerId = partnerId,
                    PartnerName = lines[0].PartnerName,
                    ItemIds = lines.Select(x => x.ItemId).ToList()
                };

                // 사장님 지시 (2026-04-26): 자동발주 → 매입처리까지 원클릭.
                // autoReceive=true 면 발주 직후 매입전환 + 매입 확정까지 진행 → 자재 재고 즉시 +반영.
                //
                // 봉합 (2026-08-25, 20260825작1 W2, 사장님 결재 "데이터 정합성이 중요하지 막아!!"):
                //   🔴 반제품·완제품은 사슬을 안 태운다. 만들어 채우는 물건이라 매입확정을 태우면
                //      재고뿐 아니라 매입 분개와 외상매입금(partner_balance)까지 잡힌다 —
                //      사지 않은 물건에 갚을 돈이 생긴다.
                //   🔴 BOM 경로와 같은 규칙을 쓴다(AutoChainPolicy 한 곳) — 두 곳이 각자 판정하면
                //      한쪽만 고쳐지는 일이 또 난다. 8/21 이 정확히 그랬다.
                //   ⚠️ 발주서는 그대로 만든다. 막는 것은 매입확정뿐이다(#20 흐름 안 끊김).
                var blockedByType = lines
                    .Where(x => !AutoChainPolicy.CanAutoReceive(x.ItemType))
                    .Select(x => x.ItemName)
                    .ToList();

                if (autoReceive && blockedByType.Count > 0)
                {
                    resultRow.Reason =
                        $"발주서만 만들었습니다 — {string.Join(", ", blockedByType.Take(3))}은(는) "
                      + "만들어서 채우는 품목이라 매입확정까지 자동으로 하지 않습니다.";
                }
                else if (autoReceive)
                {
                    try
                    {
                        var purSvc = _services.GetService<IPurchaseService>()
                            ?? throw new InvalidOperationException("매입 서비스를 찾을 수 없습니다.");
                        var (receiptId, receiptNo) = await purSvc.ConvertOrderToReceiptAsync(poId, tenantId, ct);
                        await purSvc.ConfirmReceiptAsync(receiptId, new ConfirmReceiptRequest(), ct);
                        resultRow.Reason = $"매입 자동확정: {receiptNo}";
                    }
                    catch (Exception ex)
                    {
                        // 작20260428이7 (P0-A): §절대원칙 #15 "빈 catch 금지" + §#20 "워크플로우 끊김 금지".
                        // 자동 사슬에서 매입확정 실패하면 Success=false로 명확히 알리고 로그 남김.
                        // 이전 버그: Success=true 유지 → 사용자는 성공이라 알지만 stock_ledger INSERT 안 됨 → "재고부족 안 사라짐".
                        Console.Error.WriteLine($"[WARN] 자동 사슬 매입확정 실패 — PoNo={poNo} TenantId={tenantId} ex={ex.GetType().Name} msg={ex.Message}");
                        resultRow.Success = false;
                        resultRow.Reason = $"발주 OK / 매입 자동확정 실패: {ex.Message}";
                    }
                }

                results.Add(resultRow);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
                results.Add(new AutoOrderResultDto
                {
                    Success = false,
                    PartnerId = partnerId,
                    PartnerName = lines[0].PartnerName,
                    ItemIds = lines.Select(x => x.ItemId).ToList(),
                    Reason = ex.Message
                });
            }
        }

        return results;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 매출반품 — 13차 후순위 봉합(2026-06-22, A 매입반품 대칭 풀스택).
    // 고객이 판매분을 돌려보냄. 확정 시 재고 IN(증가, 매출 OUT의 역) + 매출 역분개.
    // 매입반품(PurchaseService 5메서드)의 거울: OUT→IN, total_purchase→total_sales,
    // RecordPurchaseReturn→RecordSalesDeliveryCancel, receipt_id→delivery_id.
    // BOM 폭파 역행은 불필요(반품은 완제품 그대로 입고).
    // ═════════════════════════════════════════════════════════════════════


    /// <inheritdoc />
    public async Task<List<string>> GetSalesReturnReasonsAsync(string tenantId, CancellationToken ct = default)
    {
        // 20260825작6 — 사장님 지시: 사유는 콤보가 아니라 자율 입력이고,
        //   한 번 쓴 말이 다음부터 선택지가 된다. 우리가 코드를 미리 정하지 않는다(헌법 #11 과 같은 축).
        //   삭제분(is_deleted=1)은 뺀다 — 지운 문서의 말이 목록에 남으면 안 된다.
        var rows = await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT return_reason
              FROM sales_returns
             WHERE tenant_id = @Tid
               AND is_deleted = 0
               AND return_reason IS NOT NULL
               AND TRIM(return_reason) <> ''
             ORDER BY return_reason
            """,
            new { Tid = tenantId }, cancellationToken: ct));

        return rows.ToList();
    }
    public async Task<List<SalesReturnListDto>> GetSalesReturnsAsync(
        string tenantId, DateTime? from = null, DateTime? to = null, string? status = null, CancellationToken ct = default)
    {
        var sql = """
            SELECT sr.return_id AS ReturnId, sr.return_no AS ReturnNo, sr.return_date AS ReturnDate,
                   sr.partner_id AS PartnerId, COALESCE(p.partner_name,'') AS PartnerName,
                   COALESCE(sr.total_amount,0) AS TotalAmount, COALESCE(sr.vat_amount,0) AS VatAmount,
                   sr.status AS Status, sr.memo AS Memo,
                   ec.emp_name AS CreatedByName
            FROM sales_returns sr
            LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            LEFT JOIN employees ec ON ec.user_id = sr.created_by AND ec.tenant_id = sr.tenant_id
            WHERE sr.tenant_id = @Tid AND sr.is_deleted = 0
              AND (@From IS NULL OR sr.return_date >= @From)
              AND (@To IS NULL OR sr.return_date <= @To)
              AND (@Status IS NULL OR sr.status = @Status)
            ORDER BY sr.return_date DESC, sr.return_no DESC
            """;
        var rows = await _db.QueryAsync<SalesReturnListDto>(new CommandDefinition(
            sql, new { Tid = tenantId, From = from, To = to, Status = status }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<SalesReturnDetailDto?> GetSalesReturnDetailAsync(
        string returnId, string tenantId, CancellationToken ct = default)
    {
        var head = await _db.QueryFirstOrDefaultAsync<SalesReturnDetailDto>(new CommandDefinition(
            """
            SELECT sr.return_id AS ReturnId, sr.return_no AS ReturnNo, sr.return_date AS ReturnDate,
                   sr.delivery_id AS DeliveryId, sr.partner_id AS PartnerId, COALESCE(p.partner_name,'') AS PartnerName,
                   COALESCE(sr.total_amount,0) AS TotalAmount, COALESCE(sr.vat_amount,0) AS VatAmount,
                   sr.status AS Status, sr.memo AS Memo, sr.return_reason AS ReturnReason, sr.return_reason_memo AS ReturnReasonMemo
            FROM sales_returns sr
            LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            WHERE sr.return_id = @Id AND sr.tenant_id = @Tid AND sr.is_deleted = 0
            """,
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct));
        if (head is null) return null;

        // 🔴 20260825작12 — 작10 이 여기를 안 막아서 500 이 계속 났다.
        //   작10 은 "확정 시 500" 을 ConfirmSalesReturnAsync 안으로만 좁혀 읽고
        //   확정·취소의 **읽는 자리 2곳**만 폴백을 걸었다.
        //   그런데 사용자는 확정 버튼을 누르기 **전에** 이 상세조회를 먼저 지나간다
        //   ⇒ 마이그(DB-108) 안 들어간 DB 에서는 문서를 **여는 순간** 1054 로 죽었다.
        //   1054 는 미들웨어에서 FK(1451/1452) 필터도 InvalidOperationException 필터도
        //   못 통과하고 마지막 catch(Exception) 으로 떨어져 **정확히 500** 이 된다.
        var lossSelect = await HasSalesReturnLossColumnAsync(ct).ConfigureAwait(false)
            ? "sri.is_loss"
            : "0";
        var items = await _db.QueryAsync<SalesReturnDetailItemDto>(new CommandDefinition(
            $"""
            SELECT sri.return_item_id AS ReturnItemId, sri.item_id AS ItemId,
                   COALESCE(i.item_name,'') AS ItemName, i.spec AS Spec, i.unit AS Unit,
                   sri.warehouse_id AS WarehouseId, sri.qty AS Qty, sri.unit_price AS UnitPrice,
                   sri.supply_amount AS SupplyAmount, sri.vat_amount AS VatAmount,
                   -- 20260825작7: 원 판매 줄 연결을 함께 돌려준다.
                   --   이게 없으면 저장된 반품확인서를 다시 열어 고치는 순간
                   --   화면이 링크를 모른 채 저장해 줄 단위 연결이 끊긴다.
                   sri.delivery_item_id AS DeliveryItemId,
                   {lossSelect} AS IsLoss
            FROM sales_return_items sri
            LEFT JOIN items i ON i.item_id = sri.item_id
            WHERE sri.return_id = @Id AND sri.tenant_id = @Tid
            ORDER BY sri.return_item_id
            """,
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct));
        head.Items = items.ToList();
        return head;
    }

    public async Task<(string ReturnId, string ReturnNo)> CreateSalesReturnAsync(
        CreateSalesReturnRequest request, string tenantId, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(request.PartnerId)) throw new InvalidOperationException("거래처는 필수입니다.");
        if (request.Items is null || request.Items.Count == 0) throw new InvalidOperationException("반품 품목은 1건 이상이어야 합니다.");

        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }

        // 🔴 20260827작9 W5 — 같은 거래명세서에 살아있는 반품이 이미 있나.
        //   매입(PurchaseService.CreatePurchaseReturnAsync)에는 진작 있던 가드인데 매출만 없었다.
        //   취소분은 다시 만들 수 있어야 하므로 제외한다(매입과 동일 정책).
        //   ⚠️ 철자 주의 — sales_returns 는 'canceled'(l 하나)로 저장한다(:2508).
        //      같은 파일의 sales_deliveries 는 'cancelled'(l 둘)라 옆줄을 보고 복사하면
        //      이 가드가 영원히 안 걸린다. 막는 척하면서 아무것도 안 막는다.
        if (!string.IsNullOrWhiteSpace(request.DeliveryId))
        {
            var dupNo = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                """
                SELECT return_no FROM sales_returns
                 WHERE delivery_id=@Did AND tenant_id=@Tid
                   AND is_deleted=0 AND status <> 'canceled'
                 LIMIT 1
                """,
                new { Did = request.DeliveryId, Tid = tenantId }, cancellationToken: ct));

            if (dupNo is not null)
            {
                // 🔴 작8 교훈: 막는 것 ≠ 알려주는 것. 기존 반품번호를 반드시 담는다.
                throw new InvalidOperationException(
                    $"이미 반품전표({dupNo})가 발행된 거래명세서입니다. 기존 반품전표를 수정하세요.");
            }
        }

        // 20260827작9 W2-b — 채번 일자는 업무일(KST). 종전 DateTime.UtcNow 는 KST 09시 이전에
        //   전날 날짜로 채번해 전표 일자와 번호의 날짜가 하루 어긋났다. 매입은 이미 BusinessDate(작18 W4).
        var returnDate = request.ReturnDate == default ? BusinessDate.Today : request.ReturnDate.Date;

        // 🔴 20260827작9 W1 — prefix 를 '반-' 으로 바꾼다(사장님 지시: "반품전표 : 반-(전표번호)").
        //   종전 '매출반품-20260827-001' 은 25자인데 sales_returns.return_no 는 varchar(20) 이고
        //   이 배포는 STRICT_TRANS_TABLES 라(ApprovalService.cs:118) ERROR 1406 으로 저장이 터졌다.
        //   '반-20260827-001' = 18자. 매입 '매반-'(19자)과 나란하다.
        // 🔴 W2 — COUNT+1 → MAX+1. DocumentNumberHelper 주석이 COUNT+1 을 "진범"으로 지목했다
        //   (4/28 자동사슬 174건 중 71건 원장누락). 소프트삭제 시 COUNT 가 줄어 이미 쓴 번호를
        //   재발급하는 것이 더 큰 위험이다 — 사장님: "사슬동작중 중복생성 절대금지".
        var prefix = $"반-{returnDate:yyyyMMdd}-";
        var returnNo = await DocumentNumberHelper.NextNumberAsync(
            _db, tenantId, "sales_returns", "return_no", prefix, ct);
        var returnId = Guid.NewGuid().ToString();

        decimal totalAmount = 0, totalVat = 0;
        foreach (var it in request.Items) { totalAmount += it.SupplyAmount; totalVat += it.VatAmount; }

        await _db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO sales_returns (return_id, tenant_id, return_no, delivery_id, partner_id,
                return_date, status, total_amount, vat_amount, memo,
                return_reason, return_reason_memo, created_at, created_by, updated_at, is_deleted)
              VALUES (@ReturnId, @Tid, @ReturnNo, @DeliveryId, @PartnerId,
                @ReturnDate, 'draft', @Total, @Vat, @Memo,
                @ReturnReason, @ReturnReasonMemo, NOW(6), @CreatedBy, NOW(6), 0)",
            new
            {
                ReturnId = returnId, Tid = tenantId, ReturnNo = returnNo,
                DeliveryId = request.DeliveryId, PartnerId = request.PartnerId,
                ReturnDate = returnDate, Total = totalAmount, Vat = totalVat, Memo = request.Memo,
                // sales_returns.return_reason 는 NOT NULL DEFAULT 'customer_return'(매입반품과 달리 NOT NULL).
                // 화면이 사유를 안 보내면 NULL→1048(500)이 나므로 DDL DEFAULT 와 동일 값으로 폴백(14차 P1 봉합).
                ReturnReason = request.ReturnReason ?? "customer_return", ReturnReasonMemo = request.ReturnReasonMemo
                ,
                // 20260825작5: 전표 작성자 기록 (created_by = user_id 체계, 사장님 결재).
                CreatedBy = _currentTenant.UserId
            }, cancellationToken: ct));

        // 🔴 20260825작12 — 쓰는 자리도 막는다. 작10 은 읽는 자리만 막았다.
        //   컬럼이 없는 DB 에서는 is_loss 를 빼고 넣는다(기본 0 = 정상품과 같다).
        //   로스 기능을 끄는 게 아니다 — 컬럼이 생기면 그대로 저장된다(헌법 #20).
        var hasLoss = await HasSalesReturnLossColumnAsync(ct).ConfigureAwait(false);
        var lossCol = hasLoss ? ", is_loss" : "";
        var lossVal = hasLoss ? ", @IsLoss" : "";
        foreach (var it in request.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                $@"INSERT INTO sales_return_items (return_item_id, return_id, tenant_id, delivery_item_id,
                    item_id, qty, unit_price, original_unit_price, supply_amount, vat_amount, warehouse_id{lossCol})
                  VALUES (UUID(), @ReturnId, @Tid, @DeliveryItemId, @ItemId, @Qty, @Price, @OrigPrice, @Supply, @Vat, @Wh{lossVal})",
                new
                {
                    ReturnId = returnId, Tid = tenantId, DeliveryItemId = it.DeliveryItemId,
                    ItemId = it.ItemId, Qty = it.Qty, Price = it.UnitPrice,
                    OrigPrice = it.OriginalUnitPrice ?? it.UnitPrice,
                    Supply = it.SupplyAmount, Vat = it.VatAmount, Wh = it.WarehouseId,
                    // 20260825작6: 파손 로스 여부 — 확정 시 재고 반영을 가른다.
                    IsLoss = it.IsLoss
                }, cancellationToken: ct));
        }

        return (returnId, returnNo);
    }

    public async Task UpdateSalesReturnAsync(
        string returnId, UpdateSalesReturnRequest request, string tenantId, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(request.PartnerId)) throw new InvalidOperationException("거래처는 필수입니다.");
        if (request.Items is null || request.Items.Count == 0) throw new InvalidOperationException("반품 품목은 1건 이상이어야 합니다.");

        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }

        var current = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT return_id, status FROM sales_returns WHERE return_id=@Id AND tenant_id=@Tid AND is_deleted=0",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        var status = (string)current.status;
        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"draft 상태만 수정 가능합니다. (현재: {status})");

        var returnDate = request.ReturnDate == default ? DateTime.UtcNow.Date : request.ReturnDate.Date;
        decimal totalAmount = 0, totalVat = 0;
        foreach (var it in request.Items) { totalAmount += it.SupplyAmount; totalVat += it.VatAmount; }

        await _db.ExecuteAsync(new CommandDefinition(
            // 20260825작7: delivery_id 를 함께 갱신한다.
            //   종전엔 이 SET 에 delivery_id 가 없어서, 불러온 반품확인서를 한 번만 더 고쳐 저장하면
            //   원 거래명세서 연결이 조용히 끊겼다(생성은 넣는데 수정이 안 넣던 비대칭).
            //   COALESCE 로 감싼 이유 — 화면이 값을 안 보내는 옛 경로에서 기존 링크를 지워버리면 안 된다.
            //   화면이 링크를 지우려면 빈 문자열이 아니라 별도 해제 동작을 두는 게 맞다(헌법 #1).
            @"UPDATE sales_returns
              SET partner_id=@PartnerId, return_date=@ReturnDate,
                  delivery_id=COALESCE(@DeliveryId, delivery_id),
                  total_amount=@Total, vat_amount=@Vat, memo=@Memo,
                  return_reason=@ReturnReason, return_reason_memo=@ReturnReasonMemo, updated_at=NOW(6)
              WHERE return_id=@Id AND tenant_id=@Tid AND status='draft'",
            new
            {
                Id = returnId, Tid = tenantId, PartnerId = request.PartnerId, ReturnDate = returnDate,
                DeliveryId = string.IsNullOrWhiteSpace(request.DeliveryId) ? null : request.DeliveryId,
                Total = totalAmount, Vat = totalVat, Memo = request.Memo,
                // sales_returns.return_reason 는 NOT NULL DEFAULT 'customer_return'(매입반품과 달리 NOT NULL).
                // 화면이 사유를 안 보내면 NULL→1048(500)이 나므로 DDL DEFAULT 와 동일 값으로 폴백(14차 P1 봉합).
                ReturnReason = request.ReturnReason ?? "customer_return", ReturnReasonMemo = request.ReturnReasonMemo
            }, cancellationToken: ct));

        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sales_return_items WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct));

        // 🔴 20260825작12 — 생성과 **대칭**으로 막는다.
        //   작7 의 교훈: 생성은 넣고 수정은 안 넣으면 두 번째 저장에서 조용히 어긋난다.
        //   여기서도 한쪽만 막으면 "새로 만들면 되는데 고치면 500" 이 된다.
        var hasLossU = await HasSalesReturnLossColumnAsync(ct).ConfigureAwait(false);
        var lossColU = hasLossU ? ", is_loss" : "";
        var lossValU = hasLossU ? ", @IsLoss" : "";
        foreach (var it in request.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                $@"INSERT INTO sales_return_items (return_item_id, return_id, tenant_id, delivery_item_id,
                    item_id, qty, unit_price, original_unit_price, supply_amount, vat_amount, warehouse_id{lossColU})
                  VALUES (UUID(), @ReturnId, @Tid, @DeliveryItemId, @ItemId, @Qty, @Price, @OrigPrice, @Supply, @Vat, @Wh{lossValU})",
                new
                {
                    ReturnId = returnId, Tid = tenantId, DeliveryItemId = it.DeliveryItemId,
                    ItemId = it.ItemId, Qty = it.Qty, Price = it.UnitPrice,
                    OrigPrice = it.OriginalUnitPrice ?? it.UnitPrice,
                    Supply = it.SupplyAmount, Vat = it.VatAmount, Wh = it.WarehouseId,
                    // 20260825작6: 파손 로스 여부 — 확정 시 재고 반영을 가른다.
                    IsLoss = it.IsLoss
                }, cancellationToken: ct));
        }
    }

    /// <summary>
    /// <c>sales_return_items.is_loss</c> 가 이 DB 에 있는지 본다 (20260825작10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 1.3.13 실측에서 반품확정이 <b>500</b> 으로 죽었다. 재현해 보니 원인은
    /// <c>Unknown column 'is_loss' in 'SELECT'</c> — DB-108(작6)이 아직 안 들어간 DB 였다.
    /// </para>
    /// <para>
    /// 🔴 <b>헌법 #13</b> — 새 SQL 을 던지기 전에 실제 스키마를 확인한다.
    /// 컬럼이 있을 거라 믿고 쓰면, 마이그가 하루라도 늦게 도착한 고객은 그날 업무를 못 한다.
    /// </para>
    /// <para>
    /// ⚠️ 이 검사는 <b>기능을 끄지 않는다.</b> 컬럼이 생기는 순간 로스 판정은 그대로 살아난다.
    /// </para>
    /// </remarks>
    /// <summary>
    /// 🔴 <b>20260825작15 — 반품확정 500 의 진짜 원인.</b>
    /// <c>is_loss</c> 값을 <b>어떤 CLR 타입으로 와도</b> 안전하게 판정한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[무엇이 문제였나]</b> 종전 코드는 <c>(int)(it.is_loss ?? 0) == 0</c> 이었다.
    /// 그런데 MySqlConnector 는 <c>TINYINT(1)</c> 을 <b><c>Boolean</c></b> 으로 돌려준다
    /// (연결문자열에 <c>TreatTinyAsBoolean=false</c> 가 없어 기본값 <c>true</c> 적용).
    /// <c>dynamic</c> 캐스팅은 런타임 실제 타입이 정확히 맞아야 하므로
    /// <c>bool → int</c> 는 <c>RuntimeBinderException</c> 이다.
    /// </para>
    /// <para>
    /// 🔴 <b>세 번의 봉합이 전부 거꾸로였다.</b> 작10·작12 는 *"마이그(DB-108)가 안 들어간 DB"* 를 고쳤는데,
    /// 폴백(<c>0 AS is_loss</c>)은 <c>Int32</c> 라 <b>정상 동작</b>했고
    /// 정작 죽는 것은 <b>마이그가 들어간 DB</b>(실컬럼 → <c>Boolean</c>)였다.
    /// 사장님 PC 는 DB-108 이 적용돼 있어 계속 500 이 났다.
    /// </para>
    /// <para>
    /// <b>[증상 모양 대조]</b> <c>RuntimeBinderException</c> 은
    /// <c>InvalidOperationException</c>(→400)도 <c>MySqlException</c>(1054/1146/1062→400, 1451/1452→409)도 아니라
    /// 미들웨어 마지막 <c>catch(Exception)</c> 으로 떨어져 <b>정확히 500</b> 이다.
    /// 실측으로 재현했다 — <c>Cannot convert type 'bool' to 'int'</c>.
    /// </para>
    /// <para>
    /// ⚠️ 이 예외는 트랜잭션 <b>안</b>에서 터져 롤백된다 ⇒ <b>원장이 안 남는다</b> ⇒
    /// 작13 이 넣은 "이미 재고에 반영됨" 진입 가드에도 안 걸린다.
    /// 그래서 <b>몇 번을 눌러도 똑같이 500</b> 이었다.
    /// </para>
    /// <para>
    /// 🔴 <b>타입을 하나로 못박지 않는다.</b> 연결문자열 설정·DB 버전·컬럼 정의에 따라
    /// <c>bool</c>·<c>sbyte</c>·<c>int</c>·<c>long</c> 중 무엇이든 올 수 있다.
    /// <c>Convert.ToInt32</c> 는 이 전부를 받는다 — 한 타입만 가정하면 같은 사고가 또 난다.
    /// </para>
    /// </summary>
    /// <returns>파손 로스면 <c>true</c>. 값이 없으면 <c>false</c>(정상품 — 안전측).</returns>
    private static bool IsLossValue(object? raw)
    {
        if (raw is null || raw is DBNull) return false;
        try
        {
            return Convert.ToInt32(raw) != 0;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            // 헌법 #15 — 침묵하지 않는다. 못 읽으면 정상품(재고 반영)으로 본다.
            //   로스로 오판하면 재고가 안 늘어 현장 숫자와 어긋난다. 안전측은 false 다.
            System.Diagnostics.Trace.TraceWarning(
                $"[SalesService] is_loss 값을 읽지 못했다 — 정상품으로 본다. 실제타입={raw.GetType().Name} 값={raw}");
            return false;
        }
    }

    private async Task<bool> HasSalesReturnLossColumnAsync(CancellationToken ct)
    {
        try
        {
            var n = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE()
                   AND TABLE_NAME   = 'sales_return_items'
                   AND COLUMN_NAME  = 'is_loss'
                """,
                cancellationToken: ct)).ConfigureAwait(false);
            return n > 0;
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 침묵하지 않는다. 못 읽으면 없는 쪽(안전측)으로 본다.
            System.Diagnostics.Trace.TraceWarning($"[SalesService] is_loss 컬럼 확인 실패: {ex.Message}");
            return false;
        }
    }

    // 매출반품 확정 — draft → confirmed + 재고 IN + 매출 역분개 (단일 트랜잭션).
    // 매입반품 ConfirmPurchaseReturnAsync의 거울 — move_type out→in, RecordPurchaseReturn→RecordSalesDeliveryCancel.
    public async Task ConfirmSalesReturnAsync(string returnId, string tenantId, string? employeeId, CancellationToken ct = default)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT return_id, partner_id, return_date, status, return_no, total_amount, vat_amount FROM sales_returns WHERE return_id=@Id AND tenant_id=@Tid AND is_deleted=0",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        if ((string)header.status != "draft")
            throw new InvalidOperationException("draft 상태만 확정할 수 있습니다.");

        // 🔴 20260825작13 — 사장님 실측 반려(1.3.15): 반품확정이 여전히 500.
        //   [무엇이었나] `stock_ledger` 에 UNIQUE(uq_stock_ledger_source: source_type·source_id·item_id·move_type),
        //     `journal_entries` 에 UNIQUE(uq_je_source: tenant_id·source_type·source_id) 가 걸려 있다.
        //     원장이 이미 남아 있는 반품을 다시 확정하면 INSERT 가 **MySQL 1062(Duplicate entry)** 로 죽는다.
        //   🔴 [왜 500 이었나] 1062 는 미들웨어의 어느 필터에도 안 걸린다 —
        //     1054/1146(스키마) 도 아니고 1451/1452(FK→409) 도 아니라
        //     마지막 catch(Exception) 으로 떨어져 **{"error":"서버 오류가 발생했습니다"} 500**.
        //     레포 전체에서 1062 를 잡는 곳이 **한 군데도 없었다**(grep 0건).
        //   [어떻게 생기나] 상태 전환(6단계)은 트랜잭션 마지막이다. 그 앞 단계가 커밋된 뒤
        //     뒤에서 실패하면 상태는 draft 로 남고 원장만 남는 창이 생긴다.
        //     그 뒤로는 몇 번을 눌러도 1062 → 500 이 반복된다. **누르는 사람은 이유를 알 수 없다.**
        //   [고침] 원장이 이미 있으면 **중복 기록 대신 사람 말로 안내**한다(InvalidOperationException → 400).
        //     헌법 #3(원장 INSERT ONLY)이라 지우지 않는다 — 지우는 게 아니라 **막는다.**
        var ledgerExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM stock_ledger
             WHERE tenant_id = @Tid AND source_type = 'sales_return' AND source_id = @Id
            """,
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (ledgerExists > 0)
        {
            throw new InvalidOperationException(
                "이 반품은 이미 재고에 반영되어 있습니다. 목록을 새로고침해 상태를 확인해주세요. "
                + "계속 같은 문제가 보이면 관리자에게 알려주세요.");
        }

        DateTime rd = (DateTime)header.return_date;
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, rd, ct);

        // 20260825작10: is_loss 컬럼이 아직 없는 DB 에서도 확정은 되어야 한다.
        //   사장님 1.3.13 실측 500 의 실제 원인이 여기였다 — "Unknown column 'is_loss' in 'SELECT'".
        //   DB-108(작6) 이 아직 안 들어간 고객 DB 에서 이 SELECT 가 통째로 죽었다.
        //   🔴 로스 기능을 끄는 게 아니다. 컬럼이 생기면 그대로 동작한다.
        //      마이그가 늦게 도착한 DB 에서도 반품확정 자체는 되게 한다(헌법 #20 — 흐름은 안 끊는다).
        var hasLossColumn = await HasSalesReturnLossColumnAsync(ct).ConfigureAwait(false);
        var lossSelect = hasLossColumn ? "is_loss" : "0 AS is_loss";
        var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
            // 20260825작6: is_loss 를 함께 읽는다 — 파손품은 재고에 넣지 않는다.
            $"SELECT item_id, qty, unit_price, supply_amount, warehouse_id, {lossSelect} FROM sales_return_items WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))).ToList();

        var returnNo = (string)header.return_no;
        var partnerId = (string)header.partner_id;
        var totalAmount = (decimal)header.total_amount;
        var vatAmount = (decimal)header.vat_amount;

        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 기본창고 폴백 — 매입반품·판매·BOM과 동일(wh_code MAIN 우선, 헌법 #12 대칭).
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

            // stock_ledger UNIQUE 키(tenant, source_type=sales_return, source_id=returnId, item_id, move_type=in)
            // 단위 유일 — 같은 품목 2라인이면 item_id 합산해 키당 1행만 기록(7차 B-1 대칭).
            // 🔴 20260825작6 — 파손 로스는 재고에 넣지 않는다.
            //   사장님 정의: "파손이면 로스로 정의, 파손이 아니면 재입고(재고반영)".
            //   팔 수 없는 물건을 재고로 잡으면 현장에서 세는 숫자와 어긋난다.
            //   ⚠️ 걸러내는 것은 재고뿐이다 — 매출·미수 차감(③④)은 로스도 그대로 간다.
            //      물건은 못 쓰지만 고객에게 돈은 돌려주기 때문이다.
            var returnGroups = items
                .Where(it => !IsLossValue((object?)it.is_loss))
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

            // 1) 재고원장 Reverse IN INSERT (매출 OUT의 역행 — 반품 입고로 재고 증가)
            foreach (var g in returnGroups)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger
                      (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
                       move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES
                      (@Tid, @ItemId, @Wh, @PartnerId, @EmpId, @Date, @Ym,
                       'in', 'sales_return', @Rid, @DocNo, @Qty, 0, @UnitPrice, @Supply, '매출반품 (Reverse IN)')
                    """,
                    new
                    {
                        Tid = tenantId, ItemId = g.ItemId, Wh = g.Wh, PartnerId = partnerId, EmpId = employeeId,
                        Date = rd, Ym = rd.ToString("yyyy-MM"), Rid = returnId, DocNo = returnNo,
                        Qty = g.Qty, UnitPrice = g.UnitPrice, Supply = g.Supply
                    },
                    transaction: dbTx, cancellationToken: ct));

                // 2) item_stock 증가 — 없는 레코드도 방어적 UPSERT
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty + @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new { TenantId = tenantId, ItemId = g.ItemId, WarehouseId = g.Wh, Qty = g.Qty, UnitCost = g.UnitPrice },
                    transaction: dbTx, cancellationToken: ct));
            }

            // 3) monthly_summary 매출 역산 — MonthlySummaryGuard 멱등 가드 (ConfirmDelivery 대칭)
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx, tenantId: tenantId, date: rd,
                sourceType: "sales_return_confirmed", sourceId: returnId,
                field: MonthlySummaryGuard.SummaryField.TotalSales, amount: -totalAmount, ct: ct);

            // 4) partner_balance 매출 역산 (반품 확정 시 total_sales 차감)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId, -@Amount, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales     = total_sales - @Amount,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = tenantId, PartnerId = partnerId, Amount = totalAmount },
                transaction: dbTx, cancellationToken: ct));

            // 5) 회계 역분개 — 매출취소 역분개 재사용 (차변 매출+부가세예수금 / 대변 외상매출금)
            if (totalAmount != 0m || vatAmount != 0m)
            {
                await AutoJournalHelper.RecordSalesDeliveryCancelAsync(
                    conn, dbTx!, tenantId, returnId, returnNo, rd, partnerId, totalAmount, vatAmount, employeeId, ct);
            }

            // 6) 상태 전환
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE sales_returns SET status='confirmed', updated_at=NOW(6) WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            await _audit.LogAsync("confirm", "sales_return", returnId, ct: ct);
        }
        catch (Exception ex)
        {
            // 🔴 20260825작14 — 무엇이 터졌는지 남긴다.
            //   종전엔 조용히 롤백하고 다시 던지기만 해서, 로그엔 미들웨어의 마지막 줄만 남았다.
            //   반품확정 500 을 세 차례(작10·작12·작13) 쫓는 동안 **어느 단계에서 죽었는지**
            //   알 수 없어 매번 추측으로 다음 후보를 골랐다. 예외 종류와 MySQL 번호만 있어도
            //   다음 사람은 첫 줄에서 시작할 수 있다(헌법 #15 — 침묵하지 않는다).
            var mysqlNo = (ex as MySqlConnector.MySqlException)?.Number;
            System.Diagnostics.Trace.TraceError(
                $"[SalesService] 매출반품 확정 실패 return_id={returnId} "
                + $"예외={ex.GetType().Name} MySQL번호={(mysqlNo?.ToString() ?? "없음")} 메시지={ex.Message}");
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }

        // 결재 트리거 (커밋 이후) — 실패해도 반품 확정 원장은 유효
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "sales_return", returnId, returnNo,
                $"매출반품 확정: {returnNo}", totalAmount + vatAmount,
                tenantId, "system", "확정자", ct, _notifier);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 매출반품 {returnNo} 결재 트리거 실패: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매출반품 취소 — confirmed → canceled. 확정(ConfirmSalesReturnAsync)의 정확한 역행.
    //   봉합 (2026-06-23, 15차 적대검증 15-P1): 종전엔 확정 반품을 되돌릴 경로가 없어, 잘못 확정 시
    //   운영자가 원장을 직접 손대야 했다(헌법 #3 INSERT ONLY 위반 유발). 확정 6단계를 단일 트랜잭션으로 역행:
    //   ① stock_ledger Reverse OUT(확정 IN 되돌림) ② item_stock 차감 ③ monthly_summary +복원
    //   ④ partner_balance total_sales +복원 ⑤ 회계 매출복원 기표 ⑥ status=canceled.
    //   멱등: confirmed 상태만 취소 가능 → 취소 후 canceled 라 두 번 눌러도 차단(stock_ledger/journal UNIQUE 보호).
    // ─────────────────────────────────────────────────────────────────────
    public async Task CancelSalesReturnAsync(string returnId, string tenantId, string? employeeId, CancellationToken ct = default)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT return_no, partner_id, return_date, status, total_amount, vat_amount FROM sales_returns WHERE return_id=@Id AND tenant_id=@Tid AND is_deleted=0",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        if ((string)header.status != "confirmed")
            throw new InvalidOperationException("확정된(confirmed) 반품만 취소할 수 있습니다.");

        DateTime rd = (DateTime)header.return_date;
        // 마감월 보호 — 확정과 동일(닫힌 월의 원장은 건드리지 않는다).
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, rd, ct);

        // 20260825작10: 확정과 대칭 — 여기도 컬럼 없는 DB 를 견딘다.
        //   확정만 고치고 취소를 두면 "확정은 되는데 되돌리진 못하는" 반쪽이 된다(작9 교훈).
        var hasLossColumnForCancel = await HasSalesReturnLossColumnAsync(ct).ConfigureAwait(false);
        var lossSelectForCancel = hasLossColumnForCancel ? "is_loss" : "0 AS is_loss";
        var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
            // 20260825작6: 확정과 대칭 — is_loss 를 함께 읽어 로스는 역행에서도 뺀다.
            $"SELECT item_id, qty, unit_price, supply_amount, warehouse_id, {lossSelectForCancel} FROM sales_return_items WHERE return_id=@Id AND tenant_id=@Tid",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))).ToList();

        var returnNo = (string)header.return_no;
        var partnerId = (string)header.partner_id;
        var totalAmount = (decimal)header.total_amount;
        var vatAmount = (decimal)header.vat_amount;

        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 기본창고 폴백 — 확정과 동일(wh_code MAIN 우선, 헌법 #12 대칭).
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

            // 확정 시 item_id 합산 1행 기록과 대칭으로, 취소도 item_id 합산해 키당 1행만 역행 기록.
            //   stock_ledger UNIQUE 키(tenant, source_type=sales_return_cancel, source_id=returnId, item_id, move_type=out).
            // 🔴 20260825작6 — 확정에서 재고에 안 넣은 로스는 취소에서도 빼지 않는다.
            //   안 넣은 것을 빼면 재고가 그만큼 마이너스로 어긋난다. 확정과 반드시 같은 잣대여야 한다.
            var returnGroups = items
                .Where(it => !IsLossValue((object?)it.is_loss))
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

            // 1) 재고원장 Reverse OUT INSERT (확정 IN 되돌림 — 반품 취소로 재고 다시 감소)
            foreach (var g in returnGroups)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger
                      (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
                       move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES
                      (@Tid, @ItemId, @Wh, @PartnerId, @EmpId, @Date, @Ym,
                       'out', 'sales_return_cancel', @Rid, @DocNo, 0, @Qty, @UnitPrice, @Supply, '매출반품 취소 (Reverse OUT)')
                    """,
                    new
                    {
                        Tid = tenantId, ItemId = g.ItemId, Wh = g.Wh, PartnerId = partnerId, EmpId = employeeId,
                        Date = rd, Ym = rd.ToString("yyyy-MM"), Rid = returnId, DocNo = returnNo,
                        Qty = g.Qty, UnitPrice = g.UnitPrice, Supply = g.Supply
                    },
                    transaction: dbTx, cancellationToken: ct));

                // 2) item_stock 차감 — 확정 시 +Qty 의 역
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty - @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new { TenantId = tenantId, ItemId = g.ItemId, WarehouseId = g.Wh, Qty = g.Qty, UnitCost = g.UnitPrice },
                    transaction: dbTx, cancellationToken: ct));
            }

            // 3) monthly_summary 매출 복원 — 확정 시 -totalAmount 의 역(+totalAmount). 전용 source_type 으로 멱등.
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx, tenantId: tenantId, date: rd,
                sourceType: "sales_return_cancel", sourceId: returnId,
                field: MonthlySummaryGuard.SummaryField.TotalSales, amount: totalAmount, ct: ct);

            // 4) partner_balance 매출 복원 (확정 시 차감한 total_sales 를 다시 가산)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId, @Amount, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales     = total_sales + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = tenantId, PartnerId = partnerId, Amount = totalAmount },
                transaction: dbTx, cancellationToken: ct));

            // 5) 회계 매출복원 기표 — 확정 역분개의 역(정상 매출분개 방향), 전용 source_type
            if (totalAmount != 0m || vatAmount != 0m)
            {
                await AutoJournalHelper.RecordSalesReturnCancelAsync(
                    conn, dbTx!, tenantId, returnId, returnNo, rd, partnerId, totalAmount, vatAmount, employeeId, ct);
            }

            // 6) 상태 전환
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE sales_returns SET status='canceled', updated_at=NOW(6) WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            await _audit.LogAsync("cancel", "sales_return", returnId, ct: ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    // 매출반품 draft 삭제 — confirmed 상태는 별도 취소 경로 필요(매입반품 대칭).
    public async Task DeleteSalesReturnAsync(string returnId, string tenantId, CancellationToken ct = default)
    {
        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM sales_returns WHERE return_id=@Id AND tenant_id=@Tid AND is_deleted=0",
            new { Id = returnId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("반품 문서를 찾을 수 없습니다.");

        if (status != "draft")
            throw new InvalidOperationException("draft 상태만 삭제할 수 있습니다. 확정된 반품은 취소 처리가 필요합니다.");

        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM sales_return_items WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM sales_returns WHERE return_id=@Id AND tenant_id=@Tid",
                new { Id = returnId, Tid = tenantId }, transaction: dbTx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            await _audit.LogAsync("delete", "sales_return", returnId, ct: ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[SalesService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }
}
