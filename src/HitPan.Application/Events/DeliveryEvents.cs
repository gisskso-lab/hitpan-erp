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

public record BomAssembledEvent(
    string TenantId,
    string BomId,
    string ProductItemId,
    decimal ProducedQty,
    decimal ProductionCost,
    List<BomMaterialUsedEvent> Materials);

public record BomMaterialUsedEvent(
    string ItemId,
    decimal UsedQty,
    decimal UnitCost);

public record StockAlertEvent(
    string TenantId,
    string ItemId,
    string AlertType,
    decimal CurrentQty,
    decimal SafetyQty,
    decimal ShortageQty,
    string? PartnerId,
    decimal OrderQty,
    string? BomId);
