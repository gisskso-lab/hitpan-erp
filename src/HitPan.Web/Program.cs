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
builder.Services.AddScoped<QuotationService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<PartnerMasterService>();
builder.Services.AddScoped<ItemMasterService>();
builder.Services.AddScoped<BomService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<LeaveRequestService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<SpecialPriceService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<TaxInvoiceApiService>();
builder.Services.AddScoped<CollectionPaymentService>();
builder.Services.AddScoped<MonthlyClosingService>();
builder.Services.AddScoped<FinanceClientService>();
builder.Services.AddScoped<HrClientService>();
builder.Services.AddScoped<ESignService>();
builder.Services.AddScoped<LaborContractService>();
builder.Services.AddScoped<ChatbotService>();
builder.Services.AddTransient<HitPanApiAuthHandler>();
builder.Services.AddScoped<TenantProfileService>();
builder.Services.AddScoped(sp =>
{
    var handler = new HitPanApiAuthHandler(
        sp.GetRequiredService<HitPanProtectedLocalStorage>(),
        sp.GetRequiredService<MudBlazor.ISnackbar>(),
        sp.GetRequiredService<ILogger<HitPanApiAuthHandler>>())
    {
        InnerHandler = new HttpClientHandler()
    };
    return new HttpClient(handler) { BaseAddress = apiUri };
});

await builder.Build().RunAsync();
