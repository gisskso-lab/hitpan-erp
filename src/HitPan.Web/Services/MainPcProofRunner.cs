using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

/// <summary>
/// 🔵 「왕복 증명」 한 바퀴 — ①표 받기 → ②자기 PC 안의 히트판 두드리기 → ③결과 확인.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>20260925작1 절B — 자리만 옮긴 것이다. 본문은 한 글자도 안 바꿨다</b> (헌법 #1).
/// 종전에는 <c>MainPcGate.razor</c> 안의 private 메서드였다.
/// </para>
/// <para>
/// [왜 옮겼나] 자료관리에 들어갈 때도 <b>같은 왕복</b>이 필요해졌다
/// (<c>MainPcOnly.razor</c> · 절C). 두 곳이 각자 들고 있으면
/// <b>한쪽만 고쳐지는 날이 온다</b> — <c>MainPcOnlyAttribute.cs</c> 와
/// <c>DeviceController.cs</c> 가 이미 같은 경고를 달고 있는 자리다.
/// ⇒ <b>규칙은 한 곳에만 둔다.</b> 게이트(G-1)가 복붙본이 생기는지 직접 센다.
/// </para>
/// <para>
/// ⚠️ 판정 이름(<c>MainPcConfirmed</c> · <c>NotRegisteredYet</c> · <c>DifferentPc</c> ·
/// <c>ReRegisterRequired</c> · <c>NotProven</c>)과 출입증 보관은 <b>종전 그대로</b>다.
/// 화면은 이 값으로 갈린다.
/// </para>
/// </remarks>
public sealed class MainPcProofRunner
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public MainPcProofRunner(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    /// <summary>
    /// 왕복 한 바퀴. 서버가 내린 판정 이름을 돌려준다. 실패하면 <c>NotProven</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 실패는 오류가 아니다 — <b>클라이언트 PC 에서는 실패가 정상</b>이다.
    /// 그러므로 여기서 예외를 던지거나 화면에 무엇을 띄우지 않는다.
    /// </remarks>
    public async Task<string> RunAsync()
    {
        // ① 표를 받는다 (도메인 경유)
        var issued = await _http.PostAsJsonAsync("api/devices/mainpc-challenge", new { });
        if (!issued.IsSuccessStatusCode) return "NotProven";

        var challenge = (await issued.Content.ReadFromJsonAsync<MainPcChallengeResponse>())?.Challenge;
        if (string.IsNullOrWhiteSpace(challenge)) return "NotProven";

        // ② 자기 PC 안의 히트판을 두드린다 (터널을 안 지난다)
        //   클라이언트 PC 에서는 두드릴 히트판이 없어 false 가 온다 — 그것이 정상이다.
        //   ⚠️ 브라우저가 로컬 접근 권한을 묻는 동안 여기서 기다린다
        //     (hitpan-mainpc-proof.js 의 TIMEOUT_MS · 20260925작1 절A).
        var knocked = await _js.InvokeAsync<bool>("hitpanMainPc.probe", challenge);
        if (!knocked) return "NotProven";

        // ③ 결과를 묻는다 (도메인 경유). 통과했으면 출입증이 함께 온다.
        var verified = await _http.PostAsJsonAsync("api/devices/mainpc-verify", new { challenge });
        if (!verified.IsSuccessStatusCode) return "NotProven";

        var body = await verified.Content.ReadFromJsonAsync<MainPcVerifyResponse>();
        if (body is null) return "NotProven";

        // 🟢 출입증을 보관한다 — 이후 자료관리 요청이 이것을 들고 간다.
        if (!string.IsNullOrWhiteSpace(body.Pass))
            await _js.InvokeVoidAsync("hitpanMainPc.setPass", body.Pass);

        return body.Outcome ?? "NotProven";
    }
}

/// <summary>①이 내주는 표.</summary>
public sealed class MainPcChallengeResponse
{
    public string? Challenge { get; set; }
}

/// <summary>③의 판정. <c>Pass</c> 는 통과했을 때만 들어 있다.</summary>
public sealed class MainPcVerifyResponse
{
    public string? Outcome { get; set; }
    public bool IsMainPc { get; set; }
    public string? Pass { get; set; }
}
