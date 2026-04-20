using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HitPan.Web.Models;
using HitPan.Web.Providers;

namespace HitPan.Web.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly HitPanProtectedLocalStorage _storage;
    private readonly HitPanAuthStateProvider _authState;
    private readonly IAuthTokenRefresher _tokenRefresher;

    public AuthService(
        HttpClient http,
        HitPanProtectedLocalStorage storage,
        HitPanAuthStateProvider authState,
        IAuthTokenRefresher tokenRefresher)
    {
        _http = http;
        _storage = storage;
        _authState = authState;
        _tokenRefresher = tokenRefresher;
    }

    public async Task<AuthLoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequestDto { Email = email, Password = password },
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
