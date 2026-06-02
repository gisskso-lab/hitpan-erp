using HitPan.Backoffice.Components;
using HitPan.Backoffice.Services;
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

        builder.Services.AddMudServices();

        // 인증·인가 — 백오피스 전용 JWT (다음 세션에 별도 발급기 박제)
        // 헌법 #35 정합: ERP JWT와 완전 분리 (aud=backoffice)
        builder.Services.AddAuthentication("Backoffice").AddCookie("Backoffice", o =>
        {
            o.LoginPath = "/login";
        });
        builder.Services.AddAuthorization(o =>
        {
            // 백오피스 정책 — 다음 세션 JWT 박제 후 RequireClaim으로 강화
            o.AddPolicy("PlatformOnlyV2", p => p.RequireAuthenticatedUser());
            o.AddPolicy("PlatformManagerOrAbove", p => p.RequireAuthenticatedUser());
        });
        builder.Services.AddCascadingAuthenticationState();

        // HttpClient — 백오피스 API 호출 (별도 HitPan.Backoffice.API 분리 예정)
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

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
