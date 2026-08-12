using System.Data.Common;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace HitPan.Infrastructure.Persistence;

/// <summary>
/// MDB 마이그 전용 connection 팩토리 (정공법 SEC-04).
///
/// 환경변수 DB_HOST/DB_PORT/DB_NAME/DB_USER/DB_PASSWORD 재사용 + 마이그 전용 옵션:
///   - AllowLoadLocalInfile=true : MySqlBulkCopy(LOAD DATA LOCAL INFILE) 활성화
///   - DefaultCommandTimeout=600 : 대용량 bulk insert 10분 안전마진
///   - Pooling=true + ConnectionLifeTime=0 + ConnectionReset=true(기본) :
///     pool 반환 시 SET SESSION 변수 자동 reset → 다른 마이그 잡으로의 오염 0
///   - ApplicationName=hitpan-migration : DBA가 process list에서 마이그 잡만 분리 식별
///
/// 일반 컨트롤러 풀(AddInfrastructure의 IDbConnection)과 connection string이 다르므로
/// MySqlConnector가 내부적으로 별도 pool을 유지한다 → 물리 분리 보장.
/// </summary>
public sealed class MigrationDbConnectionFactory : IMigrationDbConnectionFactory
{
    private readonly string _connStr;
    private readonly ILogger<MigrationDbConnectionFactory> _logger;

    public MigrationDbConnectionFactory(ILogger<MigrationDbConnectionFactory> logger)
    {
        _logger = logger;

        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");

        // ApplicationName이 일반 풀과 다르므로 MySqlConnector가 별도 pool 인스턴스 유지.
        // MaximumPoolSize=20 : 11개 PANDATA 병렬 + 여유 9개 (헌법 #16 안전 마진).
        // 🔴 AllowUserVariables=true (봉합 2026-08-12, 실측 적발)
        //   이 팩토리는 MDB 마이그 전용이 아니다 — **MigrationRunner(DB 스키마 마이그)도 쓴다.**
        //   DB-NN 멱등 마이그는 'SET @col_exists := (SELECT ...)' + PREPARE/EXECUTE 관용구를 쓰는데,
        //   이 옵션이 없으면 MySqlConnector 가 '@col_exists' 를 **파라미터로 착각**해
        //     "Parameter '@col_exists' must be defined." 로 마이그가 통째로 중단된다.
        //
        //   실측(API 기동 로그): "🛑 DB 스키마 갱신 실패 — 실패 마이그: DB-88"
        //   ⇒ DB-88·DB-89·DB-90 이 전부 이 문법이라 **고객 PC 에서 적용되지 않고 있었다.**
        //     특히 DB-89 는 메인PC 잠김 봉합이다 — 1.2.67 로 게시했으나 실제로는 안 돌았다.
        //     즉 "고쳤다"고 보고한 것이 고객 PC 에서는 여전히 안 고쳐진 상태였다.
        //
        //   개발 PC 에서 못 본 이유: 마이그를 mysql.exe 로 직접 넣어 확인했기 때문이다.
        //   그 경로는 이 옵션과 무관하다(사장님 원칙 — 개발PC 정상은 검증이 아니다).
        _connStr =
            $"Server={host};Port={port};Database={db};User={user};Password={pwd};" +
            "DefaultCommandTimeout=600;AllowLoadLocalInfile=true;AllowUserVariables=true;" +
            "Pooling=true;MinimumPoolSize=0;MaximumPoolSize=20;" +
            "ApplicationName=hitpan-migration;";
    }

    public async Task<DbConnection> CreateOpenAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_connStr);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch (Exception ex)
        {
            // 헌법 #15: silent swallow 금지 — 호출자에 throw 전에 진단 로그 남김.
            _logger.LogError(ex, "[MDB마이그] 전용 풀 connection 발급 실패");
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
