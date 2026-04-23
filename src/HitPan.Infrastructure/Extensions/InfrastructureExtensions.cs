using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using System.Data;

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

        // DefaultCommandTimeout=90: 저장 후 partner_balance 집계 뷰가 대량 집계를 할 때
        // 기본 30초로는 부족해 Command Timeout → 롤백 → 유실이 발생. 90초 안전마진.
        var connStr = $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=90;";
        // v1.0.6: AutoDetect는 기동 시 DB 연결을 선행 호출 → 설치 직후 타이밍 민감 구간에서 지연·크래시 유발.
        // 설치파일은 MariaDB 11.4 MSI를 동봉하므로 고정 버전으로 안정화.
        var serverVersion = new MariaDbServerVersion(new Version(11, 4, 0));

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connStr,
                serverVersion,
                x => x.MigrationsAssembly("HitPan.Infrastructure")));
        services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));
        services.AddScoped<CommonCodeSeeder>();
        services.AddScoped<SystemSeeder>();

        return services;
    }
}
