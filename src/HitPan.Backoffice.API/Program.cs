using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

namespace HitPan.Backoffice.API;

// 백오피스 API 진입점 (헌법 #35 정합 — 본사 클라우드, ERP API와 완전 분리)
//
// 헌법 정합:
//   #18·#22 — 본사 DB만 저장 (고객 업무 데이터 0건)
//   #35 — ERP JWT와 별도 키·발급자·청취자
public class Program
{
    public static async Task Main(string[] args)
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
        // W14 Cloudflare 도메인 자동 발급 골격 (사장님 결재 2026-06-05) — 환경변수 미설정 시 503
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.ICloudflareDomainService, HitPan.Backoffice.API.Services.CloudflareDomainService>();
        // 도메인 별칭 검증 (사장님 결재 2026-06-09) — 고객 입력 ERP 주소 형식·예약어·중복 검사
        builder.Services.AddScoped<HitPan.Backoffice.API.Services.IDomainAliasService, HitPan.Backoffice.API.Services.DomainAliasService>();
        // W11 Owner 영역 (사장님 결재 2026-06-04) — bo_users + 4-eyes + 감사로그 + MFA
        builder.Services.AddScoped<HitPan.Backoffice.API.Services.IBoAuditService, HitPan.Backoffice.API.Services.BoAuditService>();
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.IMfaService, HitPan.Backoffice.API.Services.MfaService>();
        // W9 대리점 정산·시리얼 (사장님 결재 2026-06-04)
        builder.Services.AddScoped<HitPan.Backoffice.API.Services.IResellerSettlementCalculator,
                                   HitPan.Backoffice.API.Services.ResellerSettlementCalculator>();
        // 시리얼 공개키 서명 발급기 (작업지시서 20260707작1 ①단계, 사장님 승인 2026-07-07)
        //   - 부모계정 온보딩 증표를 HMAC(현행)에 더해 ECDSA P-256 공개키 서명으로 병행 발급.
        //   - 개인키 = HITPAN_SERIAL_SIGN_PRIVATE_KEY 환경변수(베타 서버격리). Singleton(개인키 PEM import 1회 재사용).
        builder.Services.AddSingleton<HitPan.Backoffice.API.Services.ISerialSignatureService,
                                      HitPan.Backoffice.API.Services.SerialSignatureService>();

        // JWT 인증 (백오피스 전용 — ERP와 분리)
        var jwt = builder.Configuration.GetSection("Jwt");
        // 보안 헌법 #23·#25: JWT Secret 환경변수/설정 누락 시 즉시 기동 중단 (fallback 시크릿 금지)
        var secret = Environment.GetEnvironmentVariable("HITPAN_BO_JWT_SECRET")
                    ?? jwt["Secret"]
                    ?? throw new InvalidOperationException("Jwt:Secret 미설정 — 환경변수 HITPAN_BO_JWT_SECRET 또는 appsettings Jwt:Secret 필수");
        if (secret.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jwt:Secret 개발 기본값 사용 금지 — 운영 시크릿으로 교체 필수");
        }
        var issuer = jwt["Issuer"] ?? "hitpan-backoffice";
        var audience = jwt["Audience"] ?? "backoffice";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
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

        // CORS — HitPan.Backoffice(5291) + HitPan.Landing(5082) + ERP demo + landing.hitpan.kr 허용
        //   - 브라운킴 PM 2026-06-08: ERP에서 시리얼 인증 API 호출용 demo.hitpan.kr 추가
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(p => p
                .WithOrigins(
                    "http://localhost:5234", "http://localhost:5291", "https://back.hitpan.kr",
                    "http://localhost:5082", "https://www.hitpan.kr", "https://landing.hitpan.kr",
                    "https://demo.hitpan.kr", "https://api-demo.hitpan.kr")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        var app = builder.Build();

        // P0 보안 봉합 (2026-06-11): HTTPS 강제 + HSTS
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }
        app.UseHttpsRedirection();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok", svc = "hitpan-backoffice-api" }));

        // 백오피스 자립형 스키마 자동 적용 (사장님 결재 2026-06-18 A안, 헌법 #29 정합)
        //   - 환경변수 HITPAN_BO_AUTO_MIGRATE=1 일 때만 동작(기본 off) — 사장님이 명시적으로 켜야 적용.
        //   - installer/backoffice/*.sql 을 번호순·멱등 적용. CREATE DATABASE·DB 인스턴스는 사장님 영역.
        //   - 실패 시 SchemaMigrator가 예외를 던져 기동 중단(스키마 불일치 런타임 500 잠복 방지, #15·#19).
        if (Environment.GetEnvironmentVariable("HITPAN_BO_AUTO_MIGRATE") == "1")
        {
            var migrateCs = app.Configuration.GetConnectionString("BackofficeDb")
                ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정 — 스키마 적용 불가");
            // installer/backoffice 는 레포 기준 경로. 배포 환경에서는 HITPAN_BO_SCHEMA_DIR 로 재지정 가능.
            var scriptsDir = Environment.GetEnvironmentVariable("HITPAN_BO_SCHEMA_DIR")
                ?? Path.Combine(AppContext.BaseDirectory, "installer", "backoffice");
            var migLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<HitPan.Backoffice.API.Services.SchemaMigrator>();
            var migrator = new HitPan.Backoffice.API.Services.SchemaMigrator(migrateCs, scriptsDir, migLogger);
            await migrator.ApplyAsync();
        }

        app.Run();
    }
}
