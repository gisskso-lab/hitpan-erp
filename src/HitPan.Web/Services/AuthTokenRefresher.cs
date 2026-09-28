using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

/// <remarks>
/// 🔴 20260928작2 절I (설계 §13-2) — 갱신 두 자리 중 하나(다른 하나는 <c>HitPanApiAuthHandler.TryRefreshAsync</c>).
/// <para>⬛ [낡은 모양 · 절I 이전] 잠금 없이 곧장 <c>api/auth/refresh</c> 를 부르고 <b>비 2xx 전부 <c>false</c></b> —
/// 호출부(<c>HitPanAuthStateProvider</c>)가 그 <c>false</c> 에 토큰을 지웠다. 저장소를 탭끼리 나눠 쓰게 되자(절H)
/// 두 탭이 같은 refresh 를 동시에 써서 두 번째가 401 → 멀쩡한 로그인이 지워졌다(PI-2).</para>
/// <para>🔴 [지금] ① <c>RefreshGate.Lock</c>(탭 안) + <c>RefreshTabLock</c>(탭 사이) ② 잠금을 잡은 뒤 <b>이중확인</b> —
/// 기다리는 사이 저장소 refresh 가 바뀌었으면 남이 돌린 것이라 성공 ③ 응답 판정은 <c>RefreshGate.Decide</c> —
/// <b>지움(Clear)일 때만 이 자리에서 지운다.</b> 유지(Keep)면 저장소를 그대로 두고 <c>false</c> 를 돌려준다
/// (호출부는 저장소에 refresh 가 남아 있는지로 「유지」를 안다 — 인터페이스 무변경).</para>
/// </remarks>
public sealed class AuthTokenRefresher(
    HttpClient http,
    HitPanProtectedLocalStorage storage,
    IJSRuntime js,
    ILogger<AuthTokenRefresher> logger) : IAuthTokenRefresher
{
    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        var seen = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
        if (!seen.Success || string.IsNullOrEmpty(seen.Value))
        {
            return false;
        }

        await RefreshGate.Lock.WaitAsync(ct);
        try
        {
            await using var tabLock = await RefreshTabLock.AcquireAsync(js, logger);

            // 🔴 이중확인 — 기다리는 사이 다른 자리·다른 탭이 이미 돌렸으면 그 토큰을 쓴다(서버에 또 보내지 않는다).
            var refresh = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
            if (!refresh.Success || string.IsNullOrEmpty(refresh.Value))
            {
                return false;   // 그새 다른 탭이 로그아웃했다
            }
            if (!string.Equals(refresh.Value, seen.Value, StringComparison.Ordinal))
            {
                logger.LogInformation("다른 탭이 먼저 토큰을 갱신했습니다 — 그 토큰을 씁니다.");
                return true;
            }

            int? status = null;
            LoginApiResponse? data = null;
            try
            {
                using var response = await http.PostAsJsonAsync(
                    "api/auth/refresh",
                    new RefreshTokenRequestDto { RefreshToken = refresh.Value },
                    cancellationToken: ct);

                status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    data = await response.Content.ReadFromJsonAsync<LoginApiResponse>(cancellationToken: ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            {
                // 연결 실패·시간 초과 — 응답을 못 받았다(status = null ⇒ 유지).
                logger.LogWarning(ex, "토큰 갱신 요청이 서버에 닿지 못했습니다 — 저장된 로그인은 그대로 둡니다.");
            }

            var after = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
            switch (RefreshGate.Decide(status, refresh.Value, after.Success ? after.Value : null))
            {
                case RefreshDecision.Saved:
                    if (data is null || string.IsNullOrEmpty(data.AccessToken))
                    {
                        logger.LogWarning("토큰 갱신 응답이 비어 있습니다 — 저장된 로그인은 그대로 둡니다.");
                        return false;
                    }

                    // ⬛ [낡은 순서 · 개정3 절S] access → refresh → 표시이름
                    // 🔴 개정3 절S — **refresh 먼저**: 남의 탭이 비교할 때 새 access 만 있고 옛 refresh 가 남은 틈을 없앤다.
                    await storage.SetAsync(AuthStorageKeys.RefreshToken, data.RefreshToken);
                    await storage.SetAsync(AuthStorageKeys.AccessToken, data.AccessToken);
                    await storage.SetAsync(AuthStorageKeys.UserDisplayName, data.UserName);
                    RefreshGate.RecordRefreshOutcome(RefreshDecision.Saved, DateTimeOffset.UtcNow);
                    return true;

                case RefreshDecision.OtherTabRotated:
                    RefreshGate.RecordRefreshOutcome(RefreshDecision.OtherTabRotated, DateTimeOffset.UtcNow);
                    return true;

                case RefreshDecision.Clear:
                    logger.LogWarning("토큰 갱신 거절(status={Status}) — 로그인이 끝났습니다.", status);
                    // ⬛ [낡은 지움 · 개정3 절S] DeleteAsync 세 칸 — 비교 없이 지웠다(그새 남의 탭이 쓴 새 토큰까지).
                    // 🔴 개정3 절S — 보낸 값과 같을 때만 지운다(한 JS 호출 안에서 비교·지움).
                    if (await storage.DeleteAuthIfRefreshIsAsync(refresh.Value) == AuthRemoveResult.Rotated)
                    {
                        logger.LogInformation("지우기 직전에 다른 탭이 새 토큰을 썼습니다 — 그 토큰을 씁니다.");
                        RefreshGate.RecordRefreshOutcome(RefreshDecision.OtherTabRotated, DateTimeOffset.UtcNow);
                        return true;
                    }
                    RefreshGate.RecordRefreshOutcome(RefreshDecision.Clear, DateTimeOffset.UtcNow);
                    return false;

                default:
                    logger.LogWarning("토큰 갱신 실패(status={Status}) — 서버 문제로 보고 로그인은 그대로 둡니다.", status);
                    RefreshGate.RecordRefreshOutcome(RefreshDecision.Keep, DateTimeOffset.UtcNow);
                    return false;
            }
        }
        finally
        {
            RefreshGate.Lock.Release();
        }
    }
}
