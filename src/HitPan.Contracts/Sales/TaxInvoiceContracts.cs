namespace HitPan.Contracts.Sales;

// 3계층 세금계산서 1계층 (내부 마킹) — DESIGN_PRINCIPLES §7
//   거래명세서(confirmed) → [계산서 발행] 버튼 → tax_invoice INSERT
//   취소: status=canceled, 역분개·monthly_summary 차감은 서비스 책임
//
// 본 라운드는 1계층만. 2/3계층 (전자세금계산서)은 별도 작지서.
//
// 멱등키: 발행/취소 둘 다 [IdempotencyKey] 필수
//   - 같은 키 재요청 → 캐시 응답 (작4 미들웨어가 처리)
//   - DB 레벨 추가 보호: tax_invoices.idempotency_key 컬럼

// === 발행 ===========================================================
public sealed record IssueTaxInvoiceRequest(
    string DeliveryId,           // 거래명세서 ID (varchar(36))
    string? Memo);               // 발행 메모 (선택)

public sealed record TaxInvoiceResponse(
    string InvoiceId,
    string TenantId,
    string DeliveryId,
    string InvoiceNo,            // 자동 생성된 계산서 번호
    DateTime IssuedAt,
    string IssuedBy,
    decimal AmountTotal,         // 공급가액
    decimal VatTotal,            // 부가세
    decimal GrandTotal,          // amount + vat
    string Status,               // issued | canceled
    string EtaxStatus,           // pending | issued | failed (2/3계층 placeholder)
    DateTime? EtaxIssuedAt);

// === 취소 ===========================================================
public sealed record CancelTaxInvoiceRequest(
    string Reason);              // 취소 사유 (감사 추적)

public sealed record CancelTaxInvoiceResponse(
    string InvoiceId,
    DateTime CanceledAt,
    string Status);              // 항상 "canceled"

// === 표준 에러 응답 ==================================================
public sealed record TaxInvoiceErrorResponse(
    string Error,                // delivery_not_found | delivery_not_confirmed | already_issued | already_canceled | forbidden
    string Message)
{
    public static TaxInvoiceErrorResponse DeliveryNotFound() =>
        new("delivery_not_found", "거래명세서를 찾을 수 없습니다.");

    public static TaxInvoiceErrorResponse DeliveryNotConfirmed() =>
        new("delivery_not_confirmed", "확정된 거래명세서만 계산서 발행이 가능합니다.");

    public static TaxInvoiceErrorResponse AlreadyIssued() =>
        new("already_issued", "이미 계산서가 발행된 거래명세서입니다.");

    public static TaxInvoiceErrorResponse AlreadyCanceled() =>
        new("already_canceled", "이미 취소된 계산서입니다.");
}

// === 목록 조회 ======================================================
public sealed record TaxInvoiceListItem(
    string InvoiceId,
    string DeliveryId,
    string DeliveryNo,
    string InvoiceNo,
    DateTime IssuedAt,
    string PartnerId,
    string PartnerName,
    decimal AmountTotal,
    decimal VatTotal,
    string Status,
    string EtaxStatus);
