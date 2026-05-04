using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.Application.Services;

public class BackofficeAuthService : IBackofficeAuthService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BackofficeAuthService> _logger;

    public BackofficeAuthService(IUnitOfWork unitOfWork, ILogger<BackofficeAuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AdminLoginResponse> AdminLoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var admin = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT admin_id, email, password_hash, admin_name, role, is_active
              FROM platform_admins
              WHERE email = @Email LIMIT 1",
            new { request.Email });

        if (admin is null)
            throw new UnauthorizedAccessException("이메일 또는 비밀번호가 올바르지 않습니다");

        if (!(bool)admin.is_active)
            throw new UnauthorizedAccessException("비활성화된 계정입니다");

        var valid = BCrypt.Net.BCrypt.Verify(request.Password, admin.password_hash.ToString());
        if (!valid)
            throw new UnauthorizedAccessException("이메일 또는 비밀번호가 올바르지 않습니다");

        string adminId = admin.admin_id.ToString();
        string adminName = admin.admin_name.ToString();
        string adminRole = admin.role.ToString();

        await db.ExecuteAsync(
            "UPDATE platform_admins SET last_login_at = @Now WHERE admin_id = @AdminId",
            new { Now = DateTime.UtcNow, AdminId = adminId });

        var (accessToken, refreshToken, expiresAt) = CreateTokenPair(
            sub: adminId,
            accountType: "platform_admin",
            role: adminRole,
            extraClaims: new Dictionary<string, string>
            {
                ["admin_id"] = adminId,
                ["name"] = adminName
            });

        await SaveRefreshTokenAsync(db, adminId, "admin", refreshToken);

        return new AdminLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            Role = adminRole,
            AdminName = adminName,
            AdminId = adminId,
            AccountType = "platform_admin"
        };
    }

    public async Task<ResellerLoginResponse> ResellerLoginAsync(ResellerLoginRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var account = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT a.account_id, a.email, a.password_hash, a.account_name, a.role,
                     a.is_active, a.reseller_id,
                     r.reseller_name, r.status AS reseller_status
              FROM reseller_accounts a
              JOIN resellers r ON r.reseller_id = a.reseller_id
              WHERE a.email = @Email LIMIT 1",
            new { request.Email });

        if (account is null)
            throw new UnauthorizedAccessException("이메일 또는 비밀번호가 올바르지 않습니다");

        if (!(bool)account.is_active)
            throw new UnauthorizedAccessException("비활성화된 계정입니다");

        string resellerStatus = account.reseller_status.ToString();
        if (resellerStatus is "suspended" or "terminated")
            throw new UnauthorizedAccessException("소속 대리점이 현재 이용 중지 상태입니다");

        var valid = BCrypt.Net.BCrypt.Verify(request.Password, account.password_hash.ToString());
        if (!valid)
            throw new UnauthorizedAccessException("이메일 또는 비밀번호가 올바르지 않습니다");

        string accountId = account.account_id.ToString();
        string accountName = account.account_name.ToString();
        string accountRole = account.role.ToString();
        string resellerId = account.reseller_id.ToString();
        string resellerName = account.reseller_name.ToString();

        await db.ExecuteAsync(
            "UPDATE reseller_accounts SET last_login_at = @Now WHERE account_id = @AccountId",
            new { Now = DateTime.UtcNow, AccountId = accountId });

        var (accessToken, refreshToken, expiresAt) = CreateTokenPair(
            sub: accountId,
            accountType: "reseller_admin",
            role: accountRole,
            extraClaims: new Dictionary<string, string>
            {
                ["account_id"] = accountId,
                ["reseller_id"] = resellerId,
                ["name"] = accountName
            });

        await SaveRefreshTokenAsync(db, accountId, "reseller", refreshToken);

        return new ResellerLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            Role = accountRole,
            AccountName = accountName,
            AccountId = accountId,
            ResellerId = resellerId,
            ResellerName = resellerName,
            AccountType = "reseller_admin"
        };
    }

    public async Task<BackofficeRefreshResponse> RefreshAsync(BackofficeRefreshRequest request, CancellationToken ct = default)
    {
        var secret = GetJwtSecret();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        ClaimsPrincipal principal;
        try
        {
            var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "hitpan-erp";
            var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "hitpan-client";

            principal = new JwtSecurityTokenHandler().ValidateToken(
                request.RefreshToken,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                },
                out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "백오피스 refresh token 검증 실패");
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var sub = principal.FindFirst("sub")?.Value
                  ?? throw new UnauthorizedAccessException("토큰 정보가 없습니다");
        var accountType = principal.FindFirst("account_type")?.Value;
        var role = principal.FindFirst("role")?.Value ?? "";

        var db = _unitOfWork.GetDbConnection();
        var isRevoked = await db.QueryFirstOrDefaultAsync<bool?>(
            "SELECT is_revoked FROM backoffice_refresh_tokens WHERE user_id = @Sub AND token_hash = @Token",
            new { Sub = sub, Token = request.RefreshToken });

        if (isRevoked is null or true)
            throw new UnauthorizedAccessException("만료된 토큰입니다");

        Dictionary<string, string> extraClaims;
        if (accountType == "platform_admin")
        {
            var adminName = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT admin_name FROM platform_admins WHERE admin_id = @Id", new { Id = sub }) ?? "";
            extraClaims = new Dictionary<string, string> { ["admin_id"] = sub, ["name"] = adminName };
        }
        else
        {
            var resellerId = principal.FindFirst("reseller_id")?.Value ?? "";
            var accountName = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT account_name FROM reseller_accounts WHERE account_id = @Id", new { Id = sub }) ?? "";
            extraClaims = new Dictionary<string, string>
            {
                ["account_id"] = sub,
                ["reseller_id"] = resellerId,
                ["name"] = accountName
            };
        }

        var (accessToken, _, expiresAt) = CreateTokenPair(sub, accountType ?? "platform_admin", role, extraClaims);

        return new BackofficeRefreshResponse { AccessToken = accessToken, ExpiresAt = expiresAt };
    }

    // ── 내부 헬퍼 ──────────────────────────────────────────────────────────────

    private (string AccessToken, string RefreshToken, DateTime ExpiresAt) CreateTokenPair(
        string sub, string accountType, string role, Dictionary<string, string> extraClaims)
    {
        var secret = GetJwtSecret();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "hitpan-erp";
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "hitpan-client";
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(AccessTokenLifetime);

        var accessClaims = new List<Claim>
        {
            new("sub", sub),
            new("account_type", accountType),
            new("role", role),
            new(ClaimTypes.Role, role)
        };
        foreach (var (k, v) in extraClaims)
            accessClaims.Add(new Claim(k, v));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer, audience, accessClaims, expires: expiresAt, signingCredentials: credentials));

        var refreshClaims = new List<Claim>
        {
            new("sub", sub),
            new("account_type", accountType),
            new("role", role),
            new("token_type", "refresh")
        };
        foreach (var (k, v) in extraClaims)
            refreshClaims.Add(new Claim(k, v));

        var refreshToken = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer, audience, refreshClaims,
                expires: now.Add(RefreshTokenLifetime), signingCredentials: credentials));

        return (accessToken, refreshToken, expiresAt);
    }

    private static async Task SaveRefreshTokenAsync(
        System.Data.IDbConnection db, string userId, string userType, string token)
    {
        await db.ExecuteAsync(
            "DELETE FROM backoffice_refresh_tokens WHERE user_id = @UserId",
            new { UserId = userId });

        await db.ExecuteAsync(
            @"INSERT INTO backoffice_refresh_tokens
                (token_id, user_id, user_type, token_hash, expires_at, is_revoked, created_at)
              VALUES
                (@TokenId, @UserId, @UserType, @TokenHash, @ExpiresAt, 0, @Now)",
            new
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = userId,
                UserType = userType,
                TokenHash = token,
                ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
                Now = DateTime.UtcNow
            });
    }

    private static string GetJwtSecret()
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("JWT_SECRET environment variable is required.");
        return secret;
    }
}
