using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// DB 스키마 마이그(DB-*.sql) 적용 주체 — 고리4 ①단계. (설계서 §4-4 #2)
///
/// schema_migrations(DB-84) 를 기준점으로, Migrations/SQL/ 폴더의 DB-NN_*.sql 을
/// 번호순·멱등·순차로 적용한다. 사람 손 ALTER(헌법 #39)를 검증된 자동 적용으로 대체.
///
/// ★ ① 범위 한정 — 절대 넘지 않음:
///  - 자동 롤백 호출 0건(실패 시 기록+중단까지만, ③단계 상태머신 범위).
///  - 앱 시작 자동 실행 배선 0건(②단계 Velopack 결선 범위). 본 클래스는 "호출되면 동작".
///
/// 헌법 정합:
///  - #1  : 기존 코드 무수정. 신규 클래스만.
///  - #13 : DESCRIBE 정신 — schema_migrations 존재 자체를 먼저 보장(EnsureTrackingTable)한 뒤 조회.
///  - #15 : 빈 catch 금지 — 실패는 로그 + 결과 객체로 보고(삼키지 않음).
///  - #16 : MySqlConnection + Task.WhenAll 금지 — 단일 connection, foreach 순차 실행.
///  - #26 : CommandTimeout = 86400(물리 한계). 목표시간 10분과 동일시 금지. 대용량 ALTER 가
///          끝까지 돌 수 있게(마이그 가능성 보장). row-by-row 정당화 아님(단순 .sql 실행).
///  - #36 : 신규설치 진실원은 clean DDL. 본 러너는 '업데이트 시 ALTER 적용' 경로 전담.
/// </summary>
public sealed class MigrationRunner : IMigrationRunner
{
    // 헌법 #26: 대용량 ALTER/마이그가 물리 한계까지 돌 수 있게 — 목표시간(10분=600초) 아니라
    //           물리 한계(24시간=86400초). Timeout 으로 마이그 가능성을 막지 않는다.
    private const int MigrationCommandTimeoutSeconds = 86400;

    // DB-NN_*.sql 파일명에서 정렬 키(번호 NN)와 식별자(DB-NN)를 뽑는 패턴.
    // 예: "DB-84_schema_migrations.sql" → 번호 84, 식별자 "DB-84".
    //     "DB-08b_..." 같은 접미사(b)도 허용: 번호 8, 식별자 "DB-08b".
    private static readonly Regex MigrationFilePattern =
        new(@"^DB-(?<num>\d+)(?<suffix>[a-zA-Z]*)_", RegexOptions.Compiled);

    private readonly IMigrationDbConnectionFactory _connFactory;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(IMigrationDbConnectionFactory connFactory, ILogger<MigrationRunner> logger)
    {
        _connFactory = connFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MigrationRunResult> ApplyPendingAsync(string? appVersion = null, CancellationToken ct = default)
    {
        // 1) 마이그 SQL 폴더 위치 결정 (여러 후보 탐색 — 개발/배포 레이아웃 양쪽 대응).
        var sqlDir = ResolveMigrationsDirectory();
        if (sqlDir is null)
        {
            const string msg = "마이그 SQL 폴더(Migrations/SQL)를 찾지 못함 — 적용 대상 0건으로 간주하고 중단(no-op).";
            _logger.LogError("[MigrationRunner] {Msg}", msg);
            return new MigrationRunResult { Success = false, FailureMessage = msg };
        }

        // 2) DB-NN_*.sql 파일을 번호순(+접미사순)으로 정렬.
        var files = Directory.GetFiles(sqlDir, "DB-*.sql")
            .Select(path => new { Path = path, Name = Path.GetFileName(path) })
            .Select(f => new { f.Path, f.Name, Match = MigrationFilePattern.Match(f.Name) })
            .Where(f => f.Match.Success)
            .OrderBy(f => int.Parse(f.Match.Groups["num"].Value))
            .ThenBy(f => f.Match.Groups["suffix"].Value, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MigrationFile(
                MigrationId: $"DB-{f.Match.Groups["num"].Value.PadLeft(2, '0')}{f.Match.Groups["suffix"].Value}",
                Path: f.Path,
                FileName: f.Name))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("[MigrationRunner] 적용 대상 DB-*.sql 0건 ({Dir}).", sqlDir);
            return new MigrationRunResult { Success = true };
        }

        // 헌법 #16: 단일 connection 으로 순차 실행 (병렬 금지 — MySqlConnection 은 thread-safe 아님).
        DbConnection conn = await _connFactory.CreateOpenAsync(ct).ConfigureAwait(false);
        await using (conn.ConfigureAwait(false))
        {
            // 3) 추적 테이블 보장 (헌법 #13 — 조회 전에 존재 확인). DB-84 가 아직 안 돌았을 수도 있으므로
            //    schema_migrations 자체는 IF NOT EXISTS 로 선행 생성한다(자기 자신을 추적할 그릇).
            await EnsureTrackingTableAsync(conn, ct).ConfigureAwait(false);

            // 4) 이미 success=1 로 적용된 마이그 식별자 집합(멱등 skip 근거).
            var applied = (await conn.QueryAsync<string>(new CommandDefinition(
                    "SELECT migration_id FROM schema_migrations WHERE success = 1;",
                    cancellationToken: ct)).ConfigureAwait(false))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var appliedNow = new List<string>();
            int skipped = 0;

            // 5) 미적용분만 순서대로 실행 (foreach = 순차, 헌법 #16).
            foreach (var mig in files)
            {
                ct.ThrowIfCancellationRequested();

                if (applied.Contains(mig.MigrationId))
                {
                    skipped++;
                    continue;
                }

                string sql;
                try
                {
                    sql = await File.ReadAllTextAsync(mig.Path, Encoding.UTF8, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 헌법 #15: silent swallow 금지 — 로그 + 결과 보고 + 중단.
                    _logger.LogError(ex, "[MigrationRunner] 마이그 파일 읽기 실패: {Id} ({File})", mig.MigrationId, mig.FileName);
                    await RecordAsync(conn, mig.MigrationId, appVersion, checksum: null, success: false, ct).ConfigureAwait(false);
                    return new MigrationRunResult
                    {
                        Success = false,
                        AppliedMigrationIds = appliedNow,
                        SkippedCount = skipped,
                        FailedMigrationId = mig.MigrationId,
                        FailureMessage = $"파일 읽기 실패: {ex.Message}",
                    };
                }

                var checksum = ComputeSha256(sql);

                try
                {
                    // 헌법 #26: 대용량 ALTER 가 물리 한계까지 돌 수 있게 86400초.
                    await conn.ExecuteAsync(new CommandDefinition(
                        sql, commandTimeout: MigrationCommandTimeoutSeconds, cancellationToken: ct))
                        .ConfigureAwait(false);

                    await RecordAsync(conn, mig.MigrationId, appVersion, checksum, success: true, ct).ConfigureAwait(false);
                    appliedNow.Add(mig.MigrationId);
                    _logger.LogInformation("[MigrationRunner] 적용 완료: {Id} ({File})", mig.MigrationId, mig.FileName);
                }
                catch (Exception ex)
                {
                    // 헌법 #15: 실패는 어느 파일·어디서인지 명확히 로그 + success=0 기록 + 즉시 중단.
                    //   ★ ① 범위: 자동 롤백 호출 절대 금지(②③ 범위). MariaDB DDL 암묵 커밋이라
                    //     트랜잭션 롤백도 불가(설계서 R1) — 여기서는 '기록 + 중단'까지만.
                    _logger.LogError(ex,
                        "[MigrationRunner] 마이그 실패 — 중단. 실패 마이그: {Id} ({File}). 다음 마이그 진행 안 함.",
                        mig.MigrationId, mig.FileName);

                    await RecordAsync(conn, mig.MigrationId, appVersion, checksum, success: false, ct).ConfigureAwait(false);

                    return new MigrationRunResult
                    {
                        Success = false,
                        AppliedMigrationIds = appliedNow,
                        SkippedCount = skipped,
                        FailedMigrationId = mig.MigrationId,
                        FailureMessage = ex.Message,
                    };
                }
            }

            _logger.LogInformation(
                "[MigrationRunner] 완료 — 신규 적용 {Applied}건, 멱등 skip {Skipped}건.",
                appliedNow.Count, skipped);

            return new MigrationRunResult
            {
                Success = true,
                AppliedMigrationIds = appliedNow,
                SkippedCount = skipped,
            };
        }
    }

    /// <summary>
    /// schema_migrations 추적 테이블을 보장한다(IF NOT EXISTS). DB-84 와 동일 정의 — 자기 자신을
    /// 추적할 그릇이 DB-84 적용 전에도 존재해야 멱등 판정이 가능하다(부트스트랩).
    /// 헌법 #17 InnoDB + utf8mb4_unicode_ci.
    /// </summary>
    private static async Task EnsureTrackingTableAsync(DbConnection conn, CancellationToken ct)
    {
        const string ddl = @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    migration_id VARCHAR(100) NOT NULL,
    app_version VARCHAR(20) NULL,
    checksum VARCHAR(64) NULL,
    success TINYINT(1) NOT NULL DEFAULT 1,
    applied_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    UNIQUE KEY uq_schema_migrations_migration_id (migration_id),
    INDEX idx_schema_migrations_applied (applied_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

        await conn.ExecuteAsync(new CommandDefinition(
            ddl, commandTimeout: MigrationCommandTimeoutSeconds, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 적용 결과를 schema_migrations 에 기록한다(성공/실패 모두).
    /// 같은 migration_id 재기록 시(부분 실패 후 재시도) ON DUPLICATE KEY UPDATE 로 멱등 갱신.
    /// </summary>
    private static async Task RecordAsync(
        DbConnection conn, string migrationId, string? appVersion, string? checksum, bool success, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO schema_migrations (migration_id, app_version, checksum, success, applied_at)
VALUES (@MigrationId, @AppVersion, @Checksum, @Success, CURRENT_TIMESTAMP(3))
ON DUPLICATE KEY UPDATE
    app_version = VALUES(app_version),
    checksum    = VALUES(checksum),
    success     = VALUES(success),
    applied_at  = CURRENT_TIMESTAMP(3);";

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { MigrationId = migrationId, AppVersion = appVersion, Checksum = checksum, Success = success ? 1 : 0 },
            commandTimeout: MigrationCommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 마이그 SQL 폴더(Migrations/SQL)를 여러 후보 위치에서 탐색한다.
    /// 개발(소스 레이아웃)·배포(실행 폴더 옆) 양쪽을 모두 시도. 없으면 null.
    /// ① 단계는 자동 배선이 없으므로, 호출 시점에 폴더가 존재하면 동작한다.
    /// </summary>
    private string? ResolveMigrationsDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            // 배포 시 실행 폴더 옆에 SQL 을 복사해 둔 경우.
            Path.Combine(baseDir, "Migrations", "SQL"),
            Path.Combine(baseDir, "Migrations", "Sql"),
            // 개발 시 bin/Debug/netX 에서 소스 트리(src/HitPan.API/Migrations/SQL)로 거슬러 올라가는 경우.
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Migrations", "SQL")),
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                _logger.LogInformation("[MigrationRunner] 마이그 SQL 폴더: {Dir}", dir);
                return dir;
            }
        }

        return null;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>정렬·실행 단위 — 마이그 식별자 + 파일 경로.</summary>
    private readonly record struct MigrationFile(string MigrationId, string Path, string FileName);
}
