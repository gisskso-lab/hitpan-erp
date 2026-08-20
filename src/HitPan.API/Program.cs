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
//   의존(IMigrationDbConnectionFactory=InfrastructureExtensions.cs:46 Singleton · ILogger)은 이미 등록됨.
//   ★ 2026-08-09 (사장님 결재 "승인") — 고리5: 앱 시작 자동 적용을 배선했다(아래 var app = builder.Build() 직후).
//     종전 "수동 호출만" 상태였고, 그래서 새 마이그가 포함된 릴리스는 워치독이 자동교체를 스스로 차단했다
//     (UpdateOrchestrator.cs:512 — "적용 주체가 없어 통과시키면 500"). 즉 DB가 바뀌면 자동 업데이트가 막혔다.
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
// 작(2026-08-13) 단계4 토대: 부서 마스터 CRUD. 종전엔 조회만 있어 부서를 만들 방법이 없었다.
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
// 작(2026-08-13) 단계5: 연차 엔진. 법정값은 labor_policy_settings 에서만 읽는다.
builder.Services.AddScoped<IAnnualLeaveService, AnnualLeaveService>();
// 작(2026-08-13) 단계6: 휴직. 휴가(leave_requests)와 나눈 이유는 AbsenceDtos 주석 참고
// (일수 칸이 99.9일까지라 육아휴직이 안 들어가고, 승인 시 연차 잔여가 깎인다).
builder.Services.AddScoped<IAbsenceService, AbsenceService>();
// 작(2026-08-13) 단계8: 급여·퇴직금. 🔴 계산하지 않는다 — 금액을 사람이 직접 넣는다
// (사장님: "급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함").
// 보호는 권한 계층(menu_code='PAYROLL')이 한다 — 컬럼 암호화는 내부자 열람을 못 막는다.
builder.Services.AddScoped<IPayrollService, PayrollService>();
// 작(2026-08-13) 단계9: 사내 메신저. 🔴 문서를 만들거나 결재하지 않는다 — 연결만 한다
// (사장님: "연결까지만 해도 충분함" / "있는 기능 연결해서 사용하는게 훨씬 효율적임").
// 열람은 방 참여 여부로 판정한다 — 부모계정도 남의 1:1 은 못 본다("본인 대화만 열람").
builder.Services.AddScoped<IChatService, ChatService>();
// 파일은 디스크에, DB 에는 경로만. DB 에 통째로 넣으면 백업·복구·업데이트가 다 느려진다
// (사장님: "히트판 ERP 데이터양이 많으면 과부화가 올 수 있어. 파일전송은 최소한으로").
builder.Services.AddSingleton<IChatFileStore, ChatFileStore>();
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
// 작(2026-08-13) 그룹웨어 단계3: 업무보고서 4종(일일·주간·월간·경위서).
// ⚠️ IReportService(현황 리포트)와 다른 것이다 — 이름이 비슷해 처음에 그 파일을 덮어썼었다.
builder.Services.AddScoped<IWorkReportService, WorkReportService>();
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
//   - 모델·max_tokens 는 설정 가능(기본값은 각 어댑터의 DefaultModel / 1024). appsettings "Chatbot:Model","Chatbot:MaxTokens".
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HitPan.API.AI.ChatbotIdentityProvider>();
builder.Services.AddSingleton<HitPan.API.AI.IChatbotIdentityProvider>(
    sp => sp.GetRequiredService<HitPan.API.AI.ChatbotIdentityProvider>());
builder.Services.AddSingleton<HitPan.Application.Services.IChatbotSystemPrompt>(
    sp => sp.GetRequiredService<HitPan.API.AI.ChatbotIdentityProvider>());

// ── AI 공급자 3사 (2026-08-12, 작업지시서 20260812작1 · 사장님 결재) ──
//   사장님 오더: "기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
//   화면 표기 결재: 클로드AI / 챗GPT / 제미나이
//
//   🔴 무회귀: 아래 IChatCompletionProvider 등록은 종전 그대로 클로드AI 를 가리킨다.
//      공급자를 고르는 곳은 IAiProviderFactory 이며, 모르는 값이면 클로드AI 로 떨어진다.
//      = 마이그레이션만 적용되고 고객이 아무것도 안 바꿨으면 종전과 완전히 같게 동작한다.
//   각 어댑터는 상태가 없다(HttpClientFactory 사용) → Singleton 안전.
builder.Services.AddSingleton<HitPan.Application.Services.AnthropicChatProvider>(sp =>
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
builder.Services.AddSingleton<HitPan.Application.Services.OpenAiChatProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var maxTokensRaw = cfg["Chatbot:MaxTokens"];
    int? maxTokens = int.TryParse(maxTokensRaw, out var mt) ? mt : null;
    return new HitPan.Application.Services.OpenAiChatProvider(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<HitPan.Application.Services.OpenAiChatProvider>>(),
        cfg["Chatbot:OpenAiModel"],
        maxTokens);
});
builder.Services.AddSingleton<HitPan.Application.Services.GeminiChatProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var maxTokensRaw = cfg["Chatbot:MaxTokens"];
    int? maxTokens = int.TryParse(maxTokensRaw, out var mt) ? mt : null;
    return new HitPan.Application.Services.GeminiChatProvider(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<HitPan.Application.Services.GeminiChatProvider>>(),
        cfg["Chatbot:GeminiModel"],
        maxTokens);
});
// 종전 경로 보존(헌법 #1) — 이 인터페이스를 주입받는 기존 코드는 그대로 클로드AI 를 받는다.
builder.Services.AddSingleton<HitPan.Application.Services.IChatCompletionProvider>(
    sp => sp.GetRequiredService<HitPan.Application.Services.AnthropicChatProvider>());
// 공급자 선택 팩토리 — 테넌트의 ai_provider 값으로 어댑터를 고른다.
builder.Services.AddSingleton<HitPan.Application.Services.Ai.IAiProviderFactory,
    HitPan.Application.Services.Ai.AiProviderFactory>();
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
// 메인PC 자동등록 — 히트판 본체·DB 를 가진 이 PC 를 등록 기기 목록에 넣는다 (20260810작3)
//   기기 슬롯을 계정이 아니라 기기로 세기로 한 결정에 맞춰, 설치된 그 PC 도 목록에 잡히게 한다.
//   ⭐ 사람이 로그인하지 않아도 등록된다 — 메인PC 는 24시간 켜두는 무인 PC 일 수 있고,
//     부모계정이 그 PC 에서 쓰인다는 보장도 없다(계정 축과 기기 축은 별개).
//   고객지원이 "그 PC 가 메인PC 인가" 를 화면으로 확인할 수 있어야 응대가 갈린다.
builder.Services.AddHostedService<MainPcRegistrationService>();
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
    // W1-4 (작업지시서 20260707작2): 부모계정(employees.role='tenant_admin') 추가 — W1-3 로 로그인 시
    //   employee_id·role claim 이 실제로 실리기 시작하면, 이 정책만 tenant_admin 이 빠져 있어 부모계정이
    //   거래명세서 수정·취소·반품에서 403 즉발(헌법 #20 판매흐름 끊김). 다른 정책들과 동일 어휘로 정합.
    options.AddPolicy("SalesManager", policy =>
        policy.RequireRole("system_admin", "sales_manager", "TenantAdmin", "tenant_admin"));
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

// 작(2026-08-13) 그룹웨어 단계2: 앱 내 결재 알림.
// 김삼성 상무 조언 1순위 — "'결재가 올라왔습니다' 알림부터. 그러면 직원이 메신저를 켤 이유가 생긴다."
// 배관(SignalR)은 위 마이그 진행률용으로 이미 검증돼 있어 패턴만 복제한다.
builder.Services.AddSingleton<INotificationService, SignalRNotificationService>();

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

// ═══════════════════════════════════════════════════════════════════════════
// 고리5 — DB 스키마 마이그 자동 적용 (사장님 결재 2026-08-09 "승인")
// ═══════════════════════════════════════════════════════════════════════════
//
// 왜 필요했나
//   업데이트는 '프로그램 파일 교체'만 한다. DB 컬럼을 추가하는 주체가 없었다.
//   그래서 새 컬럼을 읽는 코드가 배포되면 고객 PC 는 "그런 칸 없다" 로 500 이 난다.
//   워치독은 이걸 알고 새 마이그가 있는 릴리스를 스스로 차단해 왔다
//   (UpdateOrchestrator.cs:512 "워치독은 DB 마이그를 적용하지 않으므로 … 500 P0").
//   ⇒ DB 가 바뀌면 자동 업데이트가 막히고 재설치로만 갈 수 있었다.
//   ⇒ 여기서 적용 주체를 세워, DB 가 백 번 바뀌어도 고객은 팝업 [예] 한 번으로 끝난다.
//
// 안전 설계 (사장님 승인 조건 3가지 — 전부 MigrationRunner 가 이미 구현하고 있다)
//   ① 멱등 — 이미 success=1 인 마이그는 건너뛴다. 몇 번을 켜도 안전(MigrationRunner:104).
//   ② 실패 시 즉시 중단 — 실패한 마이그를 success=0 으로 기록하고 다음 것을 건드리지 않는다
//      (MigrationRunner:143-162). MariaDB DDL 은 암묵 커밋이라 롤백이 불가하므로,
//      '더 망가뜨리지 않는 것' 이 최선의 안전이다.
//   ③ 업데이트 전 자동 백업 — 워치독이 이미 수행한다(사장님 결재 업데이트 흐름).
//
// 🔴 실패해도 앱을 죽이지 않는 이유
//   여기서 Environment.Exit 을 하면 ERP 가 아예 안 뜬다. 고객은 화면조차 못 보고,
//   원격 진단도 불가능해진다(고객 PC 로컬 구조 — 헌법 #30, 본사 의존 0).
//   대신 기동은 시키되 무엇이 실패했는지 로그에 명확히 남긴다. 실패한 화면만 500 이 나고
//   나머지 업무는 계속 돌아간다 — 전면 중단보다 피해가 작다.
//   ⚠️ 단, 이 판단은 '부분 실패' 전제다. 마이그가 데이터를 변형하는 종류로 넓어지면
//     그때는 기동 차단이 맞을 수 있다. 그 시점에 재결재를 받는다.
//
// 헌법: #9 DB 는 미리 다 / #13 조회 전 존재 보장 / #15 빈 catch 금지 /
//       #26 마이그 목표(10분)-Timeout(24h) 분리 / #30 본사 의존 0 / #39 사람 손 ALTER 대체
{
    using var migScope = app.Services.CreateScope();
    var migLogger = migScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("HitPan.Startup.Migration");
    try
    {
        var runner = migScope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        var appVersion = typeof(Program).Assembly.GetName().Version?.ToString();

        var result = await runner.ApplyPendingAsync(appVersion).ConfigureAwait(false);

        if (result.Success)
        {
            if (result.AppliedMigrationIds.Count > 0)
            {
                migLogger.LogWarning(
                    "[Startup/Migration] ✅ DB 스키마 갱신 완료 — 신규 적용 {Applied}건({List}), 건너뜀 {Skipped}건.",
                    result.AppliedMigrationIds.Count,
                    string.Join(", ", result.AppliedMigrationIds),
                    result.SkippedCount);
            }
            else
            {
                migLogger.LogInformation(
                    "[Startup/Migration] DB 스키마 최신 — 적용할 것 없음(건너뜀 {Skipped}건).",
                    result.SkippedCount);
            }
        }
        else
        {
            // 헌법 #15 — 삼키지 않는다. 무엇이 어디서 실패했는지 남긴다.
            migLogger.LogError(
                "[Startup/Migration] 🛑 DB 스키마 갱신 실패 — 실패 마이그: {Failed}. 사유: {Reason}. " +
                "이 지점에서 중단했고 다음 마이그는 진행하지 않았습니다. " +
                "이미 적용된 {Applied}건은 유효합니다. " +
                "새 컬럼을 쓰는 화면은 오류가 날 수 있으나 나머지 업무는 계속 사용할 수 있습니다.",
                result.FailedMigrationId ?? "(미상)",
                result.FailureMessage ?? "(사유 미상)",
                result.AppliedMigrationIds.Count);
        }
    }
    catch (Exception ex)
    {
        // 러너 자체가 터져도 기동은 시킨다(위 '앱을 죽이지 않는 이유' 참조).
        migLogger.LogError(ex,
            "[Startup/Migration] 🛑 DB 스키마 갱신 중 예기치 못한 오류 — 기동은 계속합니다. " +
            "새 컬럼을 쓰는 화면에서 오류가 날 수 있습니다.");
    }
}

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
            // 🔴 [2026-08-20 3차 봉합 — `t1.kakaocdn.net`] 사장님 1.2.92 실측:
            //   *"이 콘텐츠는 차단되었습니다. 문제를 해결하려면 사이트 소유자에게 문의하세요."*
            //
            //   [무엇을 놓쳤나] 1·2차는 **틀(frame-src)만** 봤다. 그런데 우편번호 창은
            //     **`about:blank` iframe 에 `document.write` 로 그려진다**(로더 실측).
            //     ⇒ 그 문서는 **우리 origin 을 물려받아 우리 CSP 를 그대로 따른다.**
            //     ⇒ 틀을 열어 줘도 **그 안이 불러오는 스크립트·CSS 가 우리 CSP 에 막히면** 창이 빈 채로 뜬다.
            //   [실측] `https://postcode.map.kakao.com/search` 문서가 부르는 것:
            //     `t1.kakaocdn.net/postcode/cssjs/…/service.v2.min.js` · `…min.css` · jquery · tiara
            //     ⇒ **`t1.kakaocdn.net` 이 script-src·style-src 에 없어 전부 차단**됐다.
            //   🔴 [교훈 — 3번 만에 잡았다] 바깥 위젯을 붙일 때는 **틀 + 그 안의 자원**을 함께 본다.
            //     `frame-src` 만 열고 끝내면 이 자리에 또 온다. 확인은 iframe 문서를 직접 받아
            //     (`curl … /search`) 도메인을 `grep` 하는 것으로 끝난다.
            //   ⚠️ `t1.daumcdn.net`(로더)과 `t1.kakaocdn.net`(창 내부 자원)은 **다른 도메인**이다. 둘 다 필요하다.
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://t1.daumcdn.net https://t1.kakaocdn.net https://static.cloudflareinsights.com; " +
            "script-src-elem 'self' 'unsafe-inline' 'unsafe-eval' https://t1.daumcdn.net https://t1.kakaocdn.net https://static.cloudflareinsights.com; " +
            "style-src 'self' 'unsafe-inline' https://t1.kakaocdn.net https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
            "style-src-elem 'self' 'unsafe-inline' https://t1.kakaocdn.net https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
            "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net data:; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self' https: wss:; " +
            // 봉합 2026-08-20 20260820작4 (설계1) — 우편번호 찾기 창이 안 뜨던 것.
            //
            //   [무엇이 났나] 6/17(1.2.13) CSP 봉합이 **script-src 3종만 넣고 frame-src 를 빠뜨렸다.**
            //     카카오 우편번호는 **iframe 으로 뜬다.** frame-src 가 없으면 `default-src 'self'` 가
            //     상속되어 바깥 도메인 틀이 통째로 차단된다.
            //     ⇒ 스크립트는 정상으로 받아지고 버튼도 눌리는데 **창만 안 뜬다.**
            //       (`index.html:105` 로딩 · `openDaumPostcode` interop · 버튼 · 콜백 4층 전부 멀쩡했다)
            //     ⚠️ 업체등록(`PartnerDetail.razor:65`)과 사업장정보(`UserInfoPage.razor:167`)가
            //       **함께 죽어 있었다.** 사장님은 업체등록에서 발견하셨다.
            //
            //   🔴 [왜 개발팀이 못 봤나 — 이 자리의 교훈] 이 블록은 `if (!isDevelopment)` 안이다.
            //     ⇒ 개발PC 에서는 CSP 자체가 안 붙어 **우편번호가 항상 열린다.**
            //       터널·운영에서만 끊긴다. *"개발PC 에서 됩니다"* 가 이 건에서는 **구조적으로 무의미**하다.
            //     🔴 이 줄을 검증할 때는 반드시 **Production(CSP 켜진 상태)** 에서 연다.
            //
            //   🔴 [2026-08-20 2차 봉합 — 1차가 틀린 도메인을 적었다] 사장님 1.2.91 실측: *"우편번호 창 안뜸"*.
            //     [무엇이 틀렸나] 1차에 iframe 출처를 `postcode.map.daum.net` 으로 적었다. **추측이었고 틀렸다.**
            //       실제 로더(`postcode.v2.js`)를 받아 열어 보니 창을 여는 주소가
            //       **`https://postcode.map.kakao.com/guide`** 였다(같은 파일에 `/search` 도 있다).
            //       ⇒ CSP 는 나갔는데 **허용한 도메인이 실제와 달라** 창이 그대로 막혔다.
            //     🔴 [교훈] 바깥 위젯의 도메인은 **문서·기억이 아니라 그 위젯 파일에서 확인**한다.
            //       `curl` 로 받아 `grep` 하면 5초다. 추측하면 배포를 한 번 더 태운다(실제로 그랬다).
            //     ⚠️ `daum.net` 을 지우지 않고 남겨 둔다 — 카카오가 옛 도메인으로도 서비스할 수 있고,
            //       한 줄 더 있다고 손해가 없다. 반대로 지웠다가 옛 경로가 살아 있으면 또 막힌다.
            //   ⚠️ 넓게 열지 마라 — `frame-src https:` 같은 전체 허용은 **보안 후퇴**다.
            "frame-src 'self' https://t1.daumcdn.net https://postcode.map.kakao.com https://postcode.map.daum.net; " +
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

// 기기 인증 검사 (20260811작3 (A)) — 인증 번호가 없으면 업무 기능만 막는다.
//   로그인·기기 인증 길은 막지 않는다(막으면 번호를 넣으러 갈 수 없다).
//   DeviceApproval:Enabled 가 false 면 통째로 건너뛴다.
app.UseMiddleware<DeviceAuthMiddleware>();
// 멱등 처리: TenantMiddleware 이후 (tenantId 필요), MapControllers 이전 — [IdempotencyKey] 옵트인 액션만 영향 (DESIGN_PRINCIPLES §5.3 / 작업지시서 20260425작4)
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
// 정공법 CODE-01 (2026-05-14): 마이그 진행률 Hub.
app.MapHub<MigrationProgressHub>("/hubs/migration");
// 작(2026-08-13) 그룹웨어 단계2: 앱 내 알림 Hub.
// 🔴 이 줄이 빠지면 서비스·허브를 다 만들어도 연결 자체가 안 된다.
app.MapHub<NotificationHub>("/hubs/notify");


if (hasBlazor)
{
    app.MapFallbackToFile("index.html");
}
else if (!app.Environment.IsProduction())
{
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

// ══════════════════════════════════════════════════════════════════════════════════
//  W4-3-H — 구버전 워치독 자동 교체 인계 (20260806작6, 사장님 오더 "업데이트가 기본이야")
//
//  구버전(1.2.52 이하) 워치독은 자기 자신을 교체하지 못한다. 신버전 워치독을 {app}\watchdog.new
//  까지 받아놓고도 성공 직후 지워버린다(UpdateOrchestrator.cs:882). 그래서 워치독 코드 봉합은
//  자동업데이트로 **영원히 고객에게 도달하지 못한다** — 두 달간 버전만 오르던 무한루프의 원인.
//
//  업데이트는 api 폴더를 통째로 교체한 뒤 SYSTEM 권한으로 이 프로세스를 재기동한다. 그 통로를 타고
//  새 API 가 워치독 교체를 대신 성사시킨다. 고객 조작 0 — 재설치도 수동 조작도 없다.
//
//  ★ app.Run() **앞**에 두는 이유: Run 은 블로킹이라 뒤에 두면 종료 시점까지 실행되지 않는다.
//  ★ 이 호출은 즉시 반환한다(내부에서 백그라운드로 던진다) — ERP 기동을 1ms 도 늦추지 않는다.
//  ★ 어떤 실패도 ERP 기동을 막지 않는다(헌법 #20). 실패 시 구버전 워치독이 그대로 유지될 뿐이다.
if (OperatingSystem.IsWindows())
{
    HitPan.API.Startup.WatchdogHandoffBootstrap.ScheduleIfPending(
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WatchdogHandoff"));
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
