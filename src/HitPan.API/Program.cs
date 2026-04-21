using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.API.Extensions;
using HitPan.API.Middleware;
using HitPan.API.Services;
using HitPan.Infrastructure.Events;
using HitPan.Infrastructure.Extensions;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Persistence.Seed;
using HitPan.Infrastructure.Security;
using HitPan.API.Security;
using QuestPDF.Infrastructure;

using Microsoft.AspNetCore.Components.WebAssembly.Server;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
QuestPDF.Settings.License = LicenseType.Community;

// ── .env 파일 로드 (시크릿 분리) ──
// 프로젝트 루트 → 상위 탐색. 파일 없으면 무시 (프로덕션은 OS 환경변수 사용).
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<DeliveryBatchService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<IBomService, BomService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITenantCertificateService, TenantCertificateService>();
builder.Services.AddScoped<ITenantDeviceService, TenantDeviceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
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
builder.Services.AddScoped<MdbMigrationService>();
builder.Services.AddScoped<IPartnerBalanceRepository, PartnerBalanceRepository>();
builder.Services.AddScoped<IEventPublisher, SyncEventPublisher>();
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
        policy.RequireRole("system_admin", "purchase_manager"));
    options.AddPolicy("AccountOnly", policy =>
        policy.RequireRole("system_admin", "account_manager"));
    options.AddPolicy("HROnly", policy =>
        policy.RequireRole("system_admin", "hr_manager"));
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
        if (isDevelopment)
        {
            // LAN에서 PC2 브라우저 등 임의 Origin → API 호출 허용(Development 전용)
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            // 환경변수 ALLOWED_ORIGINS로 허용 도메인 추가 가능 (콤마 구분)
            var origins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                ?? Array.Empty<string>();
            var defaultOrigins = new[] { "http://localhost:5234", "https://localhost:7100" };
            policy.WithOrigins(defaultOrigins.Concat(origins).ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
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

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("BlazorWasmDev");

// Blazor WASM 정적 파일 서빙 — 인증 전에 처리
var hasBlazor = File.Exists(Path.Combine(builder.Environment.WebRootPath ?? "", "index.html"));
if (hasBlazor)
{
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
}

app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<AuditLogMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<SessionLimitMiddleware>();

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
            catch { /* .env 읽기 실패해도 OS 환경변수 fallback */ }
            return;
        }
        cur = cur.Parent;
    }
}
