using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface ISalesService
{
    Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    Task<string> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default);
    Task ConfirmDeliveryAsync(string deliveryId, ConfirmDeliveryRequest request, CancellationToken ct = default);
}
