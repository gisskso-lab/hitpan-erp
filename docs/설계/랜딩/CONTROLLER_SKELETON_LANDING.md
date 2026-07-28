# 랜딩 컨트롤러 스켈레톤 (W5 박제)

> **작성일**: 2026-05-26 (W5 자동 가도)
> **작성**: 백엔드 매니저 (Harvard·Oracle 30년) + 설계팀장 브라운킴
> **정합**: API_SPEC_LANDING.md 20 엔드포인트 1:1 매핑
> **헌법 정합**: #4 (decimal) / #15 (빈 catch 금지) / #16 (MySqlConnection 단일) / #18 v3 / #22 (데이터 최소주의) / #25 (쉽게)
> **결재**: 사장님 "응 다음결재" (2026-05-26, W5 자동 가도)

---

## 0. 공통 설계 원칙

### 0.1 베이스 라우트
- 모든 컨트롤러: `[Route("api/landing/{controller}")]`
- 운영: `https://www.hitpan.app`
- 베타: `https://beta.hitpan.app`

### 0.2 인증·Rate Limit
- 가입 영역: `[AllowAnonymous]` + `[EnableRateLimiting("landing-signup")]`
- 다운로드: `[Authorize(AuthenticationSchemes = "LicenseToken")]` (일회용)
- 정적 영역: `[AllowAnonymous]` + CDN Cache

### 0.3 DI 표준
```csharp
private readonly IDbConnection _db;
private readonly ILogger<XxxController> _logger;
private readonly ITraceContext _trace;
```

### 0.4 응답 표준 (ApiResponse<T>) — 백오피스와 동일 구조

---

## 1. LandingSignupController.cs (4 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing/signup")]
[AllowAnonymous]
[EnableRateLimiting("landing-signup")]
public class LandingSignupController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ISignupSessionStore _sessions;
    private readonly IBusinessNumberValidator _bizValidator;
    private readonly IR2UploadService _r2;                 // Cloudflare R2 위임 (헌법 #22)
    private readonly IBackofficePushClient _backofficePush;
    private readonly ILogger<LandingSignupController> _logger;
    private readonly ITraceContext _trace;
    // 생성자 DI
}
```

### 1.1 POST /signup
```csharp
/// <summary>가입 세션 생성 (1단계: 사업자 정보)</summary>
/// <remarks>부수효과: SignupSession 생성 + business_number 중복 검증</remarks>
[HttpPost]
[ProducesResponseType(typeof(ApiResponse<SignupSessionResult>), 200)]
[ProducesResponseType(409)]   // business_number 중복
public Task<IActionResult> StartSignup([FromBody] StartSignupRequest req);
// req: { CompanyName, BusinessNumber, RepresentativeName, Email, Phone, IndustryCategory }
// resp: { SessionId, ExpiresAt, NextStep: "EMAIL_VERIFY" }
```

### 1.2 POST /signup/business-license
```csharp
/// <summary>사업자등록증 업로드 (R2 사전서명 URL)</summary>
/// <remarks>헌법 #22: 파일은 Cloudflare R2 위임, 본사 DB는 메타만</remarks>
[HttpPost("business-license")]
[RequestSizeLimit(10_000_000)]   // 10MB
public Task<IActionResult> UploadBusinessLicense(
    [FromForm] Guid sessionId,
    IFormFile file);
// resp: { UploadId, OcrBusinessNumber, IsMatch }
```

### 1.3 POST /signup/verify-email
```csharp
/// <summary>이메일 OTP 요청·검증 (action: SEND | VERIFY)</summary>
/// <remarks>정책: 10분 만료, 5회 시도 한도</remarks>
[HttpPost("verify-email")]
[EnableRateLimiting("otp-send")]
public Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req);
// req SEND:   { SessionId, Action: "SEND" }
// req VERIFY: { SessionId, Action: "VERIFY", OtpCode }
// resp: { Verified: bool, NextStep: "PHONE_VERIFY" }
```

### 1.4 POST /signup/complete
```csharp
/// <summary>약관 동의 + 가입 확정</summary>
/// <remarks>부수효과: 백오피스 Push (tenants pre-create) — SEQUENCE_3SYSTEMS 정합</remarks>
[HttpPost("complete")]
public Task<IActionResult> CompleteSignup([FromBody] CompleteSignupRequest req);
// req: { SessionId, TermsVersion, Consents: { ServiceTerms, PrivacyPolicy, PaymentTerms, DataHandling, Marketing } }
// resp: { TenantId (pre-created), NextStep: "PAYMENT", PaymentIntentUrl }
```

---

## 2. LandingVerifyController.cs (2 엔드포인트, OTP·PASS)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing/verify-phone")]
[AllowAnonymous]
[EnableRateLimiting("landing-verify")]
public class LandingVerifyController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IPassAdapter _pass;
    private readonly IKakaoAuthAdapter _kakao;
    private readonly ICiCipher _ciCipher;          // AES-256 (헌법 #25 안전하게)
    private readonly ILogger<LandingVerifyController> _logger;
    private readonly ITraceContext _trace;
}
```

### 2.1 POST /verify-phone/start
```csharp
/// <summary>PASS / 카카오 인증 가도 시작</summary>
[HttpPost("start")]
public Task<IActionResult> StartPhoneVerify([FromBody] StartPhoneVerifyRequest req);
// req: { SessionId, Provider: "PASS" | "KAKAO" }
// resp: { RedirectUrl, CallbackToken }
```

### 2.2 POST /verify-phone/callback
```csharp
/// <summary>인증 콜백 (PASS / 카카오 → 본사)</summary>
/// <remarks>헌법 #25 안전하게: CI는 AES-256 암호화 후 DB 저장</remarks>
[HttpPost("callback")]
public Task<IActionResult> PhoneVerifyCallback([FromBody] PhoneVerifyCallbackRequest req);
// req: { CallbackToken, ProviderToken, CiEncrypted }
// resp: { Verified: true, NextStep: "TERMS" }
```

---

## 3. LandingBetaController.cs (3 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing/beta")]
[AllowAnonymous]
[EnableRateLimiting("landing-beta")]
public class LandingBetaController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IBetaApplicationService _beta;
    private readonly IOutputCache _cache;          // 10초 TTL
    private readonly ILogger<LandingBetaController> _logger;
    private readonly ITraceContext _trace;
}
```

### 3.1 POST /beta-apply
```csharp
/// <summary>베타 30곳 신청</summary>
/// <remarks>정책: 사업자번호 중복 차단</remarks>
[HttpPost("apply")]
public Task<IActionResult> ApplyBeta([FromBody] ApplyBetaRequest req);
// req: { CompanyName, BusinessNumber, RepresentativeName, IndustryCategory, ExpectedDeviceCount, PainPoints }
// resp: { ApplicationId, Status: "PENDING", QueuePosition }
```

### 3.2 GET /beta-status
```csharp
/// <summary>실시간 카운터 (Cloudflare Cache 10초 TTL)</summary>
[HttpGet("status")]
[OutputCache(Duration = 10)]
public Task<IActionResult> GetBetaStatus();
// resp: { Target: 30, Applied: N, Approved: M, Remaining: K, Deadline: datetime }
```

### 3.3 POST /beta-waitlist
```csharp
/// <summary>마감 후 대기 등록</summary>
[HttpPost("waitlist")]
public Task<IActionResult> JoinWaitlist([FromBody] WaitlistRequest req);
// req: { BusinessNumber, CompanyName, Email }
// resp: { WaitlistId, PriorityScore }
```

---

## 4. LandingPaymentController.cs (3 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing/payment")]
[AllowAnonymous]
public class LandingPaymentController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IPaymentGatewayAdapter _toss;
    private readonly IPaymentGatewayAdapter _kcp;
    private readonly IWebhookSignatureVerifier _webhookVerifier;
    private readonly ILicenseIssuer _license;
    private readonly IBackofficePushClient _backofficePush;
    private readonly ILogger<LandingPaymentController> _logger;
    private readonly ITraceContext _trace;
}
```

### 4.1 POST /payment/intent
```csharp
/// <summary>결제 인텐트 생성 (토스 위젯 / Mock)</summary>
/// <remarks>헌법 #4: amount는 decimal</remarks>
[HttpPost("intent")]
public Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest req);
// req: { SessionId, TierCode, BillingPeriod: "MONTHLY"|"ANNUAL", PaymentMethodType }
// resp: { IntentId, Provider, ProviderIntentId, Amount: decimal, ExpiresAt, WidgetConfig }
```

### 4.2 POST /payment/confirm
```csharp
/// <summary>결제 승인 콜백 → 라이선스 발급 + 백오피스 Push</summary>
[HttpPost("confirm")]
public Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentRequest req);
// req: { IntentId, ApprovalToken, ProviderPaymentId }
// resp: { Confirmed: true, LicenseToken, DownloadUrl, ReceiptUrl }
```

### 4.3 POST /payment/webhook
```csharp
/// <summary>결제사 비동기 webhook (토스·KCP) — 서명 검증 + idempotent</summary>
[HttpPost("webhook")]
[AllowAnonymous]
public async Task<IActionResult> PaymentWebhook(
    [FromHeader(Name = "X-Provider")] string provider,
    [FromHeader(Name = "X-Signature")] string signature);
// 본문 raw 읽어 서명 검증 후 처리, 200 OK idempotent
```

---

## 5. LandingDownloadController.cs (4 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing")]
public class LandingDownloadController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ILicenseValidator _licenseValidator;
    private readonly IR2PresignService _r2Presign;
    private readonly IInstallerManifestProvider _manifest;
    private readonly ILogger<LandingDownloadController> _logger;
    private readonly ITraceContext _trace;
}
```

### 5.1 GET /license/{token}
```csharp
/// <summary>라이선스 키 정보 조회 (다운로드 페이지 진입 인증)</summary>
[HttpGet("license/{token}")]
[AllowAnonymous]
public Task<IActionResult> GetLicense(string token);
// resp: { LicenseId, TenantId, Tier, DeviceCountMax, ExpiresAt, Installer: { Version, DownloadUrl, Sha256 } }
```

### 5.2 GET /download/installer
```csharp
/// <summary>설치 EXE 다운로드 (R2 사전서명 URL, 1시간 만료)</summary>
[HttpGet("download/installer")]
[AllowAnonymous]
public Task<IActionResult> DownloadInstaller([FromQuery] string licenseToken);
// 302 Redirect → R2 사전서명 URL
// 부수효과: InstallerDownload 로그 기록
```

### 5.3 POST /download/complete
```csharp
/// <summary>다운로드 완료 신고 (해시 검증 후)</summary>
[HttpPost("download/complete")]
[AllowAnonymous]
public Task<IActionResult> CompleteDownload([FromBody] CompleteDownloadRequest req);
// req: { LicenseToken, Sha256Verified: bool }
// resp: { Acknowledged: true }
```

### 5.4 GET /installer/manifest
```csharp
/// <summary>최신 설치 EXE 매니페스트 (공개)</summary>
[HttpGet("installer/manifest")]
[AllowAnonymous]
[OutputCache(Duration = 300)]   // CDN 5분
public Task<IActionResult> GetInstallerManifest();
// resp: { Version, ReleasedAt, FileSize, Sha256, ReleaseNotesUrl }
```

---

## 6. LandingStaticController.cs (4 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/landing")]
[AllowAnonymous]
public class LandingStaticController : ControllerBase
{
    private readonly IPricingService _pricing;
    private readonly IFeatureService _features;
    private readonly IInstallationGuideService _guide;
    private readonly ICtaLogger _cta;
    private readonly ILogger<LandingStaticController> _logger;
    private readonly ITraceContext _trace;
}
```

### 6.1 GET /pricing
```csharp
/// <summary>가격 티어 + FAQ + 베타 특가 (CDN Cache 5분)</summary>
[HttpGet("pricing")]
[OutputCache(Duration = 300)]
public Task<IActionResult> GetPricing();
// resp: { Tiers: PricingTier[], BetaOffer, Faq: PricingFAQ[] }
```

### 6.2 GET /features
```csharp
/// <summary>기능 영역 + 비교표 (CDN Cache 15분)</summary>
[HttpGet("features")]
[OutputCache(Duration = 900)]
public Task<IActionResult> GetFeatures();
```

### 6.3 GET /installation-guide
```csharp
/// <summary>설치 매뉴얼 (스크린샷·영상, CDN 30분)</summary>
[HttpGet("installation-guide")]
[OutputCache(Duration = 1800)]
public Task<IActionResult> GetInstallationGuide();
```

### 6.4 POST /cta-event
```csharp
/// <summary>CTA 클릭 로그 (보관 90일)</summary>
[HttpPost("cta-event")]
[EnableRateLimiting("cta-event")]
public Task<IActionResult> LogCtaEvent([FromBody] CtaEventRequest req);
// req: { CtaLabel, VisitorSessionId, Utm: { Source, Medium, Campaign } }
// resp: { Recorded: true }
```

---

## 7. 헌법 정합 체크

| 헌법 | 적용 영역 | 검증 |
|---|---|---|
| #4 decimal | Amount / MonthlyPrice / AnnualPrice 전부 decimal | DTO 전수 점검 |
| #15 빈 catch 금지 | webhook·OTP·결제 콜백 모든 catch 블록에 `_logger.LogWarning(ex, ..., traceId)` 의무 | 코드 리뷰 |
| #16 MySqlConnection 단일 | 다중 KPI 없음, 단순 INSERT/SELECT만 | 패턴 검증 |
| #18 v3 / #22 | 사업자등록증 파일 R2 위임, CI AES-256, 카드 원본 0건 | data-minimalism CI 통과 |
| #25 쉽게 | 가입 5단계 자동 가도, 다운로드 1클릭 | UX 테스트 |

---

## 8. Rate Limit 정책 (Program.cs 등록)

```csharp
builder.Services.AddRateLimiter(opt =>
{
    // 1.1~1.4: IP+사업자번호별 시간당 5회
    opt.AddPolicy("landing-signup", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new() { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));

    // 1.3 OTP: 이메일/휴대폰별 시간당 3회
    opt.AddPolicy("otp-send", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Request.Headers["X-Identity"].ToString(),
            factory: _ => new() { PermitLimit = 3, Window = TimeSpan.FromHours(1) }));

    // 2: PASS / 카카오 시간당 5회
    opt.AddPolicy("landing-verify", ctx => /* ... */);

    // 3: 베타 신청 시간당 5회
    opt.AddPolicy("landing-beta", ctx => /* ... */);

    // 6.4 CTA: IP별 분당 60회
    opt.AddPolicy("cta-event", ctx => /* ... */);

    // 글로벌: IP별 분당 60회
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});
```

---

## 9. DI 등록 영역 (Program.cs 예고)

```csharp
// 라이선스 토큰 인증
builder.Services.AddAuthentication()
    .AddScheme<LicenseTokenOptions, LicenseTokenHandler>("LicenseToken", _ => { });

// 서비스 DI
builder.Services.AddScoped<ISignupSessionStore, SignupSessionStore>();
builder.Services.AddScoped<IBusinessNumberValidator, BusinessNumberValidator>();
builder.Services.AddScoped<IR2UploadService, CloudflareR2UploadService>();
builder.Services.AddScoped<IBackofficePushClient, BackofficePushClient>();
builder.Services.AddScoped<IPassAdapter, PassAdapter>();
builder.Services.AddScoped<IKakaoAuthAdapter, KakaoAuthAdapter>();
builder.Services.AddSingleton<ICiCipher, Aes256CiCipher>();
builder.Services.AddScoped<IBetaApplicationService, BetaApplicationService>();
builder.Services.AddScoped<IPaymentGatewayAdapter, TossPaymentsAdapter>();
builder.Services.AddScoped<IPaymentGatewayAdapter, KcpAdapter>();
builder.Services.AddSingleton<IWebhookSignatureVerifier, WebhookSignatureVerifier>();
builder.Services.AddScoped<ILicenseIssuer, LicenseIssuer>();
builder.Services.AddScoped<ILicenseValidator, LicenseValidator>();
builder.Services.AddScoped<IR2PresignService, CloudflareR2PresignService>();
builder.Services.AddScoped<IInstallerManifestProvider, InstallerManifestProvider>();
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IFeatureService, FeatureService>();
builder.Services.AddScoped<IInstallationGuideService, InstallationGuideService>();
builder.Services.AddScoped<ICtaLogger, CtaLogger>();
builder.Services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));   // Scoped — 헌법 #16
builder.Services.AddOutputCache();
```

---

## 10. W6 실 구현 가도 예고

| 일자 | 산출물 |
|---|---|
| W6 D1 | DTO·RequestModel·ResponseModel 30종 박제 |
| W6 D2 | LandingSignupController 실 구현 + 사업자번호 검증 통합 |
| W6 D3 | LandingVerify (PASS/카카오) 어댑터 통합 |
| W6 D4 | LandingPayment (토스 위젯) + LandingBeta |
| W6 D5 | LandingDownload (R2 사전서명) + LandingStatic + CDN |
| W6 D6 | Rate Limit 실측 + OWASP ZAP 베타 검증 |

---

**박제자**: 백엔드 매니저 + 설계팀장 브라운킴
**검증**: 보안 매니저 1·2 + AI수석 + 마케팅팀장
**상태**: W5 자동 가도 박제 완료, 실 구현은 W6 가도
