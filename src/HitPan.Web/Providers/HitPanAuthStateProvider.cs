using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace HitPan.Web.Providers;

public sealed class HitPanAuthStateProvider(
    HitPanProtectedLocalStorage storage,
    IAuthTokenRefresher tokenRefresher,
    ISnackbar snackbar) : AuthenticationStateProvider
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
                // 🔴 20260928작2 절I (설계 §13-2 ④) — 「지움」과 「유지」를 가른다.
                //   ⬛ [낡은 갈래] 실패면 무조건 세 칸을 지우고 로그인 화면으로 보냈다 ⇒ 회선이 잠깐 끊기거나
                //     다른 탭과 동시에 갱신해도 로그인이 사라졌고, 다시 들어오려 하면 자기 접속에 막혔다(409).
                //   [지금] 지울지는 갱신 자리(`AuthTokenRefresher` · `RefreshGate.Decide`)가 정한다.
                //     저장소에 refresh 가 **남아 있으면 「유지」** — 로그인 화면으로 보내지 않고 연결 안내만 띄운다.
                //     서버가 돌아오면 다음 요청의 401 자동 재발급이 이어 준다.
                var stillHeld = await storage.GetAsync<string>(AuthStorageKeys.RefreshToken);
                if (stillHeld.Success && !string.IsNullOrEmpty(stillHeld.Value))
                {
                    // ⬛ [낡은 안내 · 개정3 절R] TryClaimOfflineNotice 30초 창만.
                    // 🔴 개정3 절R — 상한(3회 AND 60초)이면 「원활하지 않습니다」 한 번(닫을 때까지) · 그 뒤 30초 안내 멈춤.
                    HitPan.Web.Services.HitPanApiAuthHandler.ShowKeepNotice(snackbar);

                    var kept = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
                    var keptToken = kept.Success && !string.IsNullOrEmpty(kept.Value) ? kept.Value : token;
                    var keptName = await storage.GetAsync<string>(AuthStorageKeys.UserDisplayName);
                    return new AuthenticationState(CreatePrincipal(
                        keptToken, keptName is { Success: true, Value: not null } ? keptName.Value : null));
                }

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
        var jwtName = claims.FirstOrDefault(x => x.Type == "name")?.Value;
        claims.RemoveAll(x => x.Type == ClaimTypes.Name);
        if (!string.IsNullOrEmpty(displayName))
        {
            claims.Add(new Claim(ClaimTypes.Name, displayName));
        }
        else if (!string.IsNullOrEmpty(jwtName))
        {
            claims.Add(new Claim(ClaimTypes.Name, jwtName));
        }
        else if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.Name, userId));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return new ClaimsPrincipal(identity);
    }
}
