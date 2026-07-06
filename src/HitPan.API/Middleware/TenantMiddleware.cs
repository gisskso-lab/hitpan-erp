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
