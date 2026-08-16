using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HitPan.Web.Models;
using HitPan.Web.Providers;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly HitPanProtectedLocalStorage _storage;
    private readonly HitPanAuthStateProvider _authState;
    private readonly IAuthTokenRefresher _tokenRefresher;
    private readonly IJSRuntime _js;

    public AuthService(
        HttpClient http,
        HitPanProtectedLocalStorage storage,
        HitPanAuthStateProvider authState,
        IAuthTokenRefresher tokenRefresher,
        IJSRuntime js)
    {
        _http = http;
        _storage = storage;
        _authState = authState;
        _tokenRefresher = tokenRefresher;
        _js = js;
    }

    public async Task<AuthLoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            // ── 기기 지문 수집 (기기 기반 라이선싱) ──
            // JS 미로드/private 모드 등 실패해도 로그인은 진행 (서버에서 fingerprint 없으면 스킵).
            string? fingerprint = null;
            string? deviceType = null;
            string? deviceName = null;
            string? deviceId = null;
            try
            {
                fingerprint = await _js.InvokeAsync<string>("hitpanDevice.getFingerprint");
                deviceType = await _js.InvokeAsync<string>("hitpanDevice.getDeviceType");
                // 🔴 2026-08-10 [4] D-4 봉합 — 종전엔 이 값을 아무도 보내지 않아
                //   기기 목록이 전부 "(이름없음)" 이었고 메인PC 표식도 이름 없이 만들어졌다.
                deviceName = await _js.InvokeAsync<string>("hitpanDevice.getDeviceName");

                // 🔴 2026-08-16 20260816작2 — **장비넘버를 도로 보낸다** (명세서 §4-4).
                //
                //   [무엇이 없었나] 서버가 장비넘버를 내려주고(:89 아래에서 저장한다),
                //     기기가 localStorage 에 보관까지 하는데 **다음 접속에 도로 보내지 않았다.**
                //     읽는 함수(getDeviceId)는 있는데 **부르는 쪽이 0곳**이었다(명세서 §2-2 실측).
                //
                //   [무엇이 났나] 서버는 매번 지문으로만 기기를 찾았고, 지문은 브라우저가 바뀌면
                //     달라진다(_envSeed 가 userAgent 를 쓴다) ⇒ 같은 PC 인데 Edge 와 Chrome 이
                //     **서로 다른 기기**로 잡혀 슬롯을 두 번 먹었다. 사장님이 실측하신 그 증상이다.
                //
                //   ⇒ 이 한 줄이 사장님 오더 *"100번을 접속해도 한번만"* 을 성립시킨다.
                deviceId = await _js.InvokeAsync<string?>("hitpanDevice.getDeviceId");
            }
            catch { /* 지문 수집 실패 시 기본 로그인 플로우로 진행 */ }

            using var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequestDto
                {
                    Email = email,
                    Password = password,
                    DeviceFingerprint = fingerprint,
                    DeviceType = deviceType,
                    DeviceName = deviceName,
                    DeviceId = deviceId
                },
                cancellationToken: ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var err = await response.Content.ReadFromJsonAsync<ApiErrorMessageDto>(cancellationToken: ct);
                return new AuthLoginResult
                {
                    Success = false,
                    ErrorMessage = err?.Message ?? "이메일 또는 비밀번호가 틀립니다"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AuthLoginResult
                {
                    Success = false,
                    ErrorMessage = $"로그인 요청 실패 ({(int)response.StatusCode})"
                };
            }

            var data = await response.Content.ReadFromJsonAsync<LoginApiResponse>(cancellationToken: ct);
            if (data is null || string.IsNullOrEmpty(data.AccessToken))
            {
                return new AuthLoginResult { Success = false, ErrorMessage = "응답을 해석할 수 없습니다." };
            }

            // 서버가 device_id를 돌려줬으면 localStorage에 보관 (다음 로그인/미들웨어 활용용)
            if (!string.IsNullOrEmpty(data.DeviceId))
            {
                try { await _js.InvokeVoidAsync("hitpanDevice.setDeviceId", data.DeviceId); }
                catch { /* 보관 실패해도 로그인은 계속 */ }
            }

            await PersistSessionAsync(data);
            await _authState.NotifySessionChangedAsync();
            return new AuthLoginResult { Success = true, Data = data };
        }
        catch (HttpRequestException)
        {
            return new AuthLoginResult
            {
                Success = false,
                ErrorMessage = "서버에 연결할 수 없습니다. API 서버를 확인해주세요."
            };
        }
    }

    public async Task<bool> SubmitUpdateConsentAsync(string updateVersion, string action, CancellationToken ct = default)
    {
        // 고리2(A안): 로그인 직후라 인증 토큰이 보관돼 있으므로 인증 핸들러가 헤더를 붙인다.
        //   실패해도 로그인 자체는 이미 끝난 상태 — 동의 기록만 실패로 처리(false)한다.
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/update-consent",
                new UpdateConsentRequestDto { UpdateVersion = updateVersion, Action = action },
                cancellationToken: ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // 서버 연결 실패 등 — 동의 기록 실패. 다음 로그인에 재안내된다.
            return false;
        }
        catch (TaskCanceledException)
        {
            // ★ 봉합 20260806작1 P1 ([4] 검증팀 적발): 타임아웃은 HttpRequestException 이 아니라
            //   TaskCanceledException 으로 온다. 종전엔 이게 안 잡혀 호출부 catch 로 튀었고,
            //   그 결과 **가장 흔한 네트워크 장애(타임아웃)에서 화면이 그대로 침묵**했다
            //   (= 사장님이 지적하신 "아무반응이 없네" 가 그 경로에선 안 고쳐진 상태였다).
            //   false 를 돌려 호출부가 "전달하지 못했습니다" 를 안내하게 한다.
            //   ※ ct 취소(사용자 이탈)도 여기 걸리나, 그때는 화면이 이미 사라져 무해하다.
            return false;
        }
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var ok = await _tokenRefresher.TryRefreshAsync(ct);
        if (ok)
        {
            await _authState.NotifySessionChangedAsync();
        }

        return ok;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        // 로그아웃 API 호출 (자동 퇴근 기록)
        try { await _http.PostAsJsonAsync("api/auth/logout", new { }, ct); }
        catch { /* 퇴근 기록 실패해도 로그아웃 진행 */ }

        await _storage.DeleteAsync(AuthStorageKeys.AccessToken);
        await _storage.DeleteAsync(AuthStorageKeys.RefreshToken);
        await _storage.DeleteAsync(AuthStorageKeys.UserDisplayName);
        await _authState.NotifySessionChangedAsync();
    }

    private async Task PersistSessionAsync(LoginApiResponse data)
    {
        await _storage.SetAsync(AuthStorageKeys.AccessToken, data.AccessToken);
        await _storage.SetAsync(AuthStorageKeys.RefreshToken, data.RefreshToken);
        await _storage.SetAsync(AuthStorageKeys.UserDisplayName, data.UserName);
    }
}
