using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace HitPan.Web.Services;

public sealed class HitPanApiAuthHandler(
    HitPanProtectedLocalStorage storage,
    ISnackbar snackbar,
    Microsoft.JSInterop.IJSRuntime js,
    ILogger<HitPanApiAuthHandler> logger) : DelegatingHandler
{
    /// 기기 인증 번호를 실어 보내는 헤더 이름 (20260811작3 (A)).
    private const string DeviceKeyHeader = "X-HitPan-Device-Key";

    // 🔴 장비넘버 헤더 (20260816작2) — 인증번호가 없는 기기(메인PC)도 통과할 길.
    //   ⚠️ 서버 DeviceAuthMiddleware.DeviceIdHeader 와 **글자가 같아야** 한다.
    private const string DeviceIdHeader = "X-HitPan-Device-Id";
    // 동시 다발 401 시 refresh 가 중복 호출되지 않도록 직렬화한다(토큰 회전 충돌 방지).
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var skipBearer = path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);

        if (!skipBearer)
        {
            await AttachTokenAsync(request).ConfigureAwait(false);
            await AttachDeviceKeyAsync(request).ConfigureAwait(false);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // ── 진범 #22 전역 봉합 (2026-06-19) ──
        // AccessToken 15분 만료 → 모든 API 호출이 401. 기존엔 마이그 status 폴링만 수동 처리해
        // 미리보기·시작·챗봇 등은 401로 그대로 죽었다(사장님 "사장이 일일이 확인해야 에러 안 뜬다").
        // 핸들러 레벨에서 401 1회 자동 refresh + 원요청 재시도 → 모든 화면 토큰 만료 영구 봉합.
        // refresh/login 경로는 skipBearer 로 제외되어 무한루프 없음.
        if (response.StatusCode == HttpStatusCode.Unauthorized && !skipBearer)
        {
            logger.LogWarning("401 Unauthorized on {Method} {Path} — 토큰 자동 재발급을 시도합니다.",
                request.Method, path);

            // 진범 봉합 (2026-06-20, 2차 전수조사 AUTH-02): 이 요청이 들고 있던 AccessToken 을 기억해
            //   TryRefreshAsync 에 넘긴다. 락 진입 직후 토큰이 그새 바뀌어 있으면(=동시 401 중 다른 요청이
            //   이미 회전 완료) refresh 를 건너뛰고 즉시 재시도 → 불필요한 이중 회전·토큰 churn 방지.
            var tokenAt401 = await storage.GetAsync<string>(AuthStorageKeys.AccessToken).ConfigureAwait(false);
            var refreshed = await TryRefreshAsync(tokenAt401.Success ? tokenAt401.Value : null, cancellationToken)
                .ConfigureAwait(false);
            if (refreshed)
            {
                // HttpRequestMessage 는 1회용 — 동일 요청을 복제해 새 토큰으로 재시도.
                var retry = await CloneRequestAsync(request).ConfigureAwait(false);
                await AttachTokenAsync(retry).ConfigureAwait(false);

                response.Dispose();
                response = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
            }
            else if (tokenAt401.Success && !string.IsNullOrEmpty(tokenAt401.Value))
            {
                // 봉합 (2026-06-20, AUTH-03): 토큰을 들고 있었는데 자동 재발급이 최종 실패(RefreshToken
                //   만료/무효) → 세션이 끝난 것. 종전엔 원래 401 을 조용히 돌려줘 화면이 '이유 없이' 죽었다.
                //   403 처리와 대칭으로, 저장 토큰을 정리하고 사용자에게 재로그인을 정직하게 안내한다.
                //   (AuthStateProvider 가 다음 네비게이션에서 Anonymous→/login 으로 보낸다.)
                await storage.DeleteAsync(AuthStorageKeys.AccessToken).ConfigureAwait(false);
                await storage.DeleteAsync(AuthStorageKeys.RefreshToken).ConfigureAwait(false);
                await storage.DeleteAsync(AuthStorageKeys.UserDisplayName).ConfigureAwait(false);
                snackbar.Add("로그인이 만료되었습니다. 다시 로그인해주세요.", Severity.Warning);
            }
        }

        // §절대원칙 #19 — 403은 "서버 오류"가 아닌 "권한 없음"으로 사용자에게 정직하게 알린다.
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // 기기 인증이 안 된 경우인지 먼저 가른다 (20260811작3 (A)).
            //   권한 부족과 기기 미인증은 **다른 일**이다. 같은 문구로 뭉뚱그리면
            //   직원이 "권한을 달라" 고 엉뚱한 요청을 한다.
            if (await IsDeviceAuthDeniedAsync(response).ConfigureAwait(false))
            {
                logger.LogWarning("기기 미인증으로 차단됨: {Method} {Path}", request.Method, path);
                DeviceAuthState.MarkBlocked();
            }
            else
            {
                logger.LogWarning("403 Forbidden on {Method} {Path}", request.Method, path);
                snackbar.Add("이 기능에 접근할 권한이 없습니다. 관리자에게 문의하세요.", Severity.Warning);
            }
        }

        return response;
    }

    /// <summary>저장된 AccessToken 을 Authorization 헤더에 붙인다(없으면 그대로 통과).</summary>
    /// 이 403 이 "기기 인증이 안 됐다" 는 뜻인지 가른다 (20260811작3 (A)).
    ///
    ///   ⚠️ 응답 본문을 읽어도 원래 흐름이 깨지지 않게 한다 — 여기서 읽어버리면
    ///     화면 쪽에서 같은 본문을 다시 못 읽는다. 그래서 문자열로 받아 확인만 한다.
    private static async Task<bool> IsDeviceAuthDeniedAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return false;

            var isDeviceDenied = body.Contains("forbidden_device_auth", StringComparison.Ordinal);

            // 읽어버린 본문을 되돌려 놓는다 — 화면이 다시 읽을 수 있어야 한다.
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");

            return isDeviceDenied;
        }
        catch
        {
            // 본문을 못 읽으면 기기 문제라고 단정하지 않는다 — 평소 403 으로 다룬다.
            return false;
        }
    }

    /// 이 기기가 받은 인증 번호를 요청에 실어 보낸다 (20260811작3 (A)).
    ///
    ///   대표가 승인하며 알려준 번호를 직원이 자기 화면에 넣어두면 여기서 매번 함께 간다.
    ///   서버는 그 번호를 저장된 것과 대조만 한다 — 추측하지 않는다.
    ///
    ///   ⚠️ 번호가 없어도 요청은 그대로 보낸다. 막을지 말지는 **서버가 정한다.**
    ///     여기서 미리 막으면 번호를 받으러 가는 길까지 함께 막힌다.
    private async Task AttachDeviceKeyAsync(HttpRequestMessage request)
    {
        try
        {
            var key = await js.InvokeAsync<string?>("hitpanDevice.getAuthKey", Array.Empty<object?>())
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.TryAddWithoutValidation(DeviceKeyHeader, key);

            // 🔴 2026-08-16 20260816작2 봉합 — **장비넘버도 함께 보낸다.**
            //
            //   [무엇이 났나] 종전에는 인증번호만 보냈다. 그런데 **메인PC 는 인증번호를 받은 적이 없다** —
            //     그 번호는 대표가 *다른* 기기를 승인할 때 만들어지는 값이고,
            //     메인PC 는 서버가 스스로 등록하기 때문이다(MainPcRegistrationService).
            //     ⇒ 승인제를 켜자 **메인PC 가 자기 화면에서 막혔다.** 그 화면에서 풀어줄 사람은
            //       자기 자신이라 **스스로 못 빠져나온다**(사장님 실측 2026-08-16).
            //
            //   ⇒ 승인된 기기는 장비넘버만으로도 통과할 수 있어야 한다. 판정은 서버가 한다.
            var deviceId = await js.InvokeAsync<string?>("hitpanDevice.getDeviceId", Array.Empty<object?>())
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(deviceId))
                request.Headers.TryAddWithoutValidation(DeviceIdHeader, deviceId);
        }
        catch (Exception ex)
        {
            // 화면이 아직 안 떴을 때(사전 렌더링) 등 — 번호를 못 읽어도 요청은 진행한다.
            logger.LogDebug(ex, "기기 인증 번호를 읽지 못했습니다 — 번호 없이 진행합니다.");
        }
    }

    private async Task AttachTokenAsync(HttpRequestMessage request)
    {
        var token = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
        if (token.Success && !string.IsNullOrEmpty(token.Value))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }
    }

    /// <summary>
    /// RefreshToken 으로 새 AccessToken/RefreshToken 을 발급받아 저장한다.
    /// 동시 401 이 몰려도 RefreshLock 으로 1회만 회전. 실패 시 false(호출부는 원래 401 유지).
    /// </summary>
    private async Task<bool> TryRefreshAsync(string? tokenAt401, CancellationToken ct)
    {
        await RefreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 더블체크(double-checked): 락 대기 중 다른 401 요청이 이미 토큰을 회전했으면
            //   저장된 AccessToken 이 401 시점과 달라진다 → 재발급 생략하고 새 토큰으로 즉시 재시도.
            if (!string.IsNullOrEmpty(tokenAt401))
            {
                var current = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
                if (current.Success && !string.IsNullOrEmpty(current.Value)
                    && !string.Equals(current.Value, tokenAt401, StringComparison.Ordinal))
                {
                    logger.LogInformation("토큰이 이미 다른 요청에 의해 갱신됨 — 재발급 생략하고 재시도.");
                    return true;
                }
            }

            var refreshToken = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
            if (!refreshToken.Success || string.IsNullOrEmpty(refreshToken.Value))
            {
                logger.LogWarning("RefreshToken 이 없어 자동 재발급 불가 — 재로그인 필요.");
                return false;
            }

            // refresh 호출은 skipBearer 경로 — base.SendAsync 로 직접 보낸다(Bearer 헤더 없이).
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshTokenRequestDto { RefreshToken = refreshToken.Value! })
            };

            using var resp = await base.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("토큰 재발급 실패(status={Status}) — RefreshToken 만료/무효. 재로그인 필요.",
                    (int)resp.StatusCode);
                return false;
            }

            var data = await resp.Content.ReadFromJsonAsync<LoginApiResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (data is null || string.IsNullOrEmpty(data.AccessToken))
            {
                logger.LogWarning("토큰 재발급 응답이 비어있습니다 — 재로그인 필요.");
                return false;
            }

            await storage.SetAsync(AuthStorageKeys.AccessToken, data.AccessToken);
            if (!string.IsNullOrEmpty(data.RefreshToken))
            {
                await storage.SetAsync(AuthStorageKeys.RefreshToken, data.RefreshToken);
            }

            logger.LogInformation("토큰 자동 재발급 성공 — 원요청을 재시도합니다.");
            return true;
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 재발급 중 예외는 경고 후 원래 401 유지.
            logger.LogWarning(ex, "토큰 자동 재발급 중 예외 — 재로그인 필요.");
            return false;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    /// <summary>HttpRequestMessage 는 1회용이라 재시도 전 동일 내용으로 복제한다(헤더·본문 포함).</summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri)
        {
            Version = req.Version
        };

        // 본문 복제(POST/PUT 마이그 요청 등) — 스트림은 이미 소비됐을 수 있어 바이트로 떠서 재구성.
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var newContent = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            clone.Content = newContent;
        }

        // Authorization 외 헤더 복제(Authorization 은 AttachTokenAsync 가 새로 붙임).
        foreach (var h in req.Headers)
        {
            if (string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        foreach (var prop in req.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(prop.Key), prop.Value);
        }

        return clone;
    }
}
