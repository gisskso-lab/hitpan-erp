using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// MFA 등록·검증 (사장님 결재 2026-06-04, W11)
//
// 흐름:
//   1) POST /enroll-start — 시크릿 생성 + otpauth URI 반환 (QR 코드용)
//   2) POST /enroll-confirm — 코드 6자리 검증 + AES-256 박제 + 백업 코드 10개 반환
//   3) POST /verify — 로그인 후 2차 인증
//
// 헌법 정합:
//   #15·#18·#22 — 시크릿·백업코드는 평문 응답 1회만(등록 시), DB는 AES·BCrypt 박제
[ApiController]
[Route("api/backoffice/owner/mfa")]
[Authorize]
public class OwnerMfaController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<OwnerMfaController> _logger;
    private readonly IMfaService _mfa;
    private readonly IBoAuditService _audit;

    public OwnerMfaController(IConfiguration config, ILogger<OwnerMfaController> logger,
        IMfaService mfa, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _mfa = mfa;
        _audit = audit;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (userId, email, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<MfaStatusRow>(@"
                SELECT is_enabled AS IsEnabled, enrolled_at AS EnrolledAt, last_used_at AS LastUsedAt
                FROM bo_user_mfa WHERE user_id = @UserId", new { UserId = userId });
            return Ok(new
            {
                success = true,
                isEnrolled = row is not null,
                isEnabled = row?.IsEnabled == 1,
                enrolledAt = row?.EnrolledAt,
                lastUsedAt = row?.LastUsedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerMfa] 상태 조회 실패 user={Email}", email);
            return StatusCode(500, new { success = false, message = "상태 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("enroll-start")]
    public IActionResult EnrollStart()
    {
        var (_, email, _) = GetActor();
        try
        {
            var secret = _mfa.GenerateSecret();
            var uri = _mfa.BuildOtpAuthUri(secret, email);
            // 시크릿은 클라이언트가 enroll-confirm으로 다시 보내서 검증
            return Ok(new { success = true, secret, otpauthUri = uri });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerMfa] enroll-start 실패");
            return StatusCode(500, new { success = false, message = "MFA 시작 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("enroll-confirm")]
    public async Task<IActionResult> EnrollConfirm([FromBody] EnrollConfirmRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Secret) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { success = false, message = "시크릿·코드 필수" });
        if (!_mfa.Verify(req.Secret, req.Code))
            return BadRequest(new { success = false, message = "코드가 올바르지 않습니다." });

        var (userId, email, _) = GetActor();
        try
        {
            var encrypted = _mfa.Encrypt(req.Secret);
            var backupCodes = _mfa.GenerateBackupCodes();
            var hashes = backupCodes.Select(BCrypt.Net.BCrypt.HashPassword).ToArray();
            var hashesJoined = string.Join("\n", hashes);

            await using var db = await OpenAsync(ct);
            await db.ExecuteAsync(@"
                INSERT INTO bo_user_mfa
                    (user_id, secret_encrypted, is_enabled, enrolled_at, backup_codes_hash, created_at, updated_at)
                VALUES
                    (@UserId, @Secret, 1, NOW(6), @Backups, NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                    secret_encrypted = @Secret,
                    is_enabled = 1,
                    enrolled_at = NOW(6),
                    backup_codes_hash = @Backups,
                    updated_at = NOW(6)",
                new { UserId = userId, Secret = encrypted, Backups = hashesJoined });

            await _audit.LogAsync(userId, email, "owner", "mfa.enroll", "bo_user", userId,
                null, GetIp(), GetUa(), ct);

            return Ok(new
            {
                success = true,
                message = "MFA가 활성화되었습니다.",
                backupCodes,
                hint = "백업 코드는 1회만 표시됩니다. 안전한 곳에 박아두세요."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerMfa] enroll-confirm 실패");
            return StatusCode(500, new { success = false, message = "MFA 등록 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { success = false, message = "코드 필수" });

        var (userId, email, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<MfaSecretRow>(@"
                SELECT secret_encrypted AS SecretEncrypted, is_enabled AS IsEnabled
                FROM bo_user_mfa WHERE user_id = @UserId", new { UserId = userId });
            if (row is null || row.IsEnabled != 1)
                return BadRequest(new { success = false, message = "MFA가 활성화되어 있지 않습니다." });

            var secret = _mfa.Decrypt(row.SecretEncrypted);
            if (!_mfa.Verify(secret, req.Code))
                return Unauthorized(new { success = false, message = "코드가 올바르지 않습니다." });

            await db.ExecuteAsync(
                "UPDATE bo_user_mfa SET last_used_at = NOW(6) WHERE user_id = @UserId",
                new { UserId = userId });

            await _audit.LogAsync(userId, email, "owner", "mfa.verify_ok", "bo_user", userId,
                null, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "MFA 인증 통과" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerMfa] verify 실패");
            return StatusCode(500, new { success = false, message = "검증 중 오류가 발생했습니다." });
        }
    }

    private (string id, string email, string role) GetActor()
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var email = User.FindFirst("email")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? "";
        return (id, email, role);
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? GetUa()
    {
        var ua = Request.Headers["User-Agent"].ToString();
        return string.IsNullOrEmpty(ua) ? null : (ua.Length > 255 ? ua.Substring(0, 255) : ua);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    public record EnrollConfirmRequest(string Secret, string Code);
    public record VerifyRequest(string Code);

    private class MfaStatusRow
    {
        public int IsEnabled { get; set; }
        public DateTime? EnrolledAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }

    private class MfaSecretRow
    {
        public byte[] SecretEncrypted { get; set; } = Array.Empty<byte>();
        public int IsEnabled { get; set; }
    }
}
