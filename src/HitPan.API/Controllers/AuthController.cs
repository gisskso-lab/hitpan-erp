using HitPan.Application.DTOs.Auth;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IHrService _hrService;

    public AuthController(IAuthService authService, IHrService hrService)
    {
        _authService = authService;
        _hrService = hrService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _authService.LoginAsync(request, ct);

            // 로그인 성공 시 자동 출근 기록 (중복 출근은 서비스에서 무시)
            if (!string.IsNullOrEmpty(response.TenantId))
            {
                try
                {
                    // JWT에서 user_id 추출
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        await _hrService.CheckInAsync(response.TenantId, userId,
                            new HitPan.Application.DTOs.Employee.CheckInOutRequest { Memo = "자동출근" }, ct);
                    }
                }
                catch { /* 출근 기록 실패해도 로그인은 성공 */ }
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>로그아웃 + 자동 퇴근 + 세션 정리</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(userId))
        {
            // 자동 퇴근 기록
            try { await _hrService.CheckOutAsync(tenantId, userId, ct); }
            catch { }

            // 세션 + refresh_token 정리
            try
            {
                var db = HttpContext.RequestServices.GetRequiredService<System.Data.IDbConnection>();
                if (db.State != System.Data.ConnectionState.Open)
                {
                    if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
                    else db.Open();
                }
                await Dapper.SqlMapper.ExecuteAsync(db,
                    "DELETE FROM refresh_tokens WHERE user_id = @UserId",
                    new { UserId = userId });
                await Dapper.SqlMapper.ExecuteAsync(db,
                    "DELETE FROM user_sessions WHERE user_id = @UserId",
                    new { UserId = userId });
            }
            catch { }
        }

        return Ok(new { message = "로그아웃 완료" });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _authService.RefreshAsync(request, ct);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }
}
