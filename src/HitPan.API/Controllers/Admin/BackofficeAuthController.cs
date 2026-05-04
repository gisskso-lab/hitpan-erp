using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/backoffice/auth")]
public class BackofficeAuthController : ControllerBase
{
    private readonly IBackofficeAuthService _authService;
    private readonly ILogger<BackofficeAuthController> _logger;

    public BackofficeAuthController(IBackofficeAuthService authService, ILogger<BackofficeAuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("admin/login")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _authService.AdminLoginAsync(request, ct);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BackofficeAuth] 본사 로그인 실패 — {Email}", request.Email);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("reseller/login")]
    [AllowAnonymous]
    public async Task<IActionResult> ResellerLogin([FromBody] ResellerLoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _authService.ResellerLoginAsync(request, ct);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BackofficeAuth] 대리점 로그인 실패 — {Email}", request.Email);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] BackofficeRefreshRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _authService.RefreshAsync(request, ct);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BackofficeAuth] 토큰 갱신 실패");
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
