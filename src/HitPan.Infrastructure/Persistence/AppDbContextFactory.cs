using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Configuration;
using HitPan.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HitPan.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");

        var connStr = $"Server={host};Port={port};Database={db};User={user};Password={pwd};";
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        // Design-time factory avoids AutoDetect network access during migration scaffolding.
        builder.UseMySql(
            connStr,
            new MariaDbServerVersion(new Version(10, 11, 0)),
            x => x.MigrationsAssembly("HitPan.Infrastructure"));

        return new AppDbContext(builder.Options, new DesignTimeTenant(), new EncryptionService());
    }

    private sealed class DesignTimeTenant : ICurrentTenant
    {
        public string TenantId => string.Empty;
        public string UserId => string.Empty;
        public string AccountType => string.Empty;
    }
}
