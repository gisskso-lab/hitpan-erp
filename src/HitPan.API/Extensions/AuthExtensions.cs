using System.Text;
using HitPan.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.API.Extensions;

public static class AuthExtensions
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        // 사고 #46 봉합 (WS-20260612-01 2026-06-12): TenantConfigReader 영역 db.conf 영역
        //   회사별 hitpan-keys.conf 영역 JWT_SECRET 박힘 → 회사별 영역 완전 분리
        var jwtSecret = TenantConfigReader.Get("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException("JWT_SECRET 영역 0건 — hitpan-keys.conf 또는 환경변수 영역 박혀야 함");
        }

        // Production 강제 검증: JWT_SECRET 최소 32바이트(256비트) + 약한 기본값 차단
        var envName = TenantConfigReader.Get("ASPNETCORE_ENVIRONMENT") ?? "Production";
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

        // Issuer/Audience — db.conf·hitpan-keys.conf·환경변수 영역 폴백 (토큰 스푸핑 방지)
        var jwtIssuer = TenantConfigReader.Get("JWT_ISSUER") ?? "hitpan-erp";
        var jwtAudience = TenantConfigReader.Get("JWT_AUDIENCE") ?? "hitpan-client";

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
                        // 봉합 2026-05-14: SignalR JS 클라이언트 표준 access_token + 기존 token 둘 다 수용
                        // SignalR WebSocket 업그레이드 시 헤더 못 실어 ?access_token=...로 옴 (헌법 #26 봉합)
                        var t = ctx.Request.Query["access_token"].FirstOrDefault();
                        if (string.IsNullOrEmpty(t))
                        {
                            t = ctx.Request.Query["token"].FirstOrDefault();
                        }
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
