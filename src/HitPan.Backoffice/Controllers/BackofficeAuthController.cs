using System.Security.Claims;
using HitPan.Backoffice.Services;
using Microsoft.AspNetCore.Antiforgery;
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
    private readonly IAntiforgery _antiforgery;

    public BackofficeAuthController(BackofficeService bo, ILogger<BackofficeAuthController> logger, IAntiforgery antiforgery)
    {
        _bo = bo;
        _logger = logger;
        _antiforgery = antiforgery;
    }

    // 봉합 2026-07-07 (사장님 결재): antiforgery 토큰 만료/무효 시 400 dead-end 대신 로그인 재진입.
    //   [ValidateAntiForgeryToken] 속성 자동검증(→400)을 제거하고 수동 ValidateRequestAsync 로 대체.
    //   만료(AntiforgeryValidationException)만 부드럽게 로그인으로 리다이렉트 → 새 토큰 발급받아 재시도.
    //   CSRF 방어는 유지: 위조·누락 토큰도 동일 예외로 잡혀 처리 안 되고 로그인으로 반려(무한 우회 없음).
    [HttpPost("signin")]
    public async Task<IActionResult> SignIn(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        try { await _antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException)
        {
            _logger.LogInformation("[BackofficeAuth] antiforgery 만료/무효 → 로그인 재진입");
            return Redirect("/backoffice/login?error=" +
                Uri.EscapeDataString("보안 세션이 만료되었습니다. 다시 로그인해 주세요."));
        }

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
    public async Task<IActionResult> SignOutAsync()
    {
        // 봉합 2026-07-07: signin 과 동일 — 만료 토큰 400 대신 우아하게 로그인으로.
        try { await _antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException)
        {
            return LocalRedirect("/backoffice/login");
        }
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
