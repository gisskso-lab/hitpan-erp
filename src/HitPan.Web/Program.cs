using HitPan.Web;
using HitPan.Web.Providers;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = Environment.GetEnvironmentVariable("HitPan__ApiBaseUrl");
}

if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl");
}

if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = "http://localhost:5257";
}

var apiUri = new Uri($"{apiBase.TrimEnd('/')}/");

builder.Services.AddMudServices();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<HitPanProtectedLocalStorage>();
builder.Services.AddScoped<IAuthTokenRefresher, AuthTokenRefresher>();
builder.Services.AddScoped<HitPanAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<HitPanAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<WorkTabService>();
builder.Services.AddScoped<DeliveryService>();
builder.Services.AddScoped<QuotationService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<PartnerMasterService>();
builder.Services.AddScoped<ItemMasterService>();
builder.Services.AddScoped<BomService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<PositionService>();
builder.Services.AddScoped<ApprovalLineService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<LogService>();
builder.Services.AddScoped<BillsCardsBankService>();
builder.Services.AddScoped<EmailClientService>();
// 작B v3.0 (2026-05-26): 전자세금계산서 인증서 등록 (방식 A 다이렉트)
builder.Services.AddScoped<TaxInvoiceCertClientService>();
builder.Services.AddScoped<LeaveRequestService>();
// 작20260526 (사장님 결재): 통합 캘린더 + 카드 모달 가도.
builder.Services.AddScoped<UnifiedCalendarService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<SpecialPriceService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<TaxInvoiceApiService>();
builder.Services.AddScoped<CollectionPaymentService>();
builder.Services.AddScoped<MonthlyClosingService>();
builder.Services.AddScoped<FinanceClientService>();
builder.Services.AddScoped<HrClientService>();
builder.Services.AddScoped<ESignService>();
builder.Services.AddScoped<LaborContractService>();
builder.Services.AddScoped<ChatbotService>();
builder.Services.AddScoped<BackofficeService>();
// 작20260601 (사장님 결재): 랜딩 v2 정적 콘텐츠 저장 서비스.
builder.Services.AddScoped<LandingContentService>();
builder.Services.AddTransient<HitPanApiAuthHandler>();
builder.Services.AddScoped<TenantProfileService>();
builder.Services.AddScoped(sp =>
{
    var handler = new HitPanApiAuthHandler(
        sp.GetRequiredService<HitPanProtectedLocalStorage>(),
        sp.GetRequiredService<MudBlazor.ISnackbar>(),
        sp.GetRequiredService<ILogger<HitPanApiAuthHandler>>())
    {
        InnerHandler = new HttpClientHandler()
    };
    // 핫픽스 2026-05-13: 거래처별 네트워크·데이터 크기·환경 차이 고려.
    // MDB 마이그 1년치 100만+건 처리 + 느린 회선 대응 — 기본 100초 → 10분.
    return new HttpClient(handler) { BaseAddress = apiUri, Timeout = TimeSpan.FromMinutes(10) };
});

// 헌법 #35 (사장님 결재 2026-06-04) — 백오피스 API 호출용 HttpClient (라이선스 검증 + 부트스트랩).
// /setup/license에서 사용. 인증 불필요(AllowAnonymous), 익명 키 BackofficeApiHttpClient 명시.
var backofficeApiBase = builder.Configuration["BackofficeApiBaseUrl"]
    ?? Environment.GetEnvironmentVariable("BackofficeApi__BaseUrl")
    ?? "http://localhost:5258/";
builder.Services.AddKeyedScoped<HttpClient>("backoffice", (sp, key) =>
    new HttpClient { BaseAddress = new Uri(backofficeApiBase.TrimEnd('/') + "/") });

// IHttpClientFactory.CreateClient("BackofficeApi") 호출용 (브라운킴 PM 2026-06-08, 사장님 결재)
// 시리얼 인증 등 백오피스 API 호출에 사용.
builder.Services.AddHttpClient("BackofficeApi", c =>
{
    c.BaseAddress = new Uri(backofficeApiBase.TrimEnd('/') + "/");
});

await builder.Build().RunAsync();
