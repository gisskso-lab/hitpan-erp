using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public SalesService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
    }

    public async Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        var orderRepo = _unitOfWork.Repository<SalesOrder>();
        var itemRepo = _unitOfWork.Repository<SalesOrderItem>();

        var date = request.OrderDate == default ? DateTime.UtcNow.Date : request.OrderDate.Date;
        var prefix = $"SO-{date:yyyyMMdd}-";
        var today = await orderRepo.FindAsync(x => x.OrderNo.StartsWith(prefix));
        var orderNo = $"{prefix}{today.Count + 1:000}";

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
            Memo = request.Memo
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
        return orderId;
    }

    public async Task<string> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default)
    {
        var deliveryRepo = _unitOfWork.Repository<SalesDelivery>();
        var itemRepo = _unitOfWork.Repository<SalesDeliveryItem>();

        var date = request.DeliveryDate == default ? DateTime.UtcNow.Date : request.DeliveryDate.Date;
        var prefix = $"SD-{date:yyyyMMdd}-";
        var today = await deliveryRepo.FindAsync(x => x.DeliveryNo.StartsWith(prefix));
        var deliveryNo = $"{prefix}{today.Count + 1:000}";

        var deliveryId = Guid.NewGuid().ToString();
        var delivery = new SalesDelivery
        {
            Id = deliveryId,
            DeliveryId = deliveryId,
            TenantId = _currentTenant.TenantId,
            DeliveryNo = deliveryNo,
            OrderId = request.OrderId,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            DeliveryDate = date,
            SourceType = string.IsNullOrWhiteSpace(request.OrderId) ? "direct" : "from_order",
            Status = SalesDeliveryStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo
        };
        await deliveryRepo.AddAsync(delivery);

        foreach (var line in request.Items)
        {
            await itemRepo.AddAsync(new SalesDeliveryItem
            {
                Id = Guid.NewGuid().ToString(),
                DeliveryItemId = Guid.NewGuid().ToString(),
                DeliveryId = deliveryId,
                TenantId = _currentTenant.TenantId,
                OrderItemId = line.OrderItemId,
                ItemId = line.ItemId,
                WarehouseId = line.WarehouseId,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                SupplyAmount = line.SupplyAmount,
                VatAmount = line.VatAmount
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return deliveryId;
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
                var balances = await ledgerRepo.FindAsync(x => x.ItemId == line.ItemId && x.WarehouseId == line.WarehouseId);
                var currentBalance = balances.Sum(x => x.QtyIn - x.QtyOut);
                if (currentBalance - line.Qty < 0m)
                {
                    throw new InvalidOperationException("재고가 부족합니다.");
                }
            }
        }

        foreach (var line in lines)
        {
            await ledgerRepo.AddAsync(new StockLedger
            {
                TenantId = delivery.TenantId,
                ItemId = line.ItemId,
                WarehouseId = line.WarehouseId,
                PartnerId = delivery.PartnerId,
                EmployeeId = delivery.EmployeeId,
                LedgerDate = delivery.DeliveryDate,
                Ym = delivery.DeliveryDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.Out,
                SourceType = "sales_delivery",
                SourceId = delivery.DeliveryId,
                DocNo = delivery.DeliveryNo,
                QtyIn = 0m,
                QtyOut = line.Qty,
                UnitCost = line.UnitPrice,
                SupplyAmount = line.SupplyAmount
            });
        }

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
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
