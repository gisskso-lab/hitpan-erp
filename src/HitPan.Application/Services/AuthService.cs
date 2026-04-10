using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.Application.Services;

public class AuthService : IAuthService
{
    private const string InvalidCredentialMessage = "이메일 또는 비밀번호가 틀립니다";
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthUserLookup _authUserLookup;

    public AuthService(IUnitOfWork unitOfWork, IAuthUserLookup authUserLookup)
    {
        _unitOfWork = unitOfWork;
        _authUserLookup = authUserLookup;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _authUserLookup.FindUserByEmailAsync(request.Email, ct);

        if (user is null)
        {
            throw new UnauthorizedAccessException(InvalidCredentialMessage);
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
        {
            throw new UnauthorizedAccessException(InvalidCredentialMessage);
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("비활성화된 계정입니다");
        }

        var redirectToWelcome = user.LastLoginAt is null;
        user.LastLoginAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("JWT_SECRET environment variable is required.");
        }

        return CreateLoginResponse(user, secret, redirectToWelcome);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("JWT_SECRET environment variable is required.");
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        ClaimsPrincipal principal;
        try
        {
            principal = tokenHandler.ValidateToken(
                request.RefreshToken,
                new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                },
                out _);
        }
        catch
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var tokenType = principal.FindFirst("token_type")?.Value;
        if (!string.Equals(tokenType, "refresh", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var userId = principal.FindFirst("user_id")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var user = await _authUserLookup.FindUserByIdAsync(userId, ct);
        if (user is null)
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        return CreateLoginResponse(user, secret, redirectToWelcome: false);
    }

    private static LoginResponse CreateLoginResponse(User user, string secret, bool redirectToWelcome)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var accessExpiresAt = now.Add(AccessTokenLifetime);

        var accessClaims = new List<Claim>
        {
            new("tenant_id", user.TenantId),
            new("user_id", user.Id),
            new("name", user.UserName),
            new("role", user.Role.ToString())
        };

        var accessTokenDescriptor = new JwtSecurityToken(
            claims: accessClaims,
            expires: accessExpiresAt,
            signingCredentials: credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(accessTokenDescriptor);

        var refreshClaims = new List<Claim>
        {
            new("user_id", user.Id),
            new("token_type", "refresh")
        };

        var refreshTokenDescriptor = new JwtSecurityToken(
            claims: refreshClaims,
            expires: now.Add(RefreshTokenLifetime),
            signingCredentials: credentials);
        var refreshToken = new JwtSecurityTokenHandler().WriteToken(refreshTokenDescriptor);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = accessExpiresAt,
            TenantId = user.TenantId,
            UserName = user.UserName,
            Role = user.Role.ToString(),
            RedirectToWelcome = redirectToWelcome
        };
    }
}
