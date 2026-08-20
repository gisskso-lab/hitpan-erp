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
    // 봉합 (2026-06-25, 근본 — Playwright 실측이 잡은 외부 로그인 CORS P0):
    //   종전 폴백은 "http://localhost:5257" 절대경로였다. 그러면 외부 고객 브라우저가 자기 PC 의
    //   localhost:5257 을 부르다 CORS(loopback 차단)로 로그인 불가 — appsettings.json 의 ApiBaseUrl 이
    //   비거나 잘못 출하되면(17·18차 반복 계통) 매번 재발하는 단일 실패점이었다.
    //   근본 해결: 폴백을 "현재 페이지 출처"(HostEnvironment.BaseAddress)로 둔다. 그러면 브라우저는
    //   같은 출처(예: https://{회사}.hitpan.kr/api/...)로 요청 → web-server.ps1 이 /api/* 를 슬롯
    //   API 포트로 프록시(같은 출처라 CORS 자체가 없음). appsettings 값·환경 무관하게 항상 동작한다.
    //   명시값(api-demo 등)이 있으면 위에서 채택되므로 기존 정상 배포는 무영향.
    apiBase = builder.HostEnvironment.BaseAddress;
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
// 단가 참고값 말풍선 (20260820작4 · 설계2 C안) — 6화면(발주·매입·반품·견적·수주·판매) 공용.
//   한 줄 캐시를 들고 있어 Scoped 다. Singleton 으로 바꾸면 업체를 바꿔도 앞 값이 남는다.
builder.Services.AddScoped<PriceHintService>();
builder.Services.AddScoped<ItemMasterService>();
builder.Services.AddScoped<BomService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<PositionService>();
// 작(2026-08-13) 단계4 토대: 부서 마스터. 종전엔 조회만 있어 부서를 만들 수 없었다.
builder.Services.AddScoped<DepartmentService>();
// 작(2026-08-13) 단계5: 연차 엔진(반자동 3단 — 제안→수정→확정).
builder.Services.AddScoped<AnnualLeaveService>();
// 작(2026-08-13) 단계6: 휴직(육아·출산·병가 등 장기 부재). 휴가와 다른 표를 쓴다 —
// 휴가 표의 일수 칸이 99.9일까지라 육아휴직이 안 들어가고, 승인되면 연차 잔여가 깎인다.
builder.Services.AddScoped<AbsenceService>();
// 작(2026-08-13) 단계8: 급여·퇴직금. 금액을 사람이 직접 넣는다(계산하지 않는다).
builder.Services.AddScoped<PayrollService>();
// 작(2026-08-13) 단계9: 사내 메신저. 1:1·부서·단체 3종 + 문서 연결 + 읽음.
// 🔴 문서를 만들거나 결재하지 않는다 — 연결만 한다(사장님: "연결까지만 해도 충분함").
builder.Services.AddScoped<ChatService>();
// 작(2026-08-13) 그룹웨어 단계3: 업무보고서 4종(일일·주간·월간·경위서).
builder.Services.AddScoped<WorkReportService>();
// 작(2026-08-13) 그룹웨어 단계2: 앱 내 결재 알림 수신기.
// 🔴 화면 하나가 아니라 앱 전체에 떠야 하므로 레이아웃이 한 번만 연결한다.
//    (WASM 에서 Scoped 는 앱 수명과 같다 — 주변 등록과 같은 수명을 쓴다.)
builder.Services.AddScoped<NotificationClient>();
builder.Services.AddScoped<ApprovalLineService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<DataResetService>();
builder.Services.AddScoped<LogService>();
builder.Services.AddScoped<BillsCardsBankService>();
builder.Services.AddScoped<EmailClientService>();
builder.Services.AddScoped<FaxClientService>();   // 20260821작1 W3
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
// BackofficeService DI 제거 (보안 격벽 2026-06-18): 백오피스 인증·관리는 HitPan.Backoffice.API 전담.
//   ERP 프론트에 본사/대리점 호출 코드가 남지 않게 함(헌법 #35).
// 작20260601 (사장님 결재): 랜딩 v2 정적 콘텐츠 저장 서비스.
builder.Services.AddScoped<LandingContentService>();
builder.Services.AddTransient<HitPanApiAuthHandler>();
builder.Services.AddScoped<TenantProfileService>();
// 자료 화면(원장·현황·통계·재고·미수) 공용 내보내기 (사장님 지시 2026-08-12).
//   화면마다 만들면 34곳에 34가지가 생긴다 — 표만 넘기면 나머지는 이 서비스가 한다.
builder.Services.AddScoped<GridExportService>();
builder.Services.AddScoped(sp =>
{
    var handler = new HitPanApiAuthHandler(
        sp.GetRequiredService<HitPanProtectedLocalStorage>(),
        sp.GetRequiredService<MudBlazor.ISnackbar>(),
        sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
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
