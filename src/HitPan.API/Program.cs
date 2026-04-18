using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.API.Extensions;
using HitPan.API.Middleware;
using HitPan.API.Services;
using HitPan.Infrastructure.Events;
using HitPan.Infrastructure.Extensions;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Persistence.Seed;
using HitPan.Infrastructure.Security;
using HitPan.API.Security;
using QuestPDF.Infrastructure;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

// Add services to the container.

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<IHashService, HashService>();
builder.Services.AddInfrastructure();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAuthUserLookup, AuthUserLookup>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<DeliveryBatchService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<IBomService, BomService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ILeaveRequestService, LeaveRequestService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<IPartnerBalanceRepository, PartnerBalanceRepository>();
builder.Services.AddScoped<IEventPublisher, SyncEventPublisher>();
builder.Services.AddSingleton<AccessTokenValidator>();
builder.Services.AddJwtAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SalesOnly", policy =>
        policy.RequireRole("system_admin", "sales_manager", "sales_user", "TenantAdmin", "tenant_admin"));
    options.AddPolicy("SalesManager", policy =>
        policy.RequireRole("system_admin", "sales_manager"));
    options.AddPolicy("PurchaseOnly", policy =>
        policy.RequireRole("system_admin", "purchase_manager"));
    options.AddPolicy("AccountOnly", policy =>
        policy.RequireRole("system_admin", "account_manager"));
    options.AddPolicy("HROnly", policy =>
        policy.RequireRole("system_admin", "hr_manager"));
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("system_admin"));
    options.AddPolicy("PlatformOnly", policy =>
        policy.RequireClaim("account_type", "platform_admin"));
    options.AddPolicy("ResellerOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "reseller_admin") ||
            ctx.User.HasClaim("account_type", "platform_admin")));
    options.AddPolicy("TenantOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin") ||
            ctx.User.HasClaim("account_type", "tenant_user") ||
            ctx.User.HasClaim("account_type", "platform_admin")));
    // GET /api/tenants/me — Blazor TenantProfile (플랫폼·대리점·고객사)
    options.AddPolicy("TenantProfile", policy =>
        policy.RequireAssertion(ctx =>
        {
            var at = ctx.User.FindFirst("account_type")?.Value;
            return at is "platform_admin" or "reseller_admin" or "tenant_admin" or "tenant_user";
        }));
    options.AddPolicy("TenantAdminOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("account_type", "tenant_admin") ||
            ctx.User.HasClaim("account_type", "platform_admin")));
});
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWasmDev", policy =>
    {
        if (isDevelopment)
        {
            // LAN에서 PC2 브라우저 등 임의 Origin → API 호출 허용(Development 전용)
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins("http://localhost:5234", "https://localhost:7100")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SystemSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
// Production이 아니면 Swagger 활성(터미널에서 ENV 미지정 시 Production이 되어 UI가 안 뜨는 경우 방지)
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Development·LAN에서는 http://IP:5257 만 쓰는 경우가 많아 리다이렉트 생략
if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

app.UseCors("BlazorWasmDev");

app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<AuditLogMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();

app.MapControllers();

if (!app.Environment.IsProduction())
{
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.Run();
