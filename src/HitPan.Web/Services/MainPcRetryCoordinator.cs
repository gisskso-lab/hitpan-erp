using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 <b>403 <c>main_pc_only</c> 를 만나면 왕복을 한 바퀴 돌고 원요청을 한 번 더 보낸다</b>
/// — 20260924작1 절C · 설계 §6.
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 끊겼나] 출입증(<c>X-MainPc-Pass</c>)은 <b>로그인 직후 한 번</b>만 받는다.
/// 그런데 ① 출입증은 30분이면 만료되고, ② API 가 재시작되면 서버 메모리의 표가 사라지고,
/// ③ 자료관리 화면이 <b>왕복이 끝나기 전에</b> 조회를 쏘면 그 요청에는 출입증이 없다.
/// ⇒ 화면은 403 을 받고 <b>거기서 끝났다.</b> 고객이 할 수 있는 일은 재로그인뿐이었다.
/// </para>
///
/// <para>
/// [어떻게 푸나] 401 을 만나면 토큰을 다시 받아 원요청을 재시도하는 <b>바로 그 자리 옆</b>에
/// 같은 모양으로 둔다. 403 이 <c>main_pc_only</c> 일 때만 — 다른 403 은 손대지 않는다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 여기(핸들러)인가</b> — 화면마다 고치면 새 화면이 생길 때마다 또 빠진다.
/// 통로는 하나다. 🔴 <b>왕복 3콜 자신은 재시도 대상에서 뺀다</b> — 안 그러면 무한 고리다.
/// 🔴 <b>실패하면 60초 쿨다운</b> — 클라이언트 PC 에서 매 403 마다 왕복을 돌면 소음이다.
/// </para>
///
/// <para>
/// ⚠️ <b>Blazor 를 안 쓴다</b>(의도). 게이트(G-26)가 이 파일을 <b>그대로 링크해</b>
/// 실제 요청·헤더·횟수를 잰다 — 글자검사가 아니라 동작검사가 되게 하려는 것이다.
/// </para>
/// </remarks>
public sealed class MainPcRetryCoordinator(Func<DateTimeOffset> clock)
{
    /// <summary>서버가 403 본문에 적어 보내는 표식 (<c>MainPcOnlyAttribute</c> 와 글자가 같아야 한다).</summary>
    public const string ErrorMarker = "main_pc_only";

    /// <summary>🔴 실패 뒤 이만큼은 다시 왕복하지 않는다 (작지서 §3-1).</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 🔴 <b>C-② 경로</b>(작지서 §9-5) — 자동 왕복을 도는 경로는 <b>자료관리 세 갈래뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 다른 403 에서 왕복을 돌면 <b>표면이 넓어진다</b> — 오늘 출입증을 쓰는 문이 이 셋이므로
    /// 그 표면을 그대로 유지한다. 새 문이 생기면 <b>여기 한 줄</b>을 더한다(#1 추가만).
    /// </remarks>
    public static readonly string[] EligiblePathPrefixes =
    {
        "/api/backup/",
        "/api/data-reset/",
        "/api/migration/",
    };

    private readonly Func<DateTimeOffset> _clock = clock;
    private readonly object _gate = new();
    private DateTimeOffset? _lastFailureAt;

    /// <summary>
    /// 🔴 <b>C-③ 단일 왕복 잠금</b>(작지서 §9-5 · S-3).
    /// </summary>
    /// <remarks>
    /// 쿨다운은 <b>실패한 뒤에만</b> 걸린다. 그래서 화면이 조회 3개를 동시에 쏘면
    /// 셋 다 쿨다운을 통과해 <b>왕복이 3번</b> 돈다 — G-26 의 주장("1회")이 그 순간 거짓이 된다.
    /// ⇒ 먼저 들어온 <b>하나만</b> 왕복하고, 나머지는 그 결과를 쓴다.
    /// </remarks>
    private readonly SemaphoreSlim _proofLock = new(1, 1);

    /// <summary>왕복이 한 바퀴 끝날 때마다 올라간다 — <b>기다리던 요청이 "내가 돌 차례인가"</b> 를 묻는 자다.</summary>
    private int _proofEpoch;

    /// <summary>마지막 왕복이 출입증을 받아 왔는가.</summary>
    private volatile bool _lastProofSucceeded;

    /// <summary>마지막으로 출입증을 받아 온 시각.</summary>
    private DateTimeOffset? _lastSuccessAt;

    /// <summary>
    /// 🔴 이 창 안에서 온 403 은 <b>왕복 없이 재시도만</b> 한다 — 출입증이 방금 나왔기 때문이다.
    /// </summary>
    /// <remarks>짧게 잡는다. 길면 <b>진짜로 만료된 출입증</b>까지 재시도만 하다 끝난다(V-4).</remarks>
    public static readonly TimeSpan SuccessReuseWindow = TimeSpan.FromSeconds(5);

    private bool JustSucceeded()
    {
        lock (_gate)
        {
            return _lastProofSucceeded
                && _lastSuccessAt is { } at
                && _clock() - at < SuccessReuseWindow;
        }
    }

    /// <summary>🔵 실제로 왕복을 돈 횟수 — 게이트(G-26·G-32)가 <b>세어 본다.</b></summary>
    public int ProofRoundTrips { get; private set; }

    /// <summary>
    /// 왕복 3콜(<c>mainpc-challenge</c>·<c>mainpc-verify</c>·<c>mainpc-register</c>) 자신인가.
    /// </summary>
    /// <remarks>🔴 이 판정이 없으면 왕복이 403 을 만났을 때 자기 자신을 다시 불러 <b>고리가 돈다.</b></remarks>
    public static bool IsProofPath(string path) =>
        path.Contains("/api/devices/mainpc-", StringComparison.OrdinalIgnoreCase);

    /// <summary>🔴 C-② — 이 경로가 자동 왕복 대상인가.</summary>
    public static bool IsEligiblePath(string path)
    {
        foreach (var prefix in EligiblePathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

            // 상대 주소(선행 슬래시 없음)로 부른 자리도 같은 문이다.
            if (path.StartsWith(prefix[1..], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// 이 403 이 <b>자료보관 컴퓨터가 아니라서</b> 나온 것인가.
    /// </summary>
    /// <remarks>
    /// ⚠️ 본문을 읽어도 화면이 같은 본문을 다시 읽을 수 있게 <b>되돌려 놓는다</b>
    /// (형제 <c>IsDeviceAuthDeniedAsync</c> 와 같은 방식).
    /// </remarks>
    public static async Task<bool> IsMainPcDeniedAsync(
        HttpResponseMessage response,
        Action<string, Exception?> log)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return false;

            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return body.Contains(ErrorMarker, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 본문을 못 읽으면 평소 403 으로 다룬다.
            log("403 본문을 읽지 못했습니다 — 평소 403 으로 다룹니다.", ex);
            return false;
        }
    }

    /// <summary>
    /// 🔵 왕복을 한 바퀴 돌고 원요청을 <b>한 번</b> 재시도한다.
    /// </summary>
    /// <returns>재시도한 응답. 왕복을 못 돌았으면 <c>null</c>(부른 쪽이 원래 403 을 그대로 돌려준다).</returns>
    public async Task<HttpResponseMessage?> TryRecoverAsync(
        HttpRequestMessage original,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpRequestMessage, Task<HttpRequestMessage>> clone,
        Func<HttpRequestMessage, Task> decorate,
        Func<string, Task<bool>> probe,
        Func<string, Task> savePass,
        Func<Task<bool>> isOwner,
        Action<string, Exception?> log,
        CancellationToken ct)
    {
        var path = original.RequestUri?.AbsolutePath ?? string.Empty;

        // 🔴 왕복 자신은 재시도하지 않는다 — 무한 고리 금지.
        if (IsProofPath(path)) return null;

        // 🔴 C-② 경로 — 자료관리 세 갈래가 아니면 **손대지 않는다**(§9-5).
        if (!IsEligiblePath(path))
        {
            log($"자료보관 컴퓨터 재확인 대상 경로가 아닙니다: {path}", null);
            return null;
        }

        // 🔴 C-① 사람 — 오늘 출입증을 받는 사람은 **대표뿐**이다. 그 표면을 그대로 유지한다(§9-5).
        //   ⚠️ 이것은 **차단이 아니다.** 서버가 Q-1 로 막는다 — 여기서는 헛왕복을 안 돌 뿐이다.
        if (!await isOwner().ConfigureAwait(false))
        {
            log($"대표 계정이 아니어서 자료보관 컴퓨터 재확인을 돌지 않습니다: {path}", null);
            return null;
        }

        if (!TryEnter())
        {
            log($"자료보관 컴퓨터 재확인을 건너뜁니다(쿨다운 {Cooldown.TotalSeconds:0}초): {path}", null);
            return null;
        }

        // 🔴 C-③ — 여기부터 **하나씩** 들어간다. 동시에 온 403 들은 먼저 들어간 하나를 기다린다.
        var epochBefore = Volatile.Read(ref _proofEpoch);

        await _proofLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 🔴 기다리는 동안 **앞 사람이 이미 한 바퀴 돌았다면** 또 돌지 않는다.
            //   이 대조가 없으면 쿨다운을 통과한 셋이 그대로 세 바퀴를 돈다 —
            //   G-26 의 "1회" 주장이 동시요청에서 거짓이 되는 자리다(S-3).
            if (Volatile.Read(ref _proofEpoch) != epochBefore)
            {
                if (!_lastProofSucceeded)
                {
                    log($"앞선 재확인이 실패했습니다 — 왕복을 다시 돌지 않습니다: {path}", null);
                    return null;
                }

                log($"앞선 재확인이 받아 온 출입증을 씁니다(왕복 0회): {path}", null);
                return await RetryOriginalAsync(original, send, clone, decorate, ct).ConfigureAwait(false);
            }

            // 🔴 **방금** 받아 온 출입증이 있으면 왕복을 또 돌지 않는다.
            //   ⚠️ epoch 대조만으로는 부족하다 — 화면이 쏜 셋 중 마지막 하나가 앞의 왕복이
            //     **끝난 뒤에** 도착하면 자기 차례라고 믿고 또 돈다(실측: 3건에 왕복 2회).
            //     그 요청에 필요한 것은 새 출입증이 아니라 **재시도 한 번**이다.
            if (JustSucceeded())
            {
                log($"방금 받아 온 출입증을 씁니다(왕복 0회): {path}", null);
                return await RetryOriginalAsync(original, send, clone, decorate, ct).ConfigureAwait(false);
            }

            return await RecoverInsideLockAsync(
                original, send, clone, decorate, probe, savePass, log, ct).ConfigureAwait(false);
        }
        finally
        {
            _proofLock.Release();
        }
    }

    /// <summary>원요청을 <b>한 번만</b> 다시 보낸다 — <c>decorate</c> 가 출입증을 실어 준다.</summary>
    private async Task<HttpResponseMessage> RetryOriginalAsync(
        HttpRequestMessage original,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpRequestMessage, Task<HttpRequestMessage>> clone,
        Func<HttpRequestMessage, Task> decorate,
        CancellationToken ct)
    {
        var retry = await clone(original).ConfigureAwait(false);
        await decorate(retry).ConfigureAwait(false);

        var response = await send(retry, ct).ConfigureAwait(false);

        // 여전히 막히면 실패로 기록한다 — 다음 403 이 곧바로 또 왕복하지 않게.
        if (response.StatusCode == HttpStatusCode.Forbidden) MarkFailure();
        else ClearFailure();

        return response;
    }

    private async Task<HttpResponseMessage?> RecoverInsideLockAsync(
        HttpRequestMessage original,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpRequestMessage, Task<HttpRequestMessage>> clone,
        Func<HttpRequestMessage, Task> decorate,
        Func<string, Task<bool>> probe,
        Func<string, Task> savePass,
        Action<string, Exception?> log,
        CancellationToken ct)
    {
        // 🔵 이 한 바퀴를 **세어 둔다.** 뒤에 대기하던 요청들은 이 값이 올라간 것을 보고
        //   자기는 안 돈다(위 epoch 대조). 게이트 G-26·G-32 가 이 숫자를 읽는다.
        ProofRoundTrips++;
        Interlocked.Increment(ref _proofEpoch);
        _lastProofSucceeded = false;

        try
        {
            var pass = await RunProofAsync(send, decorate, probe, log, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pass))
            {
                MarkFailure();
                return null;
            }

            await savePass(pass!).ConfigureAwait(false);
            _lastProofSucceeded = true;
            lock (_gate) { _lastSuccessAt = _clock(); }

            // 🟢 원요청을 **한 번만** 다시 보낸다. decorate 가 방금 받은 출입증을 실어 준다.
            return await RetryOriginalAsync(original, send, clone, decorate, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 여기서 터져도 원래 403 이 화면으로 간다.
            log("자료보관 컴퓨터 재확인 중 오류 — 원래 응답을 그대로 돌려줍니다.", ex);
            MarkFailure();
            return null;
        }
    }

    // ── 왕복 ①②③ ────────────────────────────────────────────────

    private static async Task<string?> RunProofAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpRequestMessage, Task> decorate,
        Func<string, Task<bool>> probe,
        Action<string, Exception?> log,
        CancellationToken ct)
    {
        // ① 표를 받는다 (도메인 경유)
        using var issue = new HttpRequestMessage(HttpMethod.Post, "api/devices/mainpc-challenge")
        {
            Content = JsonContent.Create(new { })
        };
        await decorate(issue).ConfigureAwait(false);

        using var issued = await send(issue, ct).ConfigureAwait(false);
        if (!issued.IsSuccessStatusCode)
        {
            log($"자료보관 컴퓨터 표를 받지 못했습니다(status={(int)issued.StatusCode}).", null);
            return null;
        }

        var challenge = (await issued.Content
            .ReadFromJsonAsync<ChallengeResponse>(cancellationToken: ct).ConfigureAwait(false))?.Challenge;
        if (string.IsNullOrWhiteSpace(challenge)) return null;

        // ② 자기 PC 안의 히트판을 두드린다 (터널을 안 지난다)
        //   클라이언트 PC 에는 두드릴 히트판이 없다 — 여기서 끝나는 것이 **정상**이다.
        if (!await probe(challenge!).ConfigureAwait(false)) return null;

        // ③ 결과를 묻는다. 통과했으면 출입증이 함께 온다.
        using var verify = new HttpRequestMessage(HttpMethod.Post, "api/devices/mainpc-verify")
        {
            Content = JsonContent.Create(new { challenge })
        };
        await decorate(verify).ConfigureAwait(false);

        using var verified = await send(verify, ct).ConfigureAwait(false);
        if (!verified.IsSuccessStatusCode)
        {
            log($"자료보관 컴퓨터 확인이 실패했습니다(status={(int)verified.StatusCode}).", null);
            return null;
        }

        var body = await verified.Content
            .ReadFromJsonAsync<VerifyResponse>(cancellationToken: ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body?.Pass) ? null : body!.Pass;
    }

    // ── 쿨다운 ───────────────────────────────────────────────────

    private bool TryEnter()
    {
        lock (_gate)
        {
            if (_lastFailureAt is { } at && _clock() - at < Cooldown) return false;
            return true;
        }
    }

    private void MarkFailure()
    {
        // 🔴 재시도가 또 403 이면 「방금 받아 온 출입증」 창도 함께 닫는다 —
        //   안 닫으면 5초 동안 왕복 없이 재시도만 하다 끝난다.
        _lastProofSucceeded = false;
        lock (_gate)
        {
            _lastFailureAt = _clock();
            _lastSuccessAt = null;
        }
    }

    private void ClearFailure()
    {
        lock (_gate) { _lastFailureAt = null; }
    }

    private sealed class ChallengeResponse
    {
        public string? Challenge { get; set; }
    }

    private sealed class VerifyResponse
    {
        public string? Outcome { get; set; }
        public bool IsMainPc { get; set; }
        public string? Pass { get; set; }
    }
}
