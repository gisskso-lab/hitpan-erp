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
    private const string CurrentTermsVersion = "v2.0.0";

    private readonly IAuthService _authService;
    private readonly IHrService _hrService;
    private readonly ITenantDeviceService _deviceService;
    private readonly ITermsConsentService _termsConsentService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IHrService hrService,
        ITenantDeviceService deviceService,
        ITermsConsentService termsConsentService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _hrService = hrService;
        _deviceService = deviceService;
        _termsConsentService = termsConsentService;
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
                    // 봉합 (2026-06-22, 13차 2단 교차검증 — HR employee_id 부분봉합 회귀): HrController 4곳을
                    //   employee_id 로 바꿨으나 같은 HrService 를 호출하는 자동출/퇴근(AuthController)을 누락하면
                    //   자동출근은 user_id 키, 수동출근은 employee_id 키로 attendance 이중행·CheckOut 미스가 난다
                    //   (헌법 #12 구현체 전수 누락, 11차 반품사유와 동형). JWT employee_id 클레임으로 통일.
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var employeeId = jwt.Claims.FirstOrDefault(c => c.Type == "employee_id")?.Value;
                    if (!string.IsNullOrEmpty(employeeId))
                    {
                        await _hrService.CheckInAsync(response.TenantId, employeeId,
                            new HitPan.Application.DTOs.Employee.CheckInOutRequest { Memo = "자동출근" }, ct);
                    }
                }
                catch (Exception ex)
                {
                    // 출근 기록 실패는 로그인 자체를 막지 않지만 운영 추적을 위해 로그 남김
                    _logger.LogWarning(ex, "자동 출근 기록 실패 — TenantId: {TenantId}", response.TenantId);
                }
            }

            // 헌법 #24: 약관 v2.0.0 동의 여부 저장 (미동의 시 /terms 강제 이동)
            if (!string.IsNullOrEmpty(response.TenantId))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var hasAgreed = await _termsConsentService.HasAgreedAsync(
                            response.TenantId, userId, CurrentTermsVersion, ct);
                        response.RequiresTermsConsent = !hasAgreed;
                    }
                }
                catch (Exception ex)
                {
                    // 테이블 부재 등 인프라 사고 = 동의 강제 보류 (베타 1주차 정합, 헌법 #15 silent swallow 금지)
                    _logger.LogWarning(ex, "약관 동의 상태 조회 실패 — TenantId: {TenantId}", response.TenantId);
                    response.RequiresTermsConsent = false;
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
        // 봉합 (2026-06-22, 13차 2단 교차검증): 자동퇴근도 employee_id 로 통일(자동출근·HrController 정합).
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(userId))
        {
            // 자동 퇴근 기록 — employee_id 키로 자동출근행과 매칭(없으면 무시, 로그인/로그아웃 자체는 불방해)
            if (!string.IsNullOrEmpty(employeeId))
            {
                try { await _hrService.CheckOutAsync(tenantId, employeeId, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "자동 퇴근 기록 실패 — EmployeeId: {EmployeeId}", employeeId); }
            }

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
                    "UPDATE refresh_tokens SET is_revoked = 1 WHERE user_id = @UserId",
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

    /// <summary>
    /// Step-up 인증: 현재 로그인한 어드민 본인 비밀번호 재검증.
    /// WO-20260430-9 (사장님 검증 발견 — 사용자 정보 수정 등 민감 작업 보호용).
    /// 통과 시 200, 실패 시 401. 5분 캐싱은 후속 작업.
    /// </summary>
    [HttpPost("verify-password")]
    [Authorize]
    public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request, CancellationToken ct)
    {
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(request?.Password))
        {
            return Unauthorized(new { message = "인증 정보가 부족합니다." });
        }

        var ok = await _authService.VerifyOwnPasswordAsync(userId, request.Password, ct);
        return ok ? Ok(new { verified = true })
                  : Unauthorized(new { message = "비밀번호가 일치하지 않습니다." });
    }
}

public sealed record VerifyPasswordRequest(string Password);
