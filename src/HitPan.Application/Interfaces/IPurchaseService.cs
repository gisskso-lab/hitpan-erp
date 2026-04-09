using HitPan.Application.DTOs.Purchase;

namespace HitPan.Application.Interfaces;

public interface IPurchaseService
{
    Task<string> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<string> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default);
    Task ConfirmReceiptAsync(string receiptId, ConfirmReceiptRequest request, CancellationToken ct = default);
}
