using Dapper;
using HitPan.Application.DTOs.Landing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

/// <summary>
/// 랜딩페이지 공개 API — 베타 신청 접수.
/// AllowAnonymous: tenant_id 없음, 멀티테넌트 불필요.
/// beta_signups 테이블은 INSERT ONLY (append-only).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LandingController : ControllerBase
{
    private readonly string _connStr;
    private readonly ILogger<LandingController> _logger;

    public LandingController(IConfiguration config, ILogger<LandingController> logger)
    {
        _connStr = config.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("DB 연결 문자열이 없습니다.");
        _logger = logger;
    }

    /// <summary>베타 신청 접수 — 랜딩페이지에서 호출. 공개 엔드포인트.</summary>
    [HttpPost("beta-signup")]
    public async Task<IActionResult> BetaSignup([FromBody] BetaSignupRequest request, CancellationToken ct)
    {
        // 필수 항목 검증
        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.ContactName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new BetaSignupResponse { Success = false, Message = "필수 항목을 모두 입력해주세요." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await using var db = new MySqlConnection(_connStr);
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO beta_signups (signup_id, company_name, contact_name, phone, email, employee_cnt, message, ip_address) " +
                "VALUES (UUID(), @CompanyName, @ContactName, @Phone, @Email, @EmployeeCnt, @Message, @Ip)",
                new
                {
                    request.CompanyName,
                    request.ContactName,
                    request.Phone,
                    request.Email,
                    request.EmployeeCnt,
                    request.Message,
                    Ip = ip
                },
                cancellationToken: ct));

            _logger.LogInformation("베타 신청 접수: {Company} / {Email}", request.CompanyName, request.Email);

            return Ok(new BetaSignupResponse { Success = true, Message = "베타 신청이 완료되었습니다. 빠르게 연락드리겠습니다!" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "베타 신청 저장 실패: {Company} / {Email}", request.CompanyName, request.Email);
            return StatusCode(500, new BetaSignupResponse { Success = false, Message = "서버 오류가 발생했습니다. 잠시 후 다시 시도해주세요." });
        }
    }
}
