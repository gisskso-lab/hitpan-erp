using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.API.Extensions;

public static class AuthExtensions
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException("JWT_SECRET environment variable is required.");
        }

        // Production 강제 검증: JWT_SECRET 최소 32바이트(256비트) + 약한 기본값 차단
        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            if (jwtSecret.Length < 32)
            {
                throw new InvalidOperationException(
                    $"JWT_SECRET must be at least 32 characters in Production (current: {jwtSecret.Length}). " +
                    "Generate with: openssl rand -base64 64");
            }
            var weakDefaults = new[] { "secret", "changeme", "hitpan-default", "__CHANGE_ME__", "test" };
            foreach (var weak in weakDefaults)
            {
                if (jwtSecret.Contains(weak, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"JWT_SECRET contains a weak default marker '{weak}'. Replace with a strong random value.");
                }
            }
        }

        // Issuer/Audience — .env 또는 기본값 (토큰 스푸핑 방지)
        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "hitpan-erp";
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "hitpan-client";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // 보안 강화: issuer/audience 검증 활성화 (외부 토큰 차단)
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var t = ctx.Request.Query["token"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(t))
                        {
                            ctx.Token = t;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
