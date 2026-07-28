# 17. Infrastructure + DI + 미들웨어 전수 학습서

**작성:** CTO + DB 매니저
**범위:** API/Web Program.cs, Infrastructure, HostedServices, Middleware, Value Converter, Domain Entities

---

## 1. API Program.cs 부트스트랩

### 환경변수 로드
- `.env` 파일 자동 로드 (상위 디렉토리 탐색)
- DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD 필수
- ERP_ENCRYPTION_KEY, JWT_SECRET, JWT_ISSUER, JWT_AUDIENCE
- HITPAN_LOG_DIR

### Serilog 설정
- MinimumLevel: Information
- Override: Microsoft.AspNetCore=Warning, Microsoft.EntityFrameworkCore=Warning
- Sinks: Console + File (일별 롤링, 14일 보관)
- 출력 템플릿: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}`

### 빌더 설정
- `builder.Host.UseSerilog()`
- `builder.Host.UseWindowsService()` (Windows 서비스 호스트)
- EXE 옆 wwwroot 자동 탐색 (Blazor WASM 정적 파일)

## 2. DI 등록 흐름

```
AddHttpContextAccessor → AddMemoryCache → AddDataProtection
AddScoped<CurrentTenant + ICurrentTenant>
AddSingleton<IEncryptionService, IHashService, IBinaryCryptoService>
AddInfrastructure() (DB 연결 + Seeders + DbContext)
AddScoped<IUnitOfWork, IAuthUserLookup>
AddScoped (43 비즈니스 서비스)
AddSingleton<MigrationJobStore>
AddJwtAuthentication() + AddAuthorization(11 정책)
AddHostedService<IdempotencyCleanupService>
AddHostedService<IntegrityCheckService>
```

## 3. InfrastructureExtensions

```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services)
{
    var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
    var db = Environment.GetEnvironmentVariable("DB_NAME") ?? throw ...;
    var user = Environment.GetEnvironmentVariable("DB_USER") ?? throw ...;
    var pwd = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? throw ...;

    var connStr = $"Server={host};Port={port};Database={db};User={user};Password={pwd};" +
                  "DefaultCommandTimeout=90;AllowLoadLocalInfile=true;";

    var serverVersion = new MariaDbServerVersion(new Version(11, 4, 0));

    services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connStr, serverVersion,
            x => x.MigrationsAssembly("HitPan.Infrastructure")));

    services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));

    services.AddScoped<CommonCodeSeeder>();
    services.AddScoped<SystemSeeder>();

    return services;
}
```

**핵심:**
- `DefaultCommandTimeout=90` (대량 집계 안전마진)
- `AllowLoadLocalInfile=true` (MySqlBulkCopy 활성화, 마이그 성능 8분→30초)
- MariaDB 11.4.0 고정 버전 (AutoDetect 타이밍 민감 회피)

## 4. Authorization Policy 11개

| Policy | 조건 |
|---|---|
| SalesOnly | Role: system_admin, sales_manager, sales_user, TenantAdmin, tenant_admin |
| SalesManager | Role: system_admin, sales_manager |
| PurchaseOnly | Role: system_admin, purchase_manager, TenantAdmin, tenant_admin |
| AccountOnly | Role: system_admin, account_manager, TenantAdmin, tenant_admin |
| HROnly | Role: system_admin, hr_manager, TenantAdmin, tenant_admin |
| AdminOnly | Role: system_admin |
| PlatformOnly | account_type=platform_admin |
| ResellerOnly | account_type in (reseller_admin, platform_admin) |
| TenantOnly | account_type in (tenant_admin, tenant_user, platform_admin) |
| TenantProfile | account_type in (platform_admin, reseller_admin, tenant_admin, tenant_user) |
| TenantAdminOnly | account_type in (tenant_admin, platform_admin) |

## 5. JWT 클레임 구조

- user_id, name, email
- role (UserRole enum)
- account_type (platform_admin/reseller_admin/tenant_admin/tenant_user)
- tenant_id, reseller_id?, platform_id?
- token_type (access/download)
- doc_id (download 토큰 전용)

## 6. HostedServices

### IdempotencyCleanupService
- 1시간 주기
- `DELETE FROM idempotency_keys WHERE expires_at < NOW(6) LIMIT 10000`
- 자체 DB 연결 (Scoped 회피)
- fail-open

### IntegrityCheckService
- 10분 주기, 시작 1분 후 첫 실행
- 검증 2종:
  1) 음수 재고: `SELECT * FROM item_stock WHERE current_qty < 0`
  2) 월별 매출 불일치: monthly_summary vs journal_lines
- audit_trail에 'integrity_alert' 기록

## 7. 미들웨어 파이프라인 11단계

| # | 미들웨어 | 용도 |
|---|---|---|
| 1 | GlobalExceptionMiddleware | InvalidOperation→400, Unauthorized→401, MySQL 1451/1452→409, else→500 |
| 2 | HealthIpWhitelistMiddleware | /health IP 화이트리스트 (기본 127.0.0.1, ::1) |
| 3 | CORS "BlazorWasmDev" | 크로스 오리진 |
| 4 | BlazorFrameworkFiles + StaticFiles | 정적 파일 |
| 5 | AuditLogMiddleware | /api/* 요청 후 audit_logs INSERT |
| 6 | Authentication | JWT 검증 |
| 7 | Authorization | 권한 확인 |
| 8 | RateLimitMiddleware | 로그인 5분 1000회 / 쓰기 분당 600 / export 분당 30 / 전체 분당 3000 |
| 9 | TenantMiddleware | HttpContext.Items에 TenantId/UserId/AccountType 설정 |
| 10 | SessionLimitMiddleware | 구독 티어별 동시 세션 제한 (basic 8 / pro 20 / premium 100 / trial 50) |
| 11 | IdempotencyMiddleware | [IdempotencyKey] 액션 멱등성 (24h TTL) |

## 8. 보안 헤더

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: default-src 'self'; ... (Production만)
Strict-Transport-Security: max-age=31536000; includeSubDomains (Production만)
```

## 9. Value Converter (AES) 적용 컬럼 15개

### EncryptedValueConverter (string ↔ Base64 string)
- tenants.biz_no, tenants.tel
- partners.biz_no, partners.tel, partners.email, partners.fax, partners.bank_account
- employees.birth_date, employees.bank_account, employees.base_salary

### EncryptedBinaryValueConverter (string ↔ byte[] / VARBINARY)
- employees.resident_no_encrypted, salary_encrypted, salary_extra_encrypted
- partners.ceo_resident_no_encrypted
- etax_send_history.raw_response_encrypted

## 10. EncryptionService

- 알고리즘: AES-256-CBC, PKCS7 패딩
- IV: 매 호출 랜덤 생성 (16바이트)
- 형식: IV(16) + 암호문 → Base64
- 키: ERP_ENCRYPTION_KEY 환경변수 (Base64 또는 UTF8 32바이트)

## 11. Domain Entities 11종 핵심

| 엔티티 | 테이블 | 특수 |
|---|---|---|
| BaseEntity | (추상) | Id, CreatedAt/By, UpdatedAt/By |
| ITenantEntity | (인터페이스) | Global Query Filter 자동 적용 |
| Tenant | tenants | TenantCode, BizNo(암호화), Status, TrialEndsAt, LicenseKeyHash |
| User | users | Email(평문), PasswordHash, Role, AccountType, FailedLoginCount, LockoutEnd |
| Employee | employees | UserId, EmpNo, IdNoHash, BirthDate(암호화), BankAccount(암호화), BaseSalary(암호화), AnnualLeaveTotal/Used |
| Partner | partners | PartnerCode, PartnerType, BizNo(암호화), CreditLimit, PaymentTerms |
| Item | items | ItemCode, ItemType, Unit, StdPrice, CostPrice, SafeStock |
| Warehouse | warehouses | WhCode, WhType, Location |
| PurchaseOrder | purchase_orders | PoNo, Status, IsAuto |
| SalesOrder | sales_orders | OrderNo, Status, IsAuto |
| StockLedger | stock_ledger | LedgerId(자동증분 PK), Ym, MoveType, SourceType, SourceId, QtyIn/QtyOut |

## 12. AppDbContext 특수 동작

1. **자동 타임스탬프** — SaveChanges 시점에 CreatedAt/UpdatedAt 갱신
2. **Global Query Filter** — 모든 ITenantEntity에 자동 `WHERE tenant_id=@CurrentTenant`
3. **TenantConfiguration/UserConfiguration/...** — IEntityTypeConfiguration 분리

## 13. Web Program.cs (Blazor WASM)

```csharp
var apiBase = builder.Configuration["ApiBaseUrl"]
    ?? Environment.GetEnvironmentVariable("HitPan__ApiBaseUrl")
    ?? Environment.GetEnvironmentVariable("ApiBaseUrl")
    ?? "http://localhost:5257";

builder.Services.AddMudServices();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<HitPanProtectedLocalStorage>();
builder.Services.AddScoped<IAuthTokenRefresher, AuthTokenRefresher>();
builder.Services.AddScoped<HitPanAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<HitPanAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();
// ... 39개 서비스
builder.Services.AddTransient<HitPanApiAuthHandler>();
builder.Services.AddScoped(sp => new HttpClient(new HitPanApiAuthHandler(...)
    { InnerHandler = new HttpClientHandler() })
    { BaseAddress = apiUri, Timeout = TimeSpan.FromMinutes(10) });
```
