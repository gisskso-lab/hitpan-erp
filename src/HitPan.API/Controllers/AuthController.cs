using HitPan.Application.DTOs.Auth;
using HitPan.Application.DTOs.Device;
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
    private readonly ITenantDeviceService _deviceService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IHrService hrService,
        ITenantDeviceService deviceService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _hrService = hrService;
        _deviceService = deviceService;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _authService.LoginAsync(request, ct);

            // ── 기기 기반 라이선싱: 로그인 성공 후 기기 등록/갱신 ──
            // - fingerprint 없으면 스킵 (기존 클라이언트 호환)
            // - 한도 초과면 로그인 거부 (Unauthorized)
            if (!string.IsNullOrEmpty(response.TenantId) && !string.IsNullOrEmpty(request.DeviceFingerprint))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
                        var ua = request.DeviceName; // 편의상 UA를 DeviceName 필드에 담아 보낼 수도 있음
                        var deviceReq = new RegisterDeviceRequest
                        {
                            Fingerprint = request.DeviceFingerprint!,
                            DeviceType = request.DeviceType ?? "pc",
                            DeviceName = request.DeviceName,
                            UserAgent = Request.Headers["User-Agent"].ToString()
                        };

                        var (allowed, reason, deviceId) = await _deviceService.RegisterOrRefreshAsync(
                            response.TenantId, userId, deviceReq, ipAddress, ct);

                        if (!allowed)
                        {
                            // 기기 한도 초과 등 → 로그인 거부 (명확한 한국어 메시지)
                            return Unauthorized(new { message = reason });
                        }

                        response.DeviceId = deviceId;
                    }
                }
                catch (Exception ex)
                {
                    // 기기 등록 로직 자체가 실패해도 로그인 흐름은 막지 않음(운영 추적만)
                    _logger.LogWarning(ex, "기기 등록/갱신 실패 — TenantId: {TenantId}", response.TenantId);
                }
            }

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
                catch (Exception ex)
                {
                    // 출근 기록 실패는 로그인 자체를 막지 않지만 운영 추적을 위해 로그 남김
                    _logger.LogWarning(ex, "자동 출근 기록 실패 — TenantId: {TenantId}", response.TenantId);
                }
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
            catch (Exception ex) { _logger.LogWarning(ex, "자동 퇴근 기록 실패 — UserId: {UserId}", userId); }

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
            catch (Exception ex) { _logger.LogWarning(ex, "로그아웃 세션/토큰 정리 실패 — UserId: {UserId}", userId); }
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
