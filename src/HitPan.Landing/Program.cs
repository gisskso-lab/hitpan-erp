using HitPan.Landing.Components;

namespace HitPan.Landing;

// 랜딩 진입점 (헌법 #35 — 본사 클라우드 / 가입 영역)
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // HttpClient — 본사 백오피스 API 호출용 (헌법 #35: 랜딩 → 백오피스 가입 흐름)
        //   - 로컬: http://localhost:5258 (HitPan.Backoffice.API)
        //   - 운영: https://api.back.hitpan.kr (appsettings.Production.json)
        var backofficeApi = builder.Configuration["BackofficeApi:BaseUrl"] ?? "http://localhost:5258/";
        builder.Services.AddHttpClient("backoffice", c => c.BaseAddress = new Uri(backofficeApi));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("backoffice"));

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found");
        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
