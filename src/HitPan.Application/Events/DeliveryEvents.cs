namespace HitPan.Application.Events;

public record DeliveryConfirmedEvent(
    string TenantId,
    string DeliveryId,
    string PartnerId,
    decimal SupplyAmount,
    decimal VatAmount,
    decimal TotalAmount,
    List<DeliveryItemEvent> Items);

public record DeliveryItemEvent(
    string ItemId,
    decimal Qty,
    decimal UnitPrice,
    decimal Amount);

public record DeliveryCancelledEvent(
    string TenantId,
    string DeliveryId,
    string PartnerId,
    decimal TotalAmount,
    List<DeliveryItemEvent> Items);

public record PurchaseConfirmedEvent(
    string TenantId,
    string PoId,
    string PartnerId,
    decimal TotalAmount,
    List<DeliveryItemEvent> Items);
