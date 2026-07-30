# 결제 어댑터 인터페이스 명세

> 작성일: 2026-05-26
> 작성: 백엔드 매니저 + 설계팀장 브라운킴
> 정합: API_SPEC_LANDING.md / API_SPEC_BACKOFFICE.md / DB_SCHEMA_BACKOFFICE.md
> 헌법: #4(decimal)·#22(카드 원본 0)·#25(안전하게)

---

## 0. 설계 원칙

### 0.1 어댑터 패턴
- 결제사 의존성을 인터페이스 뒤로 격리
- 환경별 어댑터 선택 (appsettings.json `PaymentProvider`)
- 토스 위젯 v2 / KCP B2B / Mock 3개 구현체
- 향후 카카오페이·네이버페이 추가 시 확장만 (헌법 #11 — 덮어쓰기 금지)

### 0.2 책임 경계
- 어댑터 책임: 결제사 SDK 호출·서명·콜백 검증·환불·정기결제
- 어댑터 비책임: DB 갱신·이벤트 발행 (상위 PaymentService 영역)

### 0.3 헌법 #22 (데이터 최소주의) 절대 준수
- 카드 원본·CVC·만료일 본사 DB 0건
- 어댑터는 결제사 토큰만 받아 보관 위임
- 카드 last4 + 브랜드만 표시용 보유

---

## 1. IPaymentProvider 인터페이스

```csharp
namespace HitPan.Payment.Abstractions;

public interface IPaymentProvider
{
    /// <summary>
    /// 어댑터 식별자 ("Toss" / "Kcp" / "Mock")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 결제 인텐트 생성 (위젯·리디렉션 URL 발급)
    /// </summary>
    Task<PaymentIntentResult> CreateIntentAsync(
        PaymentIntentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 결제 승인 (콜백 토큰·서명 검증 포함)
    /// </summary>
    Task<PaymentConfirmResult> ConfirmAsync(
        string intentId,
        string approvalToken,
        CancellationToken ct = default);

    /// <summary>
    /// 환불 (전액·부분)
    /// </summary>
    Task<RefundResult> RefundAsync(
        string paymentId,
        decimal amount,        // 헌법 #4
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// 결제 상태 조회 (idempotent webhook 처리용)
    /// </summary>
    Task<PaymentStatus> GetStatusAsync(
        string paymentId,
        CancellationToken ct = default);

    /// <summary>
    /// 정기결제 빌링키 등록 (월 자동결제)
    /// </summary>
    Task<BillingKeyResult> RegisterBillingKeyAsync(
        BillingKeyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 빌링키로 정기결제 청구
    /// </summary>
    Task<PaymentResult> ChargeBillingKeyAsync(
        string billingKey,
        decimal amount,        // 헌법 #4
        string orderId,
        CancellationToken ct = default);

    /// <summary>
    /// Webhook 서명 검증 (provider 시크릿 키)
    /// </summary>
    bool VerifyWebhookSignature(
        string payload,
        string signature);
}
```

---

## 2. DTO 명세

### 2.1 PaymentIntentRequest
```csharp
public record PaymentIntentRequest(
    string OrderId,                  // 본사 발급 (intent_id)
    decimal Amount,                  // 헌법 #4
    string Currency,                 // "KRW"
    PaymentMethodType MethodType,    // CARD/BANK_TRANSFER/TAX_INVOICE
    BillingPeriod Period,            // MONTHLY/ANNUAL
    string CustomerName,
    string CustomerEmailHash,        // 해시만 (헌법 #22)
    string SuccessReturnUrl,
    string FailureReturnUrl,
    string? CallbackUrl              // webhook URL
);
```

### 2.2 PaymentIntentResult
```csharp
public record PaymentIntentResult(
    bool Success,
    string? ProviderIntentId,
    string? PaymentPageUrl,          // 위젯·리디렉션 URL
    Dictionary<string, string>? WidgetConfig,  // 토스 위젯용
    DateTime ExpiresAt,
    PaymentError? Error
);
```

### 2.3 PaymentConfirmResult
```csharp
public record PaymentConfirmResult(
    bool Success,
    string? ProviderPaymentId,
    string? ReceiptUrl,              // 결제사 위임 URL (헌법 #22)
    string? CardLast4,               // 4자리만
    string? CardBrand,
    string? BillingKey,              // 정기결제 등록 시
    DateTime? PaidAt,
    PaymentError? Error
);
```

### 2.4 RefundResult
```csharp
public record RefundResult(
    bool Success,
    string? ProviderRefundId,
    decimal RefundedAmount,
    DateTime? RefundedAt,
    PaymentError? Error
);
```

### 2.5 PaymentError
```csharp
public record PaymentError(
    string Code,                     // PROVIDER_DECLINED / NETWORK / INVALID_KEY 등
    string Message,
    string? ProviderRawCode,
    bool IsRetryable
);
```

### 2.6 BillingKeyRequest / BillingKeyResult / PaymentResult / PaymentStatus
- 동일 패턴, 부록 상세

---

## 3. 어댑터 구현 영역 3종

### 3.1 TossPaymentsAdapter (7월 실연결, 위젯 v2)
- SDK: `@tosspayments/payment-sdk` (프론트) + REST API (서버)
- BaseUrl: `https://api.tosspayments.com/v1`
- 인증: Basic Auth (Secret Key)
- 빌링키 지원: 카드 자동결제
- Webhook: 서명 HMAC-SHA256 검증
- 영수증: 토스 발급 URL (본사 DB는 URL만 보유)
- 헌법 #22: 카드 토큰·빌링키만 보관

### 3.2 KcpAdapter (B2B 영업 정합)
- BaseUrl: `https://stg-spl.kcp.co.kr` (스테이징) / `https://spl.kcp.co.kr`
- 인증: PEM 인증서 + Site Code
- 결제수단: 카드·계좌이체·**세금계산서 결제** (B2B 영업 영역 핵심)
- 세금계산서 연동: 메이크빌·이세로 위임 (헌법 #18 v3)
- 빌링키 미지원 → 정기결제는 토스로 위임

### 3.3 MockPaymentAdapter (개발·테스트, 즉시 가도)
- 환경: appsettings `PaymentProvider=Mock`
- 동작: 즉시 SUCCESS 반환 (테스트 시나리오는 amount 끝자리로 분기)
  - `amount.toString().endsWith("99")` → FAIL
  - `amount.toString().endsWith("88")` → TIMEOUT
  - 그 외 → SUCCESS
- Mock 빌링키: `mock_billing_{guid}`
- 7월 토스 실연결 후 운영 환경에선 비활성화 (개발만 유지)

---

## 4. DI 등록 영역

### 4.1 Program.cs (API)
```csharp
// 환경별 어댑터 선택
var paymentProvider = builder.Configuration["PaymentProvider"] ?? "Mock";

builder.Services.Configure<TossPaymentsOptions>(
    builder.Configuration.GetSection("Payment:Toss"));
builder.Services.Configure<KcpOptions>(
    builder.Configuration.GetSection("Payment:Kcp"));

builder.Services.AddHttpClient<TossPaymentsAdapter>();
builder.Services.AddHttpClient<KcpAdapter>();

switch (paymentProvider.ToLowerInvariant())
{
    case "toss":
        builder.Services.AddScoped<IPaymentProvider, TossPaymentsAdapter>();
        break;
    case "kcp":
        builder.Services.AddScoped<IPaymentProvider, KcpAdapter>();
        break;
    case "mock":
    default:
        builder.Services.AddScoped<IPaymentProvider, MockPaymentAdapter>();
        break;
}

// 다중 어댑터 동시 가도 영역 (KCP B2B + Toss 정기결제 동시)
builder.Services.AddKeyedScoped<IPaymentProvider, TossPaymentsAdapter>("toss");
builder.Services.AddKeyedScoped<IPaymentProvider, KcpAdapter>("kcp");
builder.Services.AddScoped<IPaymentRouter, PaymentRouter>();
```

### 4.2 PaymentRouter (다중 어댑터 라우팅)
- `PaymentMethodType.TAX_INVOICE` → KCP
- 정기결제 (BillingKey) → Toss
- 그 외 단건 카드 → 환경 설정 따름

### 4.3 appsettings.json
```json
{
  "PaymentProvider": "Mock",
  "Payment": {
    "Toss": {
      "ClientKey": "...",
      "SecretKey": "...",
      "WebhookSecret": "...",
      "BaseUrl": "https://api.tosspayments.com/v1"
    },
    "Kcp": {
      "SiteCode": "...",
      "CertPath": "/etc/hitpan/kcp.pem",
      "BaseUrl": "https://spl.kcp.co.kr"
    }
  }
}
```

---

## 5. 헌법 정합 체크

| 헌법 | 적용 |
|---|---|
| #4 decimal | Amount·RefundedAmount·ChargeBillingKey 모두 decimal |
| #11 덮어쓰기 금지 | 신규 인터페이스, 기존 코드 영향 0 |
| #22 데이터 최소주의 | 카드 원본·CVC·만료일·계좌번호 0건. 토큰·last4·브랜드만 |
| #25 안전하게 | Webhook 서명 검증·재시도 멱등성·timeout 명시 |

---

## 6. 사장님 결재 영역
- 토스 7월 실연결 일정 (위젯 v2 적용)
- KCP B2B 세금계산서 결제 어댑터 추가 (메이크빌 연동)
- 다중 어댑터 동시 가도 vs 단일 어댑터 선택 결재
- Mock 어댑터 운영 비활성화 정책 (Production 절대 사용 금지)

## 7. W3 가도 예고
- TossPaymentsAdapter 구현체 스켈레톤 코드 (호출 영역만)
- KcpAdapter 인증서 발급 절차 (이세로·메이크빌 협의)
- MockPaymentAdapter 단위 테스트 100건+ 명세
- PaymentRouter 라우팅 규칙 매트릭스 박제
- Webhook 처리 멱등성 키 설계 (provider_payment_id + event_id)
