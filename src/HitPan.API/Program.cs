using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.API.Extensions;
using HitPan.API.HostedServices;
using HitPan.API.Hubs;
using HitPan.API.Middleware;
using HitPan.API.Services;
using HitPan.Infrastructure.Events;
using HitPan.Infrastructure.Extensions;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Persistence.Seed;
using HitPan.Infrastructure.Security;
using HitPan.API.Security;
using QuestPDF.Infrastructure;
using Serilog;
using Serilog.Events;

using Microsoft.AspNetCore.Components.WebAssembly.Server;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
QuestPDF.Settings.License = LicenseType.Community;

// ── .env 파일 로드 (시크릿 분리) ──
// 프로젝트 루트 → 상위 탐색. 파일 없으면 무시 (프로덕션은 OS 환경변수 사용).
LoadDotEnv();

// ── Serilog 부트스트랩 (요청/에러 구조화 로그) ──
var logDir = Environment.GetEnvironmentVariable("HITPAN_LOG_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDir);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDir, "hitpan-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Host.UseWindowsService();

// EXE 옆 wwwroot가 있으면 WebRoot로 설정 (installer 모드)
var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
var exeWebRoot = Path.Combine(exeDir, "wwwroot");
if (Directory.Exists(exeWebRoot) && File.Exists(Path.Combine(exeWebRoot, "index.html")))
{
    builder.Environment.WebRootPath = exeWebRoot;
    builder.Environment.ContentRootPath = exeDir;
}

var isDevelopment = builder.Environment.IsDevelopment();

// Add services to the container.

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection();  // DPAPI 기반 비밀번호 보호 (인증서 등)
builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
// W2 D3 (2026-05-12 A-2 결재): MdbMigrationService 형사영역 처리용 추상화 어댑터
builder.Services.AddSingleton<HitPan.Application.Interfaces.IBinaryCryptoService, BinaryCryptoServiceAdapter>();
builder.Services.AddSingleton<IHashService, HashService>();
builder.Services.AddInfrastructure();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAuthUserLookup, AuthUserLookup>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<ISalesService, SalesService>();
// 세금계산서 1계층 (DESIGN_PRINCIPLES §7 / 작업지시서 20260425작2)
builder.Services.AddScoped<ITaxInvoiceService, TaxInvoiceService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IDeliveryBatchService, DeliveryBatchService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<IBomService, BomService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITenantCertificateService, TenantCertificateService>();
builder.Services.AddScoped<ITenantDeviceService, TenantDeviceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IPositionService, PositionService>();
builder.Services.AddScoped<IApprovalLineService, ApprovalLineService>();
builder.Services.AddScoped<IBillingProvider, ManualBillingProvider>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IBillsCardsBankService, BillsCardsBankService>();
builder.Services.AddScoped<ITermsConsentService, TermsConsentService>();  // 헌법 #24 약관 4건 강제 동의
builder.Services.AddScoped<IItemSpecService, ItemSpecService>();  // 작지① 상품 규격 1:N 콤보박스 (사장님 작업지시 2026-05-31)
builder.Services.AddScoped<IFormTemplateService, FormTemplateService>();  // 작지②·③ 양식정보설정 (사장님 작업지시 2026-05-31)
builder.Services.AddScoped<ISyncTokenService, SyncTokenService>();  // 백오피스 Pull Sync 토큰 (사장님 결재 2026-06-01)
builder.Services.AddScoped<ISyncService, SyncService>();  // 백오피스 Pull 직원·기기 (헌법 #18·#22)
builder.Services.AddScoped<ICloudflareProvisioningService, CloudflareProvisioningService>();  // 프로비저닝 스켈레톤 (헌법 #29 사장님 결재 2026-06-01)
builder.Services.AddScoped<ITossWebhookService, TossWebhookService>();  // 토스페이먼츠 Webhook (사장님 결재 2026-06-01)
builder.Services.AddScoped<ITenantSnapshotService, TenantSnapshotService>();  // 백오피스 Pull 복사본 조회 (사장님 결재 2026-06-01)
builder.Services.AddScoped<IRefundService, RefundService>();  // 백오피스 환불 처리 (사장님 결재 2026-06-01)
builder.Services.AddScoped<IResellerApplicationService, ResellerApplicationService>();  // 대리점 신청 (사장님 결재 2026-06-01)
builder.Services.AddScoped<ILandingSignupService, LandingSignupService>();  // 랜딩 가입 (사장님 모두결재 2026-06-01, 잔여 #2)
// WS-20260601-13 본사 시리얼 발급 (HP-/HR- 4-eyes, 8명제 #2·#4, 백엔드 매니저)
builder.Services.AddSingleton<HitPan.API.Services.ISerialIssueService, HitPan.API.Services.SerialIssueService>();
// WS-20260601-14 이메일·SMS 2채널 인프라 (헌법 #16 정합, 백엔드 매니저 + 보안 매니저 2)
builder.Services.AddScoped<HitPan.API.Services.Notifications.IEmailSender, HitPan.API.Services.Notifications.EmailSenderService>();
builder.Services.AddScoped<HitPan.API.Services.Notifications.ISmsSender, HitPan.API.Services.Notifications.SmsSenderService>();
builder.Services.AddScoped<HitPan.API.Services.Notifications.INotificationDispatcher, HitPan.API.Services.Notifications.NotificationDispatcher>();
builder.Services.AddScoped<IPasswordEncryptor, PasswordEncryptorAdapter>();
builder.Services.AddScoped<IPdfRenderService, PdfRenderService>();
builder.Services.AddScoped<IEmailService, EmailService>();
// 작B v3.0 (2026-05-26): 전자세금계산서 방식 A 다이렉트 — 사장님 결재 5/25 22:00 + 23:00
// 헌법 #22 (본사 데이터 0) + #23 (5중 검증) + #25 (3대 원칙) + 보안 매니저 1·2 + Red Team 본질 진단
builder.Services.AddSingleton<HitPan.Application.Services.Security.ITpmKeyService,
    HitPan.Application.Services.Security.TpmKeyService>();
builder.Services.AddScoped<HitPan.Application.Services.Security.IDoubleEncryptionService,
    HitPan.Application.Services.Security.DoubleEncryptionService>();
builder.Services.AddSingleton<HitPan.Application.Services.Security.ICertStorageService,
    HitPan.Application.Services.Security.CertStorageService>();
// W2 본질 보강 (Red Team 1순위): 원격제어 감지
builder.Services.AddSingleton<HitPan.Application.Services.Security.IRemoteControlDetector,
    HitPan.Application.Services.Security.RemoteControlDetector>();
// W2 본질 보강 (보안 매니저 2 권고): 메모리 보호 + 워치독 22 시나리오
builder.Services.AddSingleton<HitPan.Application.Services.Security.ISecureMemoryService,
    HitPan.Application.Services.Security.SecureMemoryService>();
builder.Services.AddScoped<HitPan.Application.Services.Security.ICertIntegrityWatchdog,
    HitPan.Application.Services.Security.CertIntegrityWatchdog>();
builder.Services.AddScoped<HitPan.Application.Services.TaxInvoice.ITaxInvoiceXmlBuilder,
    HitPan.Application.Services.TaxInvoice.TaxInvoiceXmlBuilder>();
var hometaxOptions = builder.Configuration.GetSection("Hometax")
    .Get<HitPan.Application.Services.TaxInvoice.HometaxOptions>()
    ?? new HitPan.Application.Services.TaxInvoice.HometaxOptions();
builder.Services.AddSingleton(hometaxOptions);
builder.Services.AddHttpClient<HitPan.Application.Services.TaxInvoice.ITaxInvoiceProvider,
    HitPan.Application.Services.TaxInvoice.DirectHometaxProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(hometaxOptions.TimeoutSeconds);
});
builder.Services.AddScoped<ILeaveRequestService, LeaveRequestService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<ICollectionService, CollectionService>();
builder.Services.AddScoped<IMonthlyClosingService, MonthlyClosingService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IHrService, HrService>();
// AI 챗봇 (Phase A: FAQ/KB 매칭 + 대화 이력 축적)
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<ExcelImportService>();
// MdbMigrationService는 Windows 전용 (Jet OLEDB). 헌법 #19 warnings 0 준수: 호출 사이트만 pragma로 억제.
#pragma warning disable CA1416  // Windows 전용 — Linux 컨테이너 배포 시 호출 안 됨 (MigrationController가 [SupportedOSPlatform("windows")])
builder.Services.AddScoped<MdbMigrationService>();
// 2026-05-14: 마이그 백그라운드 잡 진행률 저장소 (524 회피용 폴링 패턴).
// CODE-01 봉합 (2026-05-14 18:50): IDbConnection Scoped 의존성 → store도 Scoped.
// jobId 진행 상태는 static ConcurrentDictionary로 모든 요청 공유 (MigrationJobStore 내부).
builder.Services.AddScoped<MigrationJobStore>();
#pragma warning restore CA1416
// 메시지큐 제거 (2026-05-13 사장님 지시) — bulk INSERT로 마이그 빠르게 종료, 큐 불필요.
builder.Services.AddScoped<IPartnerBalanceRepository, PartnerBalanceRepository>();
builder.Services.AddScoped<IEventPublisher, SyncEventPublisher>();
// 멱등 처리 — idempotency_keys 만료 정리 (DESIGN_PRINCIPLES §5.3 / 작업지시서 20260425작4)
builder.Services.AddHostedService<IdempotencyCleanupService>();
// 정합성 자동감지 — 재고 음수·monthly_summary 불일치 6h 주기 감지 → audit_trail 기록
builder.Services.AddHostedService<IntegrityCheckService>();
// 워치독 emergency → CS 자동 발신 (헌법 #28-F, appsettings.CsAutoDispatch.Enabled=true 시만)
builder.Services.AddHostedService<HitPan.API.Services.CsAutoDispatchService>();
// 본사 ERP ↔ 백오피스 단방향 Outbox (WS-20260601-20, 8명제 #3 + 헌법 #18·#22)
//   - 단방향 절대: 백오피스(클라우드) → 본사 ERP(로컬) Push 만. 역방향 절대 금지.
//   - 폴러는 appsettings Outbox:Enabled=true + BackofficeConnectionString 설정 시만 가동.
builder.Services.AddScoped<HitPan.API.Services.Messaging.IOutboxPublisher,
                          HitPan.API.Services.Messaging.OutboxPublisherService>();
builder.Services.AddHostedService<HitPan.API.BackgroundServices.OutboxPollerWorker>();
// 전자서명 (간편인증 Mock 4종 + 수동 3종) + 전자근로계약서
builder.Services.AddScoped<IESignatureService, ESignatureService>();
builder.Services.AddScoped<ILaborContractService, LaborContractService>();
builder.Services.AddSingleton<AccessTokenValidator>();
// 백오피스 (본사 관리자 + 대리점 파트너)
builder.Services.AddScoped<IBackofficeAuthService, BackofficeAuthService>();
builder.Services.AddScoped<IResellerService, ResellerService>();
// WS-20260601-17: 대리점 RLS 셀프 서비스 (8명제 #6·#7 — JWT reseller_serial 강제, 평문 0)
builder.Services.AddScoped<HitPan.API.Services.IResellerRlsService, HitPan.API.Services.ResellerRlsService>();
builder.Services.AddJwtAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SalesOnly", policy =>
        policy.RequireRole("system_admin", "sales_manager", "sales_user", "TenantAdmin", "tenant_admin"));
    options.AddPolicy("SalesManager", policy =>
        policy.RequireRole("system_admin", "sales_manager"));
    options.AddPolicy("PurchaseOnly", policy =>
        policy.RequireRole("system_admin", "purchase_manager", "TenantAdmin", "tenant_admin"));
    options.AddPolicy("AccountOnly", policy =>
        policy.RequireRole("system_admin", "account_manager", "TenantAdmin", "tenant_admin"));
    options.AddPolicy("HROnly", policy =>
        policy.RequireRole("system_admin", "hr_manager", "TenantAdmin", "tenant_admin"));
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("system_admin"));
    options.AddPolicy("PlatformOnly", policy =>
        policy.RequireClaim("account_type", "platform_admin"));
    options.AddPolicy("ResellerOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "reseller_admin") ||
            ctx.User.HasClaim("account_type", "platform_admin")));
    options.AddPolicy("TenantOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin") ||
            ctx.User.HasClaim("account_type", "tenant_user") ||
            ctx.User.HasClaim("account_type", "platform_admin")));
    // GET /api/tenants/me — Blazor TenantProfile (플랫폼·대리점·고객사)
    options.AddPolicy("TenantProfile", policy =>
        policy.RequireAssertion(ctx =>
        {
            var at = ctx.User.FindFirst("account_type")?.Value;
            return at is "platform_admin" or "reseller_admin" or "tenant_admin" or "tenant_user";
        }));
    options.AddPolicy("TenantAdminOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin") ||
            ctx.User.HasClaim("account_type", "platform_admin")));

    // ─────────────────────────────────────────────────────────────
    // WS-20260601-16: 백오피스/랜딩 4계층 권한 정책 (2026-06-01).
    // 8대 명제 백오피스/랜딩 마스터 설계 §JWT 4계층 박제.
    // 기존 PlatformOnly(account_type=platform_admin)와 정합 유지:
    //   - role 클레임이 있으면 role 우선, 없으면 account_type fallback.
    // 헌법 #1 (덮어쓰기 금지) 준수: 기존 정책 손대지 않고 추가만.
    // 헌법 #2 정합: reseller_serial 클레임은 JWT에서만 수신.
    // ─────────────────────────────────────────────────────────────

    // OwnerOnly: role=owner — 사장님 본인 (최상위)
    options.AddPolicy("OwnerOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "owner")));

    // PlatformManagerOrAbove: role=owner OR platform_manager
    options.AddPolicy("PlatformManagerOrAbove", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "owner") ||
            ctx.User.HasClaim("role", "platform_manager")));

    // PlatformOnlyV2: role=owner OR platform_manager OR platform_staff
    // 기존 PlatformOnly와 별도 정책으로 추가 (덮어쓰기 금지).
    // 신규 백오피스/랜딩 컨트롤러는 PlatformOnlyV2 사용.
    options.AddPolicy("PlatformOnlyV2", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "owner") ||
            ctx.User.HasClaim("role", "platform_manager") ||
            ctx.User.HasClaim("role", "platform_staff")));

    // ResellerSelfOnly: role=reseller + reseller_serial 클레임 보유 필수
    // 자기 시리얼 외 데이터 접근 금지 — RLS는 BackofficeRepositoryBase에서 강제.
    options.AddPolicy("ResellerSelfOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "reseller") &&
            !string.IsNullOrWhiteSpace(ctx.User.FindFirst("reseller_serial")?.Value)));
});
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();

// 정공법 CODE-01 (2026-05-14): 마이그 진행률 SignalR push.
// 봉합(2초 폴링 900회/30분) → 정공법(서버 push 0회 폴링) 전환.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IMigrationProgressService, MigrationProgressService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWasmDev", policy =>
    {
        // 베타 단계: hitpan.kr 서브도메인 + LAN 모두 허용 (Blazor wasm preflight 호환).
        // §#18 본사 미수신과는 무관 — 이건 고객사 본인 ERP의 Web↔API 통신.
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // 정공법 CODE-01 (2026-05-14): SignalR WebSocket/LongPolling은 자격증명 필요 → AllowCredentials.
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SystemSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
// Production이 아니면 Swagger 활성(터미널에서 ENV 미지정 시 Production이 되어 UI가 안 뜨는 경우 방지)
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Development·LAN에서는 http://IP:5257 만 쓰는 경우가 많아 리다이렉트 생략
if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

// 보안 헤더 (CSP · X-Frame · X-Content-Type-Options · Referrer-Policy)
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (!isDevelopment)
    {
        h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' https:; frame-ancestors 'none'";
        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<HealthIpWhitelistMiddleware>(); // RED-3: /health IP 화이트리스트
app.UseMiddleware<WatchdogBearerMiddleware>();    // 헌법 #22·#28: /watchdog/* Bearer 검증
app.UseCors("BlazorWasmDev");

// Blazor WASM 정적 파일 서빙 — 인증 전에 처리
var hasBlazor = File.Exists(Path.Combine(builder.Environment.WebRootPath ?? "", "index.html"));
if (hasBlazor)
{
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var file = ctx.File.Name;
            if (file == "appsettings.json" || file == "index.html" || file.EndsWith(".json"))
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers["Pragma"] = "no-cache";
                ctx.Context.Response.Headers["Expires"] = "0";
            }
        }
    });
}

app.UseMiddleware<AuditLogMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
// 작20260428이7 #4: RateLimit은 Authentication 이후로 이동.
// 인증된 user_id 기반 카운트 → 터널 환경에서 다수 사용자가 같은 IP로 인식되어도 오차단 안 발생.
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<SessionLimitMiddleware>();
app.UseMiddleware<TermsConsentMiddleware>();  // 헌법 #24: 첫 로그인 약관 4건 강제 동의 검증
// 멱등 처리: TenantMiddleware 이후 (tenantId 필요), MapControllers 이전 — [IdempotencyKey] 옵트인 액션만 영향 (DESIGN_PRINCIPLES §5.3 / 작업지시서 20260425작4)
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
// 정공법 CODE-01 (2026-05-14): 마이그 진행률 Hub.
app.MapHub<MigrationProgressHub>("/hubs/migration");


if (hasBlazor)
{
    app.MapFallbackToFile("index.html");
}
else if (!app.Environment.IsProduction())
{
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.Run();

// ── .env 파일 로드 헬퍼 ──
// 실행 디렉토리부터 상위로 올라가며 .env 파일을 찾아 로드한다.
// 시크릿(DB/JWT/AES)을 소스코드 외부로 분리하여 Git 노출 방지.
static void LoadDotEnv()
{
    var cur = new DirectoryInfo(AppContext.BaseDirectory);
    while (cur is not null)
    {
        var envPath = Path.Combine(cur.FullName, ".env");
        if (File.Exists(envPath))
        {
            try { DotNetEnv.Env.Load(envPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] .env load failed at {envPath}: {ex.Message} — falling back to OS env vars");
            }
            return;
        }
        cur = cur.Parent;
    }
}
