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
}
