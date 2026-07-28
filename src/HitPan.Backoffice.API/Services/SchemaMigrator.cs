using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 백오피스(hitpan_backoffice) 자립형 스키마 자동 적용기.
///
/// 사장님 결재 2026-06-18 — A안: "백오피스 스키마 파일 신설 + 마이그 자동실행".
///
/// 동작:
///   - 앱 기동 시 installer/backoffice/ 폴더의 *.sql을 파일명 번호순으로 순차 적용.
///   - 적용 이력(_schema_migrations)으로 멱등성 — 같은 (파일명+내용해시)는 1회만.
///   - CREATE TABLE IF NOT EXISTS 기반이라 재실행 안전.
///
/// 헌법 정합:
///   - #29 — DB 인스턴스 생성(CREATE DATABASE)·운영은 사장님 영역. 본 클래스는 "앱이 자기
///           스키마를 보장"하는 코드일 뿐. 환경변수 HITPAN_BO_AUTO_MIGRATE=1 일 때만 동작(기본 off).
///           즉 사장님이 명시적으로 켜야 적용 — 통제권은 사장님.
///   - #15 — 빈 catch 금지. 실패 시 로그 + 기동 중단(스키마 불일치로 런타임 500 잠복 방지).
///   - #19 — 스키마 보장으로 "어 어젠 됐는데" 류 환경차 사고 차단.
/// </summary>
public sealed class SchemaMigrator
{
    private readonly string _connectionString;
    private readonly string _scriptsDir;
    private readonly ILogger<SchemaMigrator> _logger;

    public SchemaMigrator(string connectionString, string scriptsDir, ILogger<SchemaMigrator> logger)
    {
        _connectionString = connectionString;
        _scriptsDir = scriptsDir;
        _logger = logger;
    }

    /// <summary>
    /// 스크립트 폴더의 *.sql을 번호순으로 적용. 환경변수 HITPAN_BO_AUTO_MIGRATE=1 일 때만 호출되도록
    /// Program.cs에서 게이트한다.
    /// </summary>
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_scriptsDir))
        {
            // 스크립트 폴더가 없으면 적용할 게 없음 — 명확히 로그하고 통과(사고 아님).
            _logger.LogWarning("[SchemaMigrator] 스크립트 폴더 없음: {Dir} — 스키마 적용 건너뜀", _scriptsDir);
            return;
        }

        await using var db = new MySqlConnection(_connectionString);
        await db.OpenAsync(ct);

        // 적용 이력 테이블 보장
        await ExecuteAsync(db, @"
            CREATE TABLE IF NOT EXISTS _schema_migrations (
                file_name     varchar(200) NOT NULL,
                content_hash  varchar(64)  NOT NULL,
                applied_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
                PRIMARY KEY (file_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci", ct);

        var files = Directory.GetFiles(_scriptsDir, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("[SchemaMigrator] 적용할 .sql 0개 ({Dir})", _scriptsDir);
            return;
        }

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var rawSql = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            // 멱등 해시는 "원본"(치환 전) 기준 — env값이 바뀌어도 같은 파일은 재실행 안 함.
            // (env로 주입하는 값은 비밀번호 해시 등 민감값이라 _schema_migrations에 흔적을 남기지 않는 목적도 겸함)
            var hash = ComputeSha256(rawSql);

            // 이미 같은 내용으로 적용됐으면 건너뜀(멱등)
            var appliedHash = await QueryHashAsync(db, fileName, ct);
            if (appliedHash == hash)
            {
                _logger.LogInformation("[SchemaMigrator] skip (적용됨) {File}", fileName);
                continue;
            }

            // 환경변수 치환: SQL 안 ${VAR} 를 실행 직전에만 환경변수 값으로 치환.
            //   - 의도: 비밀번호 해시 등 민감값을 Git/SQL 파일에 평문으로 두지 않고 NCP env로 주입(#22·#23).
            //   - 미설정(${VAR}가 그대로 남음) → 이 파일은 건너뜀. 빈 값으로 깨진 INSERT 실행 방지(#15·#19).
            if (!TrySubstituteEnv(rawSql, fileName, out var sql))
            {
                continue;
            }

            try
            {
                // 한 파일을 통째로 실행(여러 CREATE TABLE 포함). MySqlConnector는
                // AllowUserVariables + 다중 statement를 ExecuteNonQuery로 처리.
                await using (var cmd = new MySqlCommand(sql, db))
                {
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // 이력 기록(파일명 PK — 내용 바뀌면 UPDATE)
                await using (var rec = new MySqlCommand(@"
                    INSERT INTO _schema_migrations (file_name, content_hash, applied_at)
                    VALUES (@F, @H, current_timestamp(6))
                    ON DUPLICATE KEY UPDATE content_hash = @H, applied_at = current_timestamp(6)", db))
                {
                    rec.Parameters.AddWithValue("@F", fileName);
                    rec.Parameters.AddWithValue("@H", hash);
                    await rec.ExecuteNonQueryAsync(ct);
                }

                _logger.LogInformation("[SchemaMigrator] applied {File}", fileName);
            }
            catch (Exception ex)
            {
                // 빈 catch 금지(#15). 스키마 불일치는 런타임 500 잠복이므로 기동 중단으로 즉시 노출(#19).
                _logger.LogError(ex, "[SchemaMigrator] 적용 실패 {File} — 기동 중단", fileName);
                throw;
            }
        }
    }

    private static async Task ExecuteAsync(MySqlConnection db, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, db) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> QueryHashAsync(MySqlConnection db, string fileName, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            "SELECT content_hash FROM _schema_migrations WHERE file_name = @F", db);
        cmd.Parameters.AddWithValue("@F", fileName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // ${VAR} 패턴을 환경변수로 치환. 미설정 변수가 하나라도 있으면 false(파일 건너뜀).
    //   - 예: 91_seed_admin.sql 의 '${HITPAN_BO_SEED_ADMIN_HASH}' → NCP env 주입값.
    //   - env 미설정 시 master 계정 시드를 건너뜀(빈 해시로 깨진 계정 INSERT 방지). 사고 아님 — 로그로 명시.
    private bool TrySubstituteEnv(string sql, string fileName, out string substituted)
    {
        substituted = sql;
        var pattern = new System.Text.RegularExpressions.Regex(@"\$\{([A-Z0-9_]+)\}");
        var matches = pattern.Matches(sql);
        if (matches.Count == 0)
        {
            return true; // 치환할 게 없으면 원본 그대로 실행
        }

        var missing = new List<string>();
        substituted = pattern.Replace(sql, m =>
        {
            var name = m.Groups[1].Value;
            var val = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(val))
            {
                missing.Add(name);
                return m.Value; // 그대로 둠(아래에서 건너뜀 처리)
            }
            return val;
        });

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "[SchemaMigrator] {File} 건너뜀 — 환경변수 미설정: {Vars}. (값 주입 후 다음 기동 때 자동 적용)",
                fileName, string.Join(", ", missing.Distinct()));
            return false;
        }
        return true;
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
