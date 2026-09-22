using System.Security.Claims;
using HitPan.Infrastructure.Security;

namespace HitPan.API.Middleware;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentTenant currentTenant)
    {
        var path = context.Request.Path;
        // API 경로가 아니면 통과 (정적 파일, Blazor WASM 등)
        if (!path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (path.StartsWithSegments("/health")
            || path.StartsWithSegments("/swagger")
            // /api/tenants/setup 제거 (2026-06-25): 익명 백도어 제거에 동반(부모계정은 create-parent 단일).
            || path.StartsWithSegments("/api/auth/login")
            || path.StartsWithSegments("/api/auth/refresh")
            // 20260825작10 — 사장님 실측: 로그인창 콘솔에 401 이 계속 찍혔다.
            //   이 주소는 "로그인 전" 업데이트 안내를 읽는다 — 토큰이 없는 게 정상이다.
            //   엔드포인트에 [AllowAnonymous] 가 붙어 있는데도, 그 앞의 이 미들웨어가 먼저 401 로 잘랐다.
            //   🔴 /api/auth 를 통째로 열지 않는다 — me·logout 까지 열린다. 이 주소 하나만 연다.
            //   서버가 스스로 loopback 여부를 보고 판단하므로(AuthController), 열어도 정보가 새지 않는다.
            || path.StartsWithSegments("/api/auth/update-status-local")
            // 🔴 20260825작12 — 작10 이 조회(GET)만 열고 실행(POST)을 빠뜨렸다.
            //   사장님 오더: "로그인 전에 업데이트를 먼저 받을 수 있도록 하자고 해서 이렇게 된거면
            //                 그럼 그렇게 구현이 되야지."
            //   실측(loopback): GET update-status-local → 200 인데
            //                   POST update-consent-local → **401**.
            //   ⇒ 「지금 업데이트」 버튼은 배포 이후 **한 번도 눌린 적이 없다.**
            //      안내는 떴는데 눌러도 아무 일이 안 났다 — 탈출구가 그려져만 있고 막혀 있었다.
            //   이 주소도 [AllowAnonymous] 인데 이 미들웨어가 먼저 잘랐다. 조회와 같은 이유로 연다.
            //   🔴 /api/auth 를 통째로 열지 않는다 — 이 주소 하나만 연다(작10 원칙 계승).
            //   실행 가부는 AuthController 가 스스로 판단한다(버전 대조·서명검증은 워치독).
            || path.StartsWithSegments("/api/auth/update-consent-local")
            // 🔴 20260922작3 — 사장님 결재. **메인PC 「왕복 증명」의 ②**(20260922작2 절A·절B).
            //   [무엇이 났나] 실측: `/api/devices/mainpc-proof` → **401 · 컨트롤러 도달 안 함.**
            //     ②는 브라우저가 **자기 PC 안의 히트판을** 두드리는 길이라 설계상 토큰을 싣지 않는다
            //     (`hitpan-mainpc-proof.js` 의 `credentials:'omit'` · 인터페이스 주석
            //      *"이 길은 로그인 토큰 없이 지나간다"*). ⇒ 이 미들웨어가 먼저 잘라
            //     `confirmed` 가 **영원히 false** 였다. 모든 PC 가 클라이언트로 판정되고
            //     자료관리가 안 열린다 — 사장님이 보신 증상 ②③ 이 하나도 봉합되지 않은 상태였다.
            //   🔴 **이 파일에서 같은 사고가 세 번째다**(작10 update-status-local · 작12 update-consent-local).
            //     `[AllowAnonymous]` 는 **이 미들웨어보다 뒤**라 소용이 없다.
            //     ⇒ ERP API 에 **토큰 없이 부르는 길**을 새로 낼 때는 이 목록을 먼저 본다.
            //   [열어도 안전한 근거] 앞의 두 주소와 같은 논리다 — **서버가 스스로 판단한다.**
            //     컨트롤러가 들어오자마자 `MainPcOnlyAttribute.IsLocalConsole` 을 보고(터널 헤더가 하나라도
            //     있으면 즉시 거절), 표는 **1회용·60초·세션 묶음**이며 회사 식별자도 **표 안에서** 나온다.
            //     표를 맞히지 못하면 아무 일도 일어나지 않는다.
            //   🔴 `/api/devices` 를 통째로 열지 않는다 — **이 주소 하나만** 연다(작10 원칙 계승).
            //     ③ `mainpc-verify` 와 `mainpc-register` 는 토큰이 **반드시** 있어야 한다. 그것이
            //     세션 대조와 대표 확인이 걸리는 자리다. 게이트가 이 비대칭을 지킨다
            //     (`MainPcProofRoundTripGateTests` G-1c · 대조군 C-16: 이 줄을 빼면 G-1c 가 FAIL 한다).
            || path.StartsWithSegments("/api/devices/mainpc-proof")
            || path.StartsWithSegments("/api/backoffice/auth")
            // 사장님 결재 저장 2026-06-02 모두결재 — 랜딩 가입·결제·설치 저장 = 인증 면제 (AllowAnonymous 저장 정합)
            || path.StartsWithSegments("/api/landing")
            || path.StartsWithSegments("/api/install")
            // 사장님 결재 저장 2026-06-08 모두결재 — 백오피스→ERP webhook 저장 (HMAC 서명·nonce 저장할 영역 자체 검증, 헌법 #35 정합)
            || path.StartsWithSegments("/api/internal")
            // 부모계정 생성 정식 경로 (사장님 결재 2026-07-06, 작지서 20260706작1):
            //   /api/setup = CompanyBootstrapController(bootstrap·create-parent) 단독. 진입 즉시
            //   VerifyBootstrapToken(HMAC-SHA256 서명+audience+만료 4중검증)이 자체 인증하므로 익명 면제해도
            //   백도어 아님(서명 없는 익명 요청은 401). 커밋 ed46b1f(2026-06-25)가 구 /api/tenants/setup 익명
            //   백도어 제거 시 신 경로 화이트리스트 등재를 누락 → 모든 신규/재설치 부모계정 생성 401 차단(헌법 #20 위반)을 봉합.
            || path.StartsWithSegments("/api/setup"))
        {
            await _next(context);
            return;
        }

        // Excel/PDF: opened in a new window with ?token=…; controller validates token and tenant.
        if (IsDocumentDownload(path))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // 보안 격벽 (사장님 결재 2026-06-18): ERP 계정 계층은 부모/자식 둘뿐.
        //   본사(platform)·대리점(reseller) 계층은 백오피스 전용 → ERP에서 제거.
        //   고객사 PC가 뚫려도 본사·타 고객사 계층 식별자가 노출되지 않게 platform_id/reseller_id 클레임 미수신.
        var accountType = context.User.FindFirstValue("account_type");
        var tenantId = context.User.FindFirstValue("tenant_id");
        var userId = context.User.FindFirstValue("user_id");
        var role = context.User.FindFirstValue("role");

        context.Items["AccountType"] = accountType;
        context.Items["TenantId"] = tenantId;
        context.Items["UserId"] = userId;
        context.Items["UserName"] = context.User.FindFirstValue("name");
        // 봉합 (2026-06-21, A-P0-1): employee_id 는 user_id 와 별개 GUID 다(AuthService 가 두 클레임을 따로 발급,
        //   UserService.CreateAsync 가 각각 Guid.NewGuid()). 결재(approval_doc_lines.approver_id·approval_documents.
        //   requester_id·approval_history.approver_id)는 전부 employee_id 체계인데 종전 컨트롤러가 user_id 를 넘겨
        //   대기함이 빈 목록이 되고 "결재 권한이 없습니다"로 영구 차단됐다(헌법 #20). 결재용 식별자를 별도로 노출한다.
        context.Items["EmployeeId"] = context.User.FindFirstValue("employee_id");

        // ERP는 단일 회사 — 부모/자식 모두 tenant_id(회사 식별자) 필수.
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        currentTenant.Set(tenantId ?? string.Empty, userId ?? string.Empty, role ?? string.Empty, accountType ?? string.Empty);

        await _next(context);
    }

    private static bool IsDocumentDownload(PathString path)
    {
        var p = path.Value ?? string.Empty;
        if (!p.StartsWith("/api/documents/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return p.EndsWith("/excel", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("/pdf", StringComparison.OrdinalIgnoreCase);
    }
}
