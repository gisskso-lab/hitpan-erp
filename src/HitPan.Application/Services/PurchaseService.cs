using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public PurchaseService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
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
}
