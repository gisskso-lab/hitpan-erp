using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.API.Extensions;
using HitPan.API.HostedServices;
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
builder.Services.AddScoped<IPasswordEncryptor, PasswordEncryptorAdapter>();
builder.Services.AddScoped<IPdfRenderService, PdfRenderService>();
builder.Services.AddScoped<IEmailService, EmailService>();
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
#pragma warning restore CA1416
builder.Services.AddScoped<IPartnerBalanceRepository, PartnerBalanceRepository>();
builder.Services.AddScoped<IEventPublisher, SyncEventPublisher>();
// 멱등 처리 — idempotency_keys 만료 정리 (DESIGN_PRINCIPLES §5.3 / 작업지시서 20260425작4)
builder.Services.AddHostedService<IdempotencyCleanupService>();
// 정합성 자동감지 — 재고 음수·monthly_summary 불일치 6h 주기 감지 → audit_trail 기록
builder.Services.AddHostedService<IntegrityCheckService>();
// 전자서명 (간편인증 Mock 4종 + 수동 3종) + 전자근로계약서
builder.Services.AddScoped<IESignatureService, ESignatureService>();
builder.Services.AddScoped<ILaborContractService, LaborContractService>();
builder.Services.AddSingleton<AccessTokenValidator>();
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
});
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWasmDev", policy =>
    {
        // 베타 단계: hitpan.kr 서브도메인 + LAN 모두 허용 (Blazor wasm preflight 호환).
        // §#18 본사 미수신과는 무관 — 이건 고객사 본인 ERP의 Web↔API 통신.
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod();
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
app.UseCors("BlazorWasmDev");

// Blazor WASM 정적 파일 서빙 — 인증 전에 처리
var hasBlazor = File.Exists(Path.Combine(builder.Environment.WebRootPath ?? "", "index.html"));
if (hasBlazor)
{
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
}

app.UseMiddleware<AuditLogMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
// 작20260428이7 #4: RateLimit은 Authentication 이후로 이동.
// 인증된 user_id 기반 카운트 → 터널 환경에서 다수 사용자가 같은 IP로 인식되어도 오차단 안 발생.
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<SessionLimitMiddleware>();
// 멱등 처리: TenantMiddleware 이후 (tenantId 필요), MapControllers 이전 — [IdempotencyKey] 옵트인 액션만 영향 (DESIGN_PRINCIPLES §5.3 / 작업지시서 20260425작4)
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();

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
