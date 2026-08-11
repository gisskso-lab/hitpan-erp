using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.API.Security;

/// 자료보관 컴퓨터(메인PC)에서 온 요청만 통과시킨다.
///
/// 🔴 2026-08-11 (사장님 지시):
///   *"자료관리는 **부모계정 + 메인PC 환경에서만** 돌도록"*
///
///   [왜 서버에도 거나] 화면을 감추는 것은 **차단이 아니다.**
///     화면을 안 보여줘도 API 주소를 알면 직접 부를 수 있다. 백업 복원이나
///     모든데이터 초기화가 그렇게 불리면 회사 장부가 통째로 사라진다.
///     ⇒ 화면(MainPcOnly.razor)과 **여기 둘 다** 막아야 막은 것이다.
///
///   [어떻게 아는가] 브라우저 말을 믿지 않는다 — "나 메인PC야" 라고 보내오는 값은
///     얼마든지 지어낼 수 있다. **서버가 요청이 들어온 자리를 직접 본다.**
///     히트판 본체는 자료가 있는 그 컴퓨터에서 돈다. 그 컴퓨터에서 열면 자기 자신에게
///     붙고(로컬), 다른 컴퓨터에서 열면 터널을 지나 바깥에서 들어온다.
///
///   ⚠️ 터널을 지나온 요청은 원래 주소를 헤더에 달고 온다. 헤더가 하나라도 있으면
///     바깥에서 온 것이다 — 로컬 주소로 보이더라도 메인PC 가 아니다.
///     (이 판정은 DeviceController.IsMainPc 와 같은 규칙이어야 한다.
///      한쪽만 고치면 화면은 열리는데 저장이 안 되는 상태가 된다)
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MainPcOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var req = context.HttpContext.Request;

        var viaTunnel =
            req.Headers.ContainsKey("CF-Connecting-IP") ||
            req.Headers.ContainsKey("X-Forwarded-For");

        var remote = context.HttpContext.Connection.RemoteIpAddress;
        var isLoopback = remote is not null && System.Net.IPAddress.IsLoopback(remote);

        if (viaTunnel || !isLoopback)
        {
            context.Result = new ObjectResult(new
            {
                error = "main_pc_only",
                message = "이 기능은 회사 자료가 들어 있는 컴퓨터에서만 사용할 수 있습니다."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        base.OnActionExecuting(context);
    }
}
