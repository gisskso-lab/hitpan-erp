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
    /// <summary>
    /// 이 요청이 <b>자료가 들어 있는 그 컴퓨터</b>에서 온 것인가.
    /// </summary>
    /// <remarks>
    /// 🔴 20260910작1 A1: 판정을 이 한 곳으로 뺐다(본문 무변경 · 헌법 #1).
    /// 「모두 지우고 새로 가져오기」는 어트리뷰트가 아니라 <b>요청 안에서</b> 이 규칙을 물어야 하기 때문이다 —
    /// 같은 <c>/start</c> 라도 「없는 것만 보태기」는 어디서든 되고, <b>지우는 쪽만</b> 메인PC 로 묶는다.
    /// 규칙을 복붙하면 한쪽만 고쳐지는 날이 온다.
    /// </remarks>
    /// <summary>
    /// 🔴 <b>이 요청이 그 컴퓨터 안에서 직접 들어왔는가</b> — 소켓만 본다. 20260922작2 절D.
    /// </summary>
    /// <remarks>
    /// 종전 <c>IsMainPc</c> 의 본문을 <b>한 글자도 바꾸지 않고</b> 이 이름으로 옮겼다(헌법 #1).
    /// <para>
    /// 🔴 <b>이 판정만 「왕복 증명」의 ②를 받을 수 있다.</b> 아래 <c>IsMainPc</c> 는 출입증도 인정하는데,
    /// ②가 그것을 쓰면 <b>출입증을 가진 브라우저가 스스로 출입증을 갱신하는 고리</b>가 생긴다.
    /// 한 번 새어 나간 출입증이 영원히 사는 길이 열린다. <b>두 판정을 절대 합치지 마라.</b>
    /// </para>
    /// </remarks>
    public static bool IsLocalConsole(HttpContext http)
    {
        var req = http.Request;

        var viaTunnel =
            req.Headers.ContainsKey("CF-Connecting-IP") ||
            req.Headers.ContainsKey("X-Forwarded-For");

        var remote = http.Connection.RemoteIpAddress;
        var isLoopback = remote is not null && System.Net.IPAddress.IsLoopback(remote);

        return !viaTunnel && isLoopback;
    }

    /// <summary>
    /// 🔵 「왕복 증명」이 발급한 <b>출입증</b>을 담아 오는 헤더 (20260922작2 절D).
    /// </summary>
    public const string PassHeader = "X-MainPc-Pass";

    public static bool IsMainPc(HttpContext http)
    {
        // ① 그 컴퓨터에서 직접 연 화면 — 종전 경로. 그대로 통과시킨다(무회귀 · 헌법 #1).
        if (IsLocalConsole(http)) return true;

        // ② 🔴 20260922작2 절D — 도메인(터널)으로 들어온 메인PC.
        //
        //   [왜 필요한가] 고객은 도메인으로 접속한다. 터널을 지나오면 ①은 **반드시 거짓**이다 —
        //     Cloudflare 가 CF-Connecting-IP 를 붙이기 때문이다.
        //     ⇒ 종전에는 도메인으로 여는 한 메인PC 로 인식된 적이 **한 번도 없었고**,
        //       자료관리(백업·초기화·자료이관)가 전부 403 이었다.
        //
        //   [무엇을 믿나] 브라우저가 **자기 PC 안의 히트판을 실제로 두드려** 받아 온 출입증.
        //     기기ID 나 사용자ID 로 기억하지 않는다 — 그것이 지금 뚫려 있는 바로 그 구멍이다
        //     (밖에서 온 값으로 신분을 정하면 언제나 자칭이 가능하다).
        //     출입증은 **서버가 만들어 서버 메모리에만 두고**, 짧게 산다.
        var pass = http.Request.Headers[PassHeader].ToString();
        if (string.IsNullOrWhiteSpace(pass)) return false;

        // ⚠️ 어트리뷰트는 DI 를 직접 못 받는다 — 요청 범위에서 꺼낸다.
        //   서비스가 없으면(구성 누락) **막는다.** 열어 두는 쪽으로 실패하지 않는다.
        var proof = http.RequestServices
            .GetService(typeof(HitPan.Application.Interfaces.IMainPcProofService))
            as HitPan.Application.Interfaces.IMainPcProofService;

        return proof?.IsPassValid(pass) == true;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!IsMainPc(context.HttpContext))
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
