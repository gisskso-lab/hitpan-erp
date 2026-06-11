using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 백오피스 인증 API (헌법 #35 정합 — ERP API와 완전 분리)
//
// 가벼운 박제 (B안 — 사장님 결재 2026-06-04):
//   - Dapper로 platform_admins·resellers 테이블 직접 쿼리
//   - BCrypt 비밀번호 검증
//   - JWT 발급 (백오피스 전용 키 · aud=backoffice)
//   - 다음 단계: hitpan_backoffice DB 분리 후 ConnectionString만 교체
[ApiController]
[Route("api/backoffice/auth")]
public class BackofficeAuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<BackofficeAuthController> _logger;

    public BackofficeAuthController(IConfiguration config, ILogger<BackofficeAuthController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("admin/login")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminLogin([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { success = false, message = "이메일·비밀번호 필수" });

        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<AdminRow>(
                @"SELECT CAST(admin_id AS CHAR) AS AdminId, email AS Email, password_hash AS PasswordHash,
                         admin_name AS AdminName, role AS Role, is_active AS IsActive
                  FROM platform_admins WHERE email = @Email LIMIT 1",
                new { req.Email });

            if (row is null || row.IsActive != 1 || !BCrypt.Net.BCrypt.Verify(req.Password, row.PasswordHash))
            {
                _logger.LogInformation("[BackofficeAuth] admin login failed email={Email}", req.Email);
                return Unauthorized(new { success = false, message = "이메일 또는 비밀번호가 올바르지 않습니다" });
            }

            var (accessToken, refreshToken, expiresAt) = IssueTokens(new Dictionary<string, string?>
            {
                ["sub"] = row.AdminId,
                ["name"] = row.AdminName,
                ["email"] = row.Email,
                ["account_type"] = "platform_admin",
                ["role"] = row.Role
            });

            return Ok(new
            {
                success = true,
                data = new
                {
                    accessToken,
                    refreshToken,
                    expiresAt,
                    role = row.Role,
                    adminName = row.AdminName,
                    adminId = row.AdminId,
                    accountType = "platform_admin"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BackofficeAuth] admin login error email={Email}", req.Email);
            return StatusCode(500, new { success = false, message = "서버 오류" });
        }
    }

    [HttpPost("reseller/login")]
    [AllowAnonymous]
    public async Task<IActionResult> ResellerLogin([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { success = false, message = "이메일·비밀번호 필수" });

        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<ResellerRow>(
                @"SELECT CAST(ra.account_id AS CHAR) AS AccountId, ra.email AS Email, ra.password_hash AS PasswordHash,
                         ra.account_name AS AccountName, ra.role AS Role, ra.is_active AS IsActive,
                         CAST(r.reseller_id AS CHAR) AS ResellerId, r.reseller_name AS ResellerName
                  FROM reseller_accounts ra
                  INNER JOIN resellers r ON r.reseller_id = ra.reseller_id
                  WHERE ra.email = @Email LIMIT 1",
                new { req.Email });

            if (row is null || row.IsActive != 1 || !BCrypt.Net.BCrypt.Verify(req.Password, row.PasswordHash))
            {
                _logger.LogInformation("[BackofficeAuth] reseller login failed email={Email}", req.Email);
                return Unauthorized(new { success = false, message = "이메일 또는 비밀번호가 올바르지 않습니다" });
            }

            var (accessToken, refreshToken, expiresAt) = IssueTokens(new Dictionary<string, string?>
            {
                ["sub"] = row.AccountId,
                ["name"] = row.AccountName,
                ["email"] = row.Email,
                ["account_type"] = "reseller_admin",
                ["role"] = row.Role,
                ["reseller_id"] = row.ResellerId
            });

            return Ok(new
            {
                success = true,
                data = new
                {
                    accessToken,
                    refreshToken,
                    expiresAt,
                    role = row.Role,
                    accountName = row.AccountName,
                    accountId = row.AccountId,
                    resellerId = row.ResellerId,
                    resellerName = row.ResellerName,
                    accountType = "reseller_admin"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BackofficeAuth] reseller login error email={Email}", req.Email);
            return StatusCode(500, new { success = false, message = "서버 오류" });
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        // 헌법 #35 — 백오피스 DB 분리 (사장님 결재 2026-06-04):
        //   - 로컬 개발: BackofficeDb = hitpan_erp 동일 가리킴 (편의)
        //   - 클라우드 배포: BackofficeDb만 hitpan_backoffice 별도 DB로 교체
        //   - 코드 변경 0, ConnectionString만 교체
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private (string accessToken, string refreshToken, DateTime expiresAt) IssueTokens(Dictionary<string, string?> claimsMap)
    {
        var jwt = _config.GetSection("Jwt");
        // 보안 헌법 #23·#25: JWT Secret 환경변수/설정 누락 시 즉시 예외 (fallback 시크릿 금지)
        var secret = Environment.GetEnvironmentVariable("HITPAN_BO_JWT_SECRET")
                    ?? jwt["Secret"]
                    ?? throw new InvalidOperationException("Jwt:Secret 미설정");
        if (secret.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jwt:Secret 개발 기본값 사용 금지");
        }
        var issuer = jwt["Issuer"] ?? "hitpan-backoffice";
        var audience = jwt["Audience"] ?? "backoffice";
        var expHours = int.TryParse(jwt["ExpiresHours"], out var h) ? h : 8;
        var expiresAt = DateTime.UtcNow.AddHours(expHours);

        var claims = claimsMap
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => new Claim(kv.Key, kv.Value!))
            .ToList();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expiresAt, signingCredentials: creds);
        var access = new JwtSecurityTokenHandler().WriteToken(token);

        // refresh: 단순 GUID (다음 단계 — DB 박제 + 만료·재발급)
        var refresh = Guid.NewGuid().ToString("N");
        return (access, refresh, expiresAt);
    }

    public record LoginRequest(string Email, string Password);

    private class AdminRow
    {
        public string AdminId { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string AdminName { get; set; } = "";
        public string Role { get; set; } = "";
        public int IsActive { get; set; }
    }

    private class ResellerRow
    {
        public string AccountId { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Role { get; set; } = "";
        public int IsActive { get; set; }
        public string ResellerId { get; set; } = "";
        public string ResellerName { get; set; } = "";
    }
}
