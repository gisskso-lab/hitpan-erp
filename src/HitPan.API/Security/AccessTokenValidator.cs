using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.API.Security;

/// <summary>
/// Validates access JWTs (same rules as JwtBearer) for query-string flows (e.g. document downloads).
/// </summary>
public sealed class AccessTokenValidator
{
    private readonly TokenValidationParameters _parameters;

    public AccessTokenValidator()
    {
        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException("JWT_SECRET environment variable is required.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    public ClaimsPrincipal? ValidateAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _parameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwt
                || !string.Equals(jwt.Header.Alg, "HS256", StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(
                    principal.FindFirst("token_type")?.Value,
                    "refresh",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
