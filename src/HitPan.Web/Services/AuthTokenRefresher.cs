using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class AuthTokenRefresher(HttpClient http, HitPanProtectedLocalStorage storage) : IAuthTokenRefresher
{
    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        var refresh = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
        if (!refresh.Success || string.IsNullOrEmpty(refresh.Value))
        {
            return false;
        }

        using var response = await http.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshTokenRequestDto { RefreshToken = refresh.Value },
            cancellationToken: ct);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var data = await response.Content.ReadFromJsonAsync<LoginApiResponse>(cancellationToken: ct);
        if (data is null || string.IsNullOrEmpty(data.AccessToken))
        {
            return false;
        }

        await storage.SetAsync(AuthStorageKeys.AccessToken, data.AccessToken);
        await storage.SetAsync(AuthStorageKeys.RefreshToken, data.RefreshToken);
        await storage.SetAsync(AuthStorageKeys.UserDisplayName, data.UserName);
        return true;
    }
}
