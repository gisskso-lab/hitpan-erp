using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

namespace HitPan.Backoffice.API;

// 백오피스 API 진입점 (헌법 #35 정합 — 본사 클라우드, ERP API와 완전 분리)
//
// 헌법 정합:
//   #18·#22 — 본사 DB만 박제 (고객 업무 데이터 0건)
//   #35 — ERP JWT와 별도 키·발급자·청취자
public class Program
{
    public static void Main(string[] args)
    {
        // QuestPDF Community 라이선스 (매출 1백만 달러 미만, 무료)
        QuestPDF.Settings.License = LicenseType.Community;

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.IEmailSender, HitPan.Backoffice.API.Services.EmailSender>();
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.IContractPdfGenerator, HitPan.Backoffice.API.Services.ContractPdfGenerator>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.IBoPermissionService, HitPan.Backoffice.API.Services.BoPermissionService>();
        // W10 webhook (사장님 결재 2026-06-04) — 구독·기기 변경 → ERP 동기화
        builder.Services.AddScoped<HitPan.Backoffice.API.Services.IWebhookOutboundService, HitPan.Backoffice.API.Services.WebhookOutboundService>();
        builder.Services.AddHostedService<HitPan.Backoffice.API.Services.WebhookDispatcher>();
        // W11 Owner 영역 (사장님 결재 2026-06-04) — bo_users + 4-eyes + 감사로그 + MFA
        builder.Services.AddScoped<HitPan.Backoffice.API.Services.IBoAuditService, HitPan.Backoffice.API.Services.BoAuditService>();
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.IMfaService, HitPan.Backoffice.API.Services.MfaService>();

        // JWT 인증 (백오피스 전용 — ERP와 분리)
        var jwt = builder.Configuration.GetSection("Jwt");
        var secret = jwt["Secret"] ?? "DEV-backoffice-secret-key-change-in-production-32+chars";
        var issuer = jwt["Issuer"] ?? "hitpan-backoffice";
        var audience = jwt["Audience"] ?? "backoffice";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
                };
            });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("PlatformAdmin", p => p.RequireClaim("account_type", "platform_admin", "platform_owner"));
            o.AddPolicy("Reseller", p => p.RequireClaim("account_type", "reseller_admin"));
            o.AddPolicy("Any", p => p.RequireAuthenticatedUser());
        });

        // CORS — HitPan.Backoffice(5291) + HitPan.Landing(5082) 허용
        //   - 랜딩은 Server-side Blazor라 서버측 HttpClient 호출이지만, 운영 환경 추가 안전망
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(p => p
                .WithOrigins(
                    "http://localhost:5291", "https://back.hitpan.kr",
                    "http://localhost:5082", "https://www.hitpan.kr")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        var app = builder.Build();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok", svc = "hitpan-backoffice-api" }));

        app.Run();
    }
}
