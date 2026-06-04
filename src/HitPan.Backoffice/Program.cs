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
            .AddInteractiveServerComponents();

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
        });
        builder.Services.AddCascadingAuthenticationState();

        // HttpClient — ERP API 호출 (헌법 #35: 백오피스가 ERP API로 인증 위임 — 다음 단계에 별도 백오피스 API로 전환)
        var backofficeApi = builder.Configuration["BackofficeApi:BaseUrl"] ?? "http://localhost:5257/";
        builder.Services.AddHttpClient<BackofficeService>(c => c.BaseAddress = new Uri(backofficeApi));

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
