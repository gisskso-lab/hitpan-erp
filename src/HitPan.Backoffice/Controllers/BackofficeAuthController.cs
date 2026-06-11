using System.Security.Claims;
using HitPan.Backoffice.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.Controllers;

// 백오피스 인증 컨트롤러 (헌법 #35 정합 — 백오피스 자체 인증, ERP JWT와 분리)
//
// 흐름:
//   POST /backoffice/auth/signin (email, password, returnUrl)
//      → BackofficeService.AdminLoginAsync / ResellerLoginAsync (ERP API 호출)
//      → 성공 시 Cookie SignInAsync (백오피스 쿠키)
//      → returnUrl 또는 /admin/dashboard 리다이렉트
//
// 헌법 정합:
//   #15 빈 catch 금지, ILogger 저장
//   #18·#22 평문 사업자정보 0건, 토큰만 저장
//   #35 백오피스 자체 인증, ERP와 완전 분리
[Route("backoffice/auth")]
public class BackofficeAuthController : Controller
{
    private readonly BackofficeService _bo;
    private readonly ILogger<BackofficeAuthController> _logger;

    public BackofficeAuthController(BackofficeService bo, ILogger<BackofficeAuthController> logger)
    {
        _bo = bo;
        _logger = logger;
    }

    [HttpPost("signin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Redirect($"/backoffice/login?error={Uri.EscapeDataString("이메일·비밀번호를 입력하세요")}");

        // 본사 관리자 우선 시도 → 실패 시 대리점
        var admin = await _bo.AdminLoginAsync(email, password, ct);
        if (admin.Success && admin.Data is not null)
        {
            await SignInWithClaimsAsync(new Dictionary<string, string?>
            {
                [ClaimTypes.NameIdentifier] = admin.Data.AdminId,
                [ClaimTypes.Name] = admin.Data.AdminName,
                [ClaimTypes.Email] = email,
                ["account_type"] = admin.Data.AccountType,
                ["role"] = admin.Data.Role,
                ["access_token"] = admin.Data.AccessToken,
                ["refresh_token"] = admin.Data.RefreshToken
            }, admin.Data.ExpiresAt);

            _logger.LogInformation("[BackofficeAuth] admin signed in email={Email}", email);
            return LocalRedirect(SafeReturn(returnUrl, "/admin/dashboard"));
        }

        var reseller = await _bo.ResellerLoginAsync(email, password, ct);
        if (reseller.Success && reseller.ResellerData is not null)
        {
            await SignInWithClaimsAsync(new Dictionary<string, string?>
            {
                [ClaimTypes.NameIdentifier] = reseller.ResellerData.AccountId,
                [ClaimTypes.Name] = reseller.ResellerData.AccountName,
                [ClaimTypes.Email] = email,
                ["account_type"] = reseller.ResellerData.AccountType,
                ["role"] = reseller.ResellerData.Role,
                ["reseller_id"] = reseller.ResellerData.ResellerId,
                ["access_token"] = reseller.ResellerData.AccessToken,
                ["refresh_token"] = reseller.ResellerData.RefreshToken
            }, reseller.ResellerData.ExpiresAt);

            _logger.LogInformation("[BackofficeAuth] reseller signed in email={Email}", email);
            return LocalRedirect(SafeReturn(returnUrl, "/admin/dashboard"));
        }

        var msg = admin.Message ?? reseller.Message ?? "이메일 또는 비밀번호가 올바르지 않습니다";
        _logger.LogWarning("[BackofficeAuth] login failed email={Email} msg={Msg}", email, msg);
        return Redirect($"/backoffice/login?error={Uri.EscapeDataString(msg)}");
    }

    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect("/backoffice/login");
    }

    private async Task SignInWithClaimsAsync(Dictionary<string, string?> claimsMap, DateTime expiresAt)
    {
        var claims = claimsMap
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => new Claim(kv.Key, kv.Value!))
            .ToList();

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = expiresAt > DateTime.UtcNow ? expiresAt : DateTime.UtcNow.AddHours(8)
            });
    }

    private static string SafeReturn(string? returnUrl, string fallback)
    {
        if (string.IsNullOrEmpty(returnUrl)) return fallback;
        // 외부 URL 차단
        if (returnUrl.StartsWith("/") && !returnUrl.StartsWith("//")) return returnUrl;
        return fallback;
    }
}
