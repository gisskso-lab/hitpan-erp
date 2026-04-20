using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface ISalesService
{
    Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    Task<(string Id, string DocumentNumber)> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default);
    Task ConfirmDeliveryAsync(string deliveryId, ConfirmDeliveryRequest request, CancellationToken ct = default);

    Task<DeliveryDetailDto?> GetDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default);

    Task<List<DeliveryListDto>> GetDeliveriesAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default);

    Task UpdateDeliveryAsync(
        string deliveryId,
        UpdateDeliveryDto dto,
        string tenantId,
        string userId,
        CancellationToken ct = default);

    Task DeleteDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default);

    Task<List<SalesOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default);

    Task<(string DeliveryId, string DocumentNumber)> ConvertOrderToDeliveryAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default);

    Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default);
}
