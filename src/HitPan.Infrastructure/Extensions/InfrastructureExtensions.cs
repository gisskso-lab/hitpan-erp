using HitPan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HitPan.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
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
        ServerVersion serverVersion;
        try
        {
            serverVersion = ServerVersion.AutoDetect(connStr);
        }
        catch
        {
            // Fallback keeps startup/migrations operable before real DB credentials are provisioned.
            serverVersion = new MariaDbServerVersion(new Version(10, 11, 0));
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connStr,
                serverVersion,
                x => x.MigrationsAssembly("HitPan.Infrastructure")));

        return services;
    }
}
