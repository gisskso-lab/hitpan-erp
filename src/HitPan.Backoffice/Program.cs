using HitPan.Backoffice.Components;
using HitPan.Backoffice.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;

namespace HitPan.Backoffice;

// 백오피스 진입점 (헌법 #35 정합 — 본사 클라우드 / 워크스페이스)
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(o =>
            {
                // 사고 진단용 — 회선 종료 시 정확한 예외 메시지 노출 (사장님 진단 요청 2026-06-16)
                o.DetailedErrors = true;
            });

        // 폼 POST 로그인용 컨트롤러 (Views 포함 — AntiForgery 필터 등록 위해)
        builder.Services.AddControllersWithViews();

        builder.Services.AddMudServices();

        // 인증·인가 — 백오피스 쿠키 (ERP JWT와 완전 분리, 헌법 #35)
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/backoffice/login";
                o.LogoutPath = "/backoffice/auth/signout";
                o.AccessDeniedPath = "/backoffice/login";
                o.Cookie.Name = "hitpan_bo";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
            });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("PlatformOnlyV2", p => p.RequireAuthenticatedUser()
                .RequireClaim("account_type", "platform_admin", "platform_owner"));
            o.AddPolicy("PlatformManagerOrAbove", p => p.RequireAuthenticatedUser()
                .RequireClaim("account_type", "platform_admin", "platform_owner"));
            // Owner 전용 (사장님 본인) — 본사 직원 관리·시스템 설정
            o.AddPolicy("OwnerOnly", p => p.RequireAuthenticatedUser()
                .RequireClaim("role", "owner"));
        });
        builder.Services.AddCascadingAuthenticationState();

        // HttpClient — 백오피스 API 호출 (헌법 #35 객체 완전 분리, 사장님 결재 2026-06-04)
        //   기본값 http://localhost:5258/ (백오피스 API). ERP API(5257)와 완전 분리.
        //   운영: 환경변수 HITPAN_BACKOFFICE_API_URL 우선, 그 다음 appsettings BackofficeApi:BaseUrl.
        var backofficeApi = Environment.GetEnvironmentVariable("HITPAN_BACKOFFICE_API_URL")
                          ?? builder.Configuration["BackofficeApi:BaseUrl"]
                          ?? "http://localhost:5258/";
        builder.Services.AddHttpClient<BackofficeService>(c => c.BaseAddress = new Uri(backofficeApi));
        // 협력업체 가입 신청 등 비인증 공개 API용 named client (사장님 결재 2026-06-04)
        builder.Services.AddHttpClient("backoffice-public", c => c.BaseAddress = new Uri(backofficeApi));

        // 인증이 필요한 백오피스 API 호출용 — 쿠키 access_token → Bearer 자동 첨부
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<JwtFromCookieHandler>();
        builder.Services.AddHttpClient("backoffice-authed", c => c.BaseAddress = new Uri(backofficeApi))
            .AddHttpMessageHandler<JwtFromCookieHandler>();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found");
        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapControllers();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
