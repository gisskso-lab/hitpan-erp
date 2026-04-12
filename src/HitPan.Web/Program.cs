using HitPan.Web;
using HitPan.Web.Providers;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = Environment.GetEnvironmentVariable("HitPan__ApiBaseUrl");
}

if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl");
}

if (string.IsNullOrWhiteSpace(apiBase))
{
    apiBase = "http://localhost:5257";
}

var apiUri = new Uri($"{apiBase.TrimEnd('/')}/");

builder.Services.AddMudServices();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<HitPanProtectedLocalStorage>();
builder.Services.AddScoped<IAuthTokenRefresher, AuthTokenRefresher>();
builder.Services.AddScoped<HitPanAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<HitPanAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<WorkTabService>();
builder.Services.AddScoped<DeliveryService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<SpecialPriceService>();
builder.Services.AddTransient<HitPanApiAuthHandler>();
builder.Services.AddScoped<TenantProfileService>();
builder.Services.AddScoped(sp =>
{
    var handler = new HitPanApiAuthHandler(sp.GetRequiredService<HitPanProtectedLocalStorage>())
    {
        InnerHandler = new HttpClientHandler()
    };
    return new HttpClient(handler) { BaseAddress = apiUri };
});

await builder.Build().RunAsync();
