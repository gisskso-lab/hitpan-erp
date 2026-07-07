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

// seed-parent 오프라인 서브커맨드 (작업지시서 20260707작1 ②단계 P0-3, A안 — 사장님 결재 2026-07-07):
//   웹 호스트를 띄우지 않고 부모계정을 로컬 DB 에 생성하고 종료한다(브라우저·CORS·터널 우회).
//   설치마법사(.iss)가 HitPan.API.exe seed-parent <inputJsonPath> 로 호출. 종료 코드로 판정.
if (args.Length > 0 && string.Equals(args[0], HitPan.API.Services.SeedParentCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    var seedExit = await HitPan.API.Services.SeedParentCommand.RunAsync(builder, args);
    return seedExit;
}

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
// 고리4 P1 (사장님 결재 2026-06-30, 작4): DB 스키마 마이그(DB-*.sql) 적용 주체 등록.
//   ★ 수동 호출만 — 앱시작 자동 실행 배선 0건(IMigrationRunner.cs ① 범위, 헌법 #39 운영 보호).
//   의존(IMigrationDbConnectionFactory=InfrastructureExtensions.cs:46 Singleton · ILogger)은 이미 등록됨.
builder.Services.AddScoped<IMigrationRunner, MigrationRunner>();
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
builder.Services.AddScoped<IDataResetService, DataResetService>();
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
// 헌법 #35 (2026-06-04 사장님 결재) — 협력업체 신청은 HitPan.Backoffice.API로 이식 완료. ERP는 고객사 업무 전용.
// 헌법 #35 (2026-06-04 사장님 결재) — 랜딩 가입은 HitPan.Backoffice.API로 이식 완료. ERP는 고객사 업무 전용.
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
//   신규(2026-06-19): KB 매칭 실패 시 외부 도우미 직통 호출 + 정체성 System Prompt.
//   - 정체성 .md 는 앱 시작 1회 로드 캐싱 (Singleton). IChatbotSystemPrompt 경계로 ChatbotService 에 주입.
//   - 외부 모델 호출은 IHttpClientFactory 기반(고객 PC → Anthropic 직통, 본사 프록시 0 / 헌법 #18·#22).
//   - 모델·max_tokens 는 설정 가능(기본 claude-sonnet-4-6 / 1024). appsettings "Chatbot:Model","Chatbot:MaxTokens".
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HitPan.API.AI.ChatbotIdentityProvider>();
builder.Services.AddSingleton<HitPan.API.AI.IChatbotIdentityProvider>(
    sp => sp.GetRequiredService<HitPan.API.AI.ChatbotIdentityProvider>());
builder.Services.AddSingleton<HitPan.Application.Services.IChatbotSystemPrompt>(
    sp => sp.GetRequiredService<HitPan.API.AI.ChatbotIdentityProvider>());
builder.Services.AddSingleton<HitPan.Application.Services.IChatCompletionProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var model = cfg["Chatbot:Model"];
    var maxTokensRaw = cfg["Chatbot:MaxTokens"];
    int? maxTokens = int.TryParse(maxTokensRaw, out var mt) ? mt : null;
    return new HitPan.Application.Services.AnthropicChatProvider(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<HitPan.Application.Services.AnthropicChatProvider>>(),
        model,
        maxTokens);
});
// AI 직원 — 자연어 분석 명령을 실데이터 표+차트로 처리(읽기 전용, IReportService 재사용).
builder.Services.AddScoped<HitPan.Application.Services.IAiEmployeeAnalysisService,
    HitPan.Application.Services.AiEmployeeAnalysisService>();

// ── AI 직원 엔진 (Tool Use) — 사장님 "히트판의 FSD" 비전 (2026-06-20) ──
//   클로드가 명령 보고 히트판 도구를 스스로 골라 호출. Tool 클래스 1개 추가 → 여기 등록 → 끝.
builder.Services.AddSingleton<HitPan.API.AI.AiAgentSystemPromptProvider>();
builder.Services.AddSingleton<HitPan.Application.Services.Ai.IAiAgentSystemPrompt>(
    sp => sp.GetRequiredService<HitPan.API.AI.AiAgentSystemPromptProvider>());
builder.Services.AddScoped<HitPan.Application.Services.Ai.IHitpanToolRegistry,
    HitPan.Application.Services.Ai.HitpanToolRegistry>();
builder.Services.AddScoped<HitPan.Application.Services.Ai.IAiAgentService,
    HitPan.Application.Services.Ai.AiAgentService>();
//   Tool 카탈로그 — 새 도구는 이 줄들 아래에 AddScoped<IHitpanTool, XxxTool>() 한 줄씩 추가.
builder.Services.AddScoped<HitPan.Application.Services.Ai.IHitpanTool,
    HitPan.Application.Services.Ai.Tools.SalesProfitabilityTool>();
builder.Services.AddScoped<HitPan.Application.Services.Ai.IHitpanTool,
    HitPan.Application.Services.Ai.Tools.PartnerSearchTool>();
builder.Services.AddScoped<HitPan.Application.Services.Ai.IHitpanTool,
    HitPan.Application.Services.Ai.Tools.CreateDeliveryDraftTool>();

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
// 부모계정 온보딩 증표 병행 검증기 + 공유 프로비저너 (작업지시서 20260707작1 ②단계, 사장님 승인 2026-07-07)
//   - SerialProofVerifier: 2-part HMAC(이행기) / 3-part 공개키(ECDSA) 병행 검증. 공개키 EXE 내장.
//   - CompanyBootstrapProvisioner: create-parent 웹 API 와 seed-parent 오프라인 서브커맨드가 공유하는 DB 트랜잭션.
builder.Services.AddSingleton<HitPan.API.Services.SerialProofVerifier>();
builder.Services.AddScoped<HitPan.API.Services.CompanyBootstrapProvisioner>();
// 헌법 #35 객체 완전 분리 (사장님 결재 2026-06-04, 보안 격벽 완료 2026-06-18):
//   - 본사 백오피스·대리점 영역 컨트롤러·서비스는 HitPan.Backoffice.API로 이전
//   - ERP에서 IResellerService / IResellerRlsService DI 등록 제거
//   - BackofficeAuthService 옛 ERP 잔재 제거 완료 — 백오피스 인증은 HitPan.Backoffice.API 전담.
//     고객사 PC(ERP)에 본사·대리점 인증 코드가 존재하지 않게 하여 공격면 제거(헌법 #7·#22·#35).
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
    // PlatformOnly·ResellerOnly 정책 제거 (보안 격벽 2026-06-18): 본사·대리점 계층은 백오피스 전용.
    //   ERP API 컨트롤러 중 이 정책을 [Authorize(Policy)]로 소비하는 곳 0건 — 죽은 등록 제거.
    // 죽은 분기 청소 (2026-06-22, 헌법 #38 계정 계층 격벽 명문화): platform_admin/reseller_admin 은
    //   본사·대리점 계층으로 백오피스 전용 — 2026-06-18 격벽으로 ERP 토큰에 이 account_type 클레임이
    //   발급되지 않으므로(AuthService 는 tenant_admin/tenant_user 만 발급) 아래 OR 절은 도달 불가능한
    //   죽은 분기였다. 제거해도 부모(tenant_admin)/자식(tenant_user) 동작 무손상. (reseller_id 수신 캐시
    //   = 덩어리2/헌법 #37 과 무관 — 그건 영업 메타데이터로 보존, 여기는 계정 계층 잔재라 청소.)
    options.AddPolicy("TenantOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin") ||
            ctx.User.HasClaim("account_type", "tenant_user")));
    // GET /api/tenants/me — Blazor TenantProfile (부모/자식 고객사 계정)
    options.AddPolicy("TenantProfile", policy =>
        policy.RequireAssertion(ctx =>
        {
            var at = ctx.User.FindFirst("account_type")?.Value;
            return at is "tenant_admin" or "tenant_user";
        }));
    options.AddPolicy("TenantAdminOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin")));

    // 백오피스/랜딩 4계층 권한 정책(OwnerOnly·PlatformManagerOrAbove·PlatformOnlyV2·ResellerSelfOnly) 제거
    //   (보안 격벽 2026-06-18): 본사·대리점 계층 인가는 HitPan.Backoffice.API 전담(자체 동명 정책 보유).
    //   ERP API 컨트롤러 소비 0건 — 고객사 PC에 본사 권한 정책이 남지 않게 제거(헌법 #7·#35).
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
        // 봉합 2026-06-17 1.2.13 — CSP 3종 도메인 정합 (pretendard·다음우편·CF insights)
        h["Content-Security-Policy"] = "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://t1.daumcdn.net https://static.cloudflareinsights.com; " +
            "script-src-elem 'self' 'unsafe-inline' 'unsafe-eval' https://t1.daumcdn.net https://static.cloudflareinsights.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
            "style-src-elem 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
            "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net data:; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self' https: wss:; " +
            "frame-ancestors 'none'";
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

// seed-parent 서브커맨드가 있어 진입점이 int 를 반환하므로, 정상 웹 실행 경로도 명시적으로 0 을 반환한다.
//   (app.Run() 은 블로킹이라 여기 도달 시 정상 종료.)
return 0;

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
