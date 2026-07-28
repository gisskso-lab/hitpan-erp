# 백오피스 컨트롤러 스켈레톤 (W5 박제)

> **작성일**: 2026-05-26 (W5 자동 가도)
> **작성**: 백엔드 매니저 (Harvard·Oracle 30년) + 설계팀장 브라운킴
> **정합**: API_SPEC_BACKOFFICE.md 26 엔드포인트 1:1 매핑
> **헌법 정합**: #2 (tenant_id JWT) / #4 (decimal) / #7 (SaaS·ERP 권한 분리) / #15 (빈 catch 금지) / #16 (MySqlConnection 단일 사용) / #18 v3 / #22 (데이터 최소주의)
> **실 구현**: 본 문서는 박제만, 실 코드 작성은 W6 가도
> **결재**: 사장님 "응 다음결재" (2026-05-26, W5 자동 가도)

---

## 0. 공통 설계 원칙

### 0.1 베이스 라우트
- 모든 컨트롤러: `[Route("api/admin/{controller}")]`
- 운영: `https://backoffice.hitpan.app`
- 베타: `https://backoffice-beta.hitpan.app`

### 0.2 인증·권한 정합
- `[ApiController]` 필수
- `[Authorize(AuthenticationSchemes = "AdminJwt")]` — ERP tenant JWT와 절대 분리 (헌법 #7)
- TOTP 2FA: `[RequireTotp]` 커스텀 속성 (DELETE / Refund / Settle 영역)
- Policy 매핑: `[Authorize(Policy = "Admin.Tenants.Read")]`

### 0.3 DI 표준 시그니처
```csharp
private readonly IDbConnection _db;          // MySqlConnection (헌법 #16: Task.WhenAll 금지)
private readonly ILogger<XxxController> _logger;
private readonly ITraceContext _trace;       // trace_id 발급
```

### 0.4 응답 표준 (ApiResponse<T>)
```csharp
return Ok(new ApiResponse<TenantDetail> {
    Success = true,
    Data = result,
    Error = null,
    TraceId = _trace.Current,
    ServerTime = DateTime.UtcNow
});
```

### 0.5 헌법 #15 빈 catch 금지 표준
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "{Action} 실패 traceId={TraceId}", nameof(GetTenants), _trace.Current);
    throw;   // 또는 ApiResponse 실패 반환, silent swallow 절대 금지
}
```

---

## 1. AdminTenantsController.cs (7 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/tenants")]
[Authorize(AuthenticationSchemes = "AdminJwt")]
[Produces("application/json")]
public class AdminTenantsController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ITenantQueryService _query;   // Dapper 위임
    private readonly IProvisioningQueue _provisioning;
    private readonly ILogger<AdminTenantsController> _logger;
    private readonly ITraceContext _trace;

    public AdminTenantsController(
        IDbConnection db,
        ITenantQueryService query,
        IProvisioningQueue provisioning,
        ILogger<AdminTenantsController> logger,
        ITraceContext trace)
    {
        _db = db;
        _query = query;
        _provisioning = provisioning;
        _logger = logger;
        _trace = trace;
    }
}
```

### 1.1 GET /tenants
```csharp
/// <summary>고객사 목록 조회 (검색·필터·페이지)</summary>
/// <remarks>권한: Admin.Tenants.Read / 헌법 #18 v3 메타만</remarks>
[HttpGet]
[Authorize(Policy = "Admin.Tenants.Read")]
[ProducesResponseType(typeof(ApiResponse<PagedResult<TenantSummary>>), 200)]
public Task<IActionResult> GetTenants(
    [FromQuery] string? q,
    [FromQuery] TenantStatus? status,
    [FromQuery] string? tier,
    [FromQuery] string? industry,
    [FromQuery] int page = 1,
    [FromQuery] int size = 50);
```

### 1.2 GET /tenants/{id}
```csharp
/// <summary>고객사 단건 상세 (구독·결제·heartbeat 요약 포함)</summary>
/// <remarks>권한: Admin.Tenants.Read / 헌법 #16: QueryMultipleAsync 단일 connection</remarks>
[HttpGet("{id:guid}")]
[Authorize(Policy = "Admin.Tenants.Read")]
[ProducesResponseType(typeof(ApiResponse<TenantDetail>), 200)]
[ProducesResponseType(404)]
public Task<IActionResult> GetTenantDetail(Guid id);
```

### 1.3 POST /tenants
```csharp
/// <summary>고객사 수동 생성 (영업 직접 등록 예외 영역)</summary>
/// <remarks>권한: Admin.Tenants.Write + Role.Sales / 부수효과: provisioning 큐 enqueue</remarks>
[HttpPost]
[Authorize(Policy = "Admin.Tenants.Write")]
[ProducesResponseType(typeof(ApiResponse<TenantDetail>), 201)]
[ProducesResponseType(409)]   // business_number 중복
public Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req);
```

### 1.4 PATCH /tenants/{id}
```csharp
/// <summary>고객사 정보 수정 (business_number 변경 금지)</summary>
[HttpPatch("{id:guid}")]
[Authorize(Policy = "Admin.Tenants.Write")]
public Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest req);
```

### 1.5 PATCH /tenants/{id}/suspend
```csharp
/// <summary>고객사 정지 (미결제·정책 위반)</summary>
/// <remarks>부수효과: ERP 라이선스 무효화 큐 enqueue (헌법 #30 워치독 회수)</remarks>
[HttpPatch("{id:guid}/suspend")]
[Authorize(Policy = "Admin.Tenants.Suspend")]
public Task<IActionResult> SuspendTenant(Guid id, [FromBody] SuspendTenantRequest req);
```

### 1.6 PATCH /tenants/{id}/resume
```csharp
[HttpPatch("{id:guid}/resume")]
[Authorize(Policy = "Admin.Tenants.Suspend")]
public Task<IActionResult> ResumeTenant(Guid id);
```

### 1.7 DELETE /tenants/{id}
```csharp
/// <summary>고객사 해지 (soft delete + 30일 후 메타 폐기)</summary>
/// <remarks>권한: Admin.Tenants.Delete + 2FA 재확인 / 헌법 #22 정합</remarks>
[HttpDelete("{id:guid}")]
[Authorize(Policy = "Admin.Tenants.Delete")]
[RequireTotp]
public Task<IActionResult> DeleteTenant(Guid id, [FromBody] DeleteTenantRequest req);
```

---

## 2. AdminSubscriptionsController.cs (4 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/subscriptions")]
[Authorize(AuthenticationSchemes = "AdminJwt")]
public class AdminSubscriptionsController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ISubscriptionService _service;
    private readonly IBillingScheduler _billing;
    private readonly ILogger<AdminSubscriptionsController> _logger;
    private readonly ITraceContext _trace;
    // 생성자 DI 동일 패턴
}
```

### 2.1 GET /subscriptions
```csharp
[HttpGet]
[Authorize(Policy = "Admin.Subscriptions.Read")]
public Task<IActionResult> GetSubscriptions(
    [FromQuery] Guid? tenantId,
    [FromQuery] string? tier,
    [FromQuery] SubscriptionStatus? status,
    [FromQuery] int page = 1, [FromQuery] int size = 50);
```

### 2.2 POST /subscriptions
```csharp
/// <summary>신규 구독 생성 (수동 영역)</summary>
/// <remarks>부수효과: billing_cycles 첫 회 가도 enqueue</remarks>
[HttpPost]
[Authorize(Policy = "Admin.Subscriptions.Write")]
public Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest req);
```

### 2.3 PATCH /subscriptions/{id}/change-plan
```csharp
/// <summary>티어 업/다운그레이드 (proration 일할 차액 결제)</summary>
[HttpPatch("{id:guid}/change-plan")]
[Authorize(Policy = "Admin.Subscriptions.Write")]
public Task<IActionResult> ChangePlan(Guid id, [FromBody] ChangePlanRequest req);
// req: { NewTierId, EffectiveDate?, Proration: bool }
// 헌법 #4: 모든 금액 decimal
```

### 2.4 PATCH /subscriptions/{id}/cancel
```csharp
/// <summary>해지 예약 (ACTIVE → CANCELLATION_SCHEDULED → CANCELLED)</summary>
[HttpPatch("{id:guid}/cancel")]
[Authorize(Policy = "Admin.Subscriptions.Cancel")]
public Task<IActionResult> CancelSubscription(Guid id, [FromBody] CancelSubscriptionRequest req);
```

---

## 3. AdminPaymentsController.cs (3 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/payments")]
[Authorize(AuthenticationSchemes = "AdminJwt", Roles = "FINANCE,SUPER_ADMIN")]
public class AdminPaymentsController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IPaymentGatewayAdapter _toss;     // TossPaymentsAdapter
    private readonly IPaymentGatewayAdapter _kcp;      // KcpAdapter
    private readonly IRefundService _refund;
    private readonly ILogger<AdminPaymentsController> _logger;
    private readonly ITraceContext _trace;
}
```

### 3.1 GET /payments
```csharp
[HttpGet]
[Authorize(Policy = "Admin.Payments.Read")]
public Task<IActionResult> GetPayments(
    [FromQuery] Guid? tenantId,
    [FromQuery] PaymentStatus? status,
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    [FromQuery] string? provider,
    [FromQuery] int page = 1, [FromQuery] int size = 50);
```

### 3.2 POST /payments/refund
```csharp
/// <summary>환불 처리 (2FA 재확인)</summary>
/// <remarks>헌법 #4 amount decimal / 결제사 어댑터 위임 (본사 카드 원본 0)</remarks>
[HttpPost("refund")]
[Authorize(Policy = "Admin.Payments.Refund")]
[RequireTotp]
public Task<IActionResult> RefundPayment([FromBody] RefundRequest req);
// req: { PaymentId, Amount: decimal, Reason }
// resp: RefundResult { RefundId, Status, RefundedAt, ProviderRefundId }
```

### 3.3 GET /payments/{id}/invoice
```csharp
/// <summary>영수증·세금계산서 URL 조회 (URL만, 원본 0건 — 헌법 #22)</summary>
[HttpGet("{id:guid}/invoice")]
[Authorize(Policy = "Admin.Payments.Read")]
public Task<IActionResult> GetInvoice(Guid id);
```

---

## 4. AdminResellersController.cs (5 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/resellers")]
[Authorize(AuthenticationSchemes = "AdminJwt")]
public class AdminResellersController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IResellerService _service;
    private readonly ICommissionCalculator _commission;
    private readonly ILogger<AdminResellersController> _logger;
    private readonly ITraceContext _trace;
}
```

### 4.1 GET /resellers
```csharp
[HttpGet]
[Authorize(Policy = "Admin.Resellers.Read")]
public Task<IActionResult> GetResellers(
    [FromQuery] string? q,
    [FromQuery] string? contractType,
    [FromQuery] int page = 1, [FromQuery] int size = 50);
```

### 4.2 POST /resellers
```csharp
[HttpPost]
[Authorize(Policy = "Admin.Resellers.Write")]
public Task<IActionResult> CreateReseller([FromBody] CreateResellerRequest req);
// req: { CompanyName, BusinessNumber, ContractType, CommissionRate: decimal, ... }
```

### 4.3 PATCH /resellers/{id}
```csharp
[HttpPatch("{id:guid}")]
[Authorize(Policy = "Admin.Resellers.Write")]
public Task<IActionResult> UpdateReseller(Guid id, [FromBody] UpdateResellerRequest req);
// commission_rate 변경은 ResellerContractChange 별도 영역 가도
```

### 4.4 GET /resellers/{id}/commissions
```csharp
/// <summary>월별 수수료 산정 조회</summary>
/// <remarks>권한: Admin.Resellers.Read | Role.ResellerAdmin(자기 것만)</remarks>
[HttpGet("{id:guid}/commissions")]
[Authorize(Policy = "Admin.Resellers.Read")]
public Task<IActionResult> GetCommissions(
    Guid id,
    [FromQuery] int year,
    [FromQuery] int month);
// resp: { Period, TotalCommission: decimal, Items: CommissionItem[] }
```

### 4.5 POST /resellers/{id}/settle
```csharp
/// <summary>수수료 정산 가도 (Finance + 2FA)</summary>
[HttpPost("{id:guid}/settle")]
[Authorize(Policy = "Admin.Resellers.Settle")]
[RequireTotp]
public Task<IActionResult> SettleCommission(Guid id, [FromBody] SettleRequest req);
// req: { PeriodYear, PeriodMonth, SettleMethod: enum, Memo }
// 부수효과: commission_settlements INSERT + 회계 외부 위임
```

---

## 5. AdminTelemetryController.cs (3 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/telemetry")]
[Authorize(AuthenticationSchemes = "AdminJwt", Policy = "Admin.Telemetry.Read")]
public class AdminTelemetryController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ITelemetryReader _reader;          // 메타만 (헌법 #18 v3, #22)
    private readonly ILogger<AdminTelemetryController> _logger;
    private readonly ITraceContext _trace;
}
```

### 5.1 GET /telemetry/heartbeats
```csharp
/// <summary>고객 PC 워치독 ping (헌법 #30 — 메타만, 업무 데이터 0)</summary>
[HttpGet("heartbeats")]
public Task<IActionResult> GetHeartbeats(
    [FromQuery] int minutes = 60,
    [FromQuery] Guid? tenantId);
// resp: { Items: [{ TenantId, LastSeen, Status }] } — 업무 데이터 0 검증 필수
```

### 5.2 GET /telemetry/usage
```csharp
/// <summary>사용량 메트릭 (디바이스 수·DB 크기 카운터만)</summary>
/// <remarks>헌법 #22: 업무 데이터 카운트 제외, 라이선스 검증 메트릭만</remarks>
[HttpGet("usage")]
public Task<IActionResult> GetUsage(
    [FromQuery] Guid? tenantId,
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to);
```

### 5.3 GET /telemetry/alerts
```csharp
/// <summary>알림 큐 (워치독 이상·결제 실패 등)</summary>
[HttpGet("alerts")]
public Task<IActionResult> GetAlerts(
    [FromQuery] AlertSeverity? severity,
    [FromQuery] AlertStatus? status = AlertStatus.Open,
    [FromQuery] int page = 1, [FromQuery] int size = 50);
```

---

## 6. AdminAuthController.cs (4 엔드포인트)

### 클래스 영역
```csharp
[ApiController]
[Route("api/admin/auth")]
[AllowAnonymous]   // 로그인 자체는 비인증
public class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _auth;
    private readonly ITotpVerifier _totp;
    private readonly IAdminSessionStore _sessions;
    private readonly ILogger<AdminAuthController> _logger;
    private readonly ITraceContext _trace;
}
```

### 6.1 POST /auth/login
```csharp
/// <summary>1차 인증 (ID·비밀번호)</summary>
/// <remarks>응답: pre_auth_token (5분 만료), TOTP 단계로 가도</remarks>
[HttpPost("login")]
public Task<IActionResult> Login([FromBody] LoginRequest req);
// resp: { PreAuthToken, TotpRequired: true }
```

### 6.2 POST /auth/verify-totp
```csharp
/// <summary>2차 인증 (TOTP 6자리)</summary>
[HttpPost("verify-totp")]
public Task<IActionResult> VerifyTotp([FromBody] VerifyTotpRequest req);
// req: { PreAuthToken, TotpCode }
// resp: { AccessToken, RefreshToken, ExpiresIn, AdminUser: AdminUserSummary }
```

### 6.3 POST /auth/logout
```csharp
[HttpPost("logout")]
[Authorize(AuthenticationSchemes = "AdminJwt")]
public Task<IActionResult> Logout();
// 부수효과: admin_sessions 무효화 + refresh_token revoke
```

### 6.4 POST /auth/refresh
```csharp
[HttpPost("refresh")]
public Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req);
// req: { RefreshToken }
// resp: 신규 AccessToken (refresh는 회전 정책)
```

---

## 7. 헌법 정합 체크 (전 컨트롤러 일괄)

| 헌법 | 적용 영역 | 검증 |
|---|---|---|
| #2 tenant_id JWT 클레임 | 본 백오피스는 본사 직원용 — `[Authorize(AdminJwt)]`로 ERP tenant JWT와 절대 분리 | Issuer·Audience 분리 |
| #4 decimal | RefundRequest.Amount / CommissionRate / TierPrice 전부 decimal | DTO 전수 점검 |
| #7 SaaS·ERP 권한 혼용 금지 | AuthenticationScheme `AdminJwt` ≠ `TenantJwt`, Policy 네임스페이스 `Admin.*` 고정 | Startup 분리 등록 |
| #15 빈 catch 금지 | 전 컨트롤러 catch 블록에 `_logger.LogWarning(ex, ..., traceId)` 의무 | 코드 리뷰 체크리스트 |
| #16 MySqlConnection + Task.WhenAll 금지 | QueryMultipleAsync 또는 UNION ALL 단일 쿼리만 | TenantDetail 다중 KPI 패턴 |
| #18 v3 / #22 | telemetry·heartbeat 응답에 업무 데이터 컬럼 0건 검증 | data-minimalism CI 통과 |
| #19 warnings 0 | 컨트롤러 전수 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | CI Roslyn 게이트 |

---

## 8. DI 등록 영역 (Program.cs 예고)

```csharp
// 백오피스 전용 인증 스킴 (ERP와 분리)
builder.Services.AddAuthentication("AdminJwt")
    .AddJwtBearer("AdminJwt", opt => {
        opt.TokenValidationParameters.ValidIssuer = "hitpan-backoffice";
        opt.TokenValidationParameters.ValidAudience = "hitpan-admin-users";
    });

// Policy 매핑
builder.Services.AddAuthorization(opt => {
    opt.AddPolicy("Admin.Tenants.Read", p => p.RequireRole("OPS","SALES","SUPPORT","FINANCE","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Tenants.Write", p => p.RequireRole("OPS","SALES","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Tenants.Suspend", p => p.RequireRole("OPS","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Tenants.Delete", p => p.RequireRole("SUPER_ADMIN"));
    opt.AddPolicy("Admin.Subscriptions.Read", p => p.RequireRole("OPS","FINANCE","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Subscriptions.Write", p => p.RequireRole("OPS","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Subscriptions.Cancel", p => p.RequireRole("OPS","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Payments.Read", p => p.RequireRole("FINANCE","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Payments.Refund", p => p.RequireRole("FINANCE","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Resellers.Read", p => p.RequireRole("SALES","OPS","SUPER_ADMIN","RESELLER_ADMIN"));
    opt.AddPolicy("Admin.Resellers.Write", p => p.RequireRole("SALES","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Resellers.Settle", p => p.RequireRole("FINANCE","SUPER_ADMIN"));
    opt.AddPolicy("Admin.Telemetry.Read", p => p.RequireRole("OPS","SUPPORT","SUPER_ADMIN"));
});

// 서비스 DI
builder.Services.AddScoped<ITenantQueryService, TenantQueryService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IRefundService, RefundService>();
builder.Services.AddScoped<IResellerService, ResellerService>();
builder.Services.AddScoped<ICommissionCalculator, CommissionCalculator>();
builder.Services.AddScoped<ITelemetryReader, TelemetryReader>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddSingleton<ITotpVerifier, TotpVerifier>();
builder.Services.AddScoped<IPaymentGatewayAdapter, TossPaymentsAdapter>();
builder.Services.AddScoped<IPaymentGatewayAdapter, KcpAdapter>();
builder.Services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));   // Scoped — 헌법 #16
```

---

## 9. W6 실 구현 가도 예고

| 일자 | 산출물 |
|---|---|
| W6 D1 | DTO·RequestModel·ResponseModel 30종 박제 |
| W6 D2 | AdminTenantsController 실 구현 + 단위 테스트 |
| W6 D3 | AdminSubscriptions·Payments 실 구현 |
| W6 D4 | AdminResellers·Telemetry 실 구현 |
| W6 D5 | AdminAuth + TOTP 통합 + 통합 테스트 |
| W6 D6 | Swagger 문서 자동 생성 + Postman 컬렉션 export |

---

**박제자**: 백엔드 매니저 + 설계팀장 브라운킴
**검증**: 보안 매니저 1·2 + AI수석 + PM
**상태**: W5 자동 가도 박제 완료, 실 구현은 W6 가도
