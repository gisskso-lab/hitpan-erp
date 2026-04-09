using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HitPan.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
        var db = Environment.GetEnvironmentVariable("DB_NAME")
            ?? throw new InvalidOperationException("DB_NAME 환경변수 없음");
        var user = Environment.GetEnvironmentVariable("DB_USER")
            ?? throw new InvalidOperationException("DB_USER 환경변수 없음");
        var pwd = Environment.GetEnvironmentVariable("DB_PASSWORD")
            ?? throw new InvalidOperationException("DB_PASSWORD 환경변수 없음");

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
    }
}
