using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace HitPan.Web.Providers;

public sealed class HitPanAuthStateProvider(
    HitPanProtectedLocalStorage storage,
    IAuthTokenRefresher tokenRefresher) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var access = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
        if (!access.Success || string.IsNullOrEmpty(access.Value))
        {
            return Anonymous();
        }

        var token = access.Value;
        if (IsAccessTokenExpired(token))
        {
            var refreshed = await tokenRefresher.TryRefreshAsync();
            if (!refreshed)
            {
                await storage.DeleteAsync(AuthStorageKeys.AccessToken);
                await storage.DeleteAsync(AuthStorageKeys.RefreshToken);
                await storage.DeleteAsync(AuthStorageKeys.UserDisplayName);
                return Anonymous();
            }

            var again = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
            if (!again.Success || string.IsNullOrEmpty(again.Value))
            {
                return Anonymous();
            }

            token = again.Value;
        }

        var nameResult = await storage.GetAsync<string>(AuthStorageKeys.UserDisplayName);
        var displayName = nameResult is { Success: true, Value: not null } ? nameResult.Value : null;
        return new AuthenticationState(CreatePrincipal(token, displayName));
    }

    public Task NotifySessionChangedAsync()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return Task.CompletedTask;
    }

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static bool IsAccessTokenExpired(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return jwt.ValidTo <= DateTime.UtcNow.AddSeconds(30);
    }

    private static ClaimsPrincipal CreatePrincipal(string accessToken, string? displayName)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var claims = jwt.Claims.ToList();

        var role = claims.FirstOrDefault(x => x.Type == "role")?.Value;
        if (!string.IsNullOrEmpty(role))
        {
            claims.RemoveAll(x => x.Type == ClaimTypes.Role);
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var userId = claims.FirstOrDefault(x => x.Type == "user_id")?.Value;
        claims.RemoveAll(x => x.Type == ClaimTypes.Name);
        if (!string.IsNullOrEmpty(displayName))
        {
            claims.Add(new Claim(ClaimTypes.Name, displayName));
        }
        else if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.Name, userId));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return new ClaimsPrincipal(identity);
    }
}
