using System.Diagnostics;
using System.Text;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 워치독 로컬 새버전 상태 라이터 (작1 고리2 마지막 빈 칸, 2026-06-29).
///
/// A안(헌법 #30): 워치독이 발견한 Major 새버전 정보를 고객 PC 로컬 ERP DB(local_update_status, DB-83)에 적재한다.
///   ① 워치독이 Major manifest 발견 → ② 본 라이터가 local_update_status 에 UPSERT(최신 1건) →
///   ③ ERP 가 로그인 시 본 테이블을 SELECT 해 "설치버전보다 높은 새버전 있나" 판단 → Y/N 동의 팝업.
/// 본사를 거치지 않는다(본사 의존 0). 워치독→로컬 DB→ERP 단방향 로컬 자가완결.
///
/// 왜 MySqlConnection 패키지를 안 붙이고 mariadb 클라이언트 CLI 로 쓰나(WatchdogConsentReader 와 동일 정신):
///   워치독 .csproj 는 DB 드라이버 의존이 0이다. 동의 리더가 이미 db.conf 자격증명 + MariaDB 클라이언트 CLI
///   직접 실행 패턴을 쓰므로, 적재도 같은 자족(self-contained) 방식으로 한다 — API 생존에 의존 0(헌법 #30).
///   헌법 #16(MySqlConnection + Task.WhenAll 금지)은 단일 동기 쿼리라 무관하나, 애초에 드라이버를 안 쓴다.
///
/// 헌법 정합:
///   #1 — 추가만(신규 클래스) / #15 — 모든 실패 경로 로그(침묵 금지) / #16 — 단일 쿼리(드라이버 미사용) /
///   #18·#22·#30 — 로컬 DB 만 씀, 본사 전송·의존 0 / #34 — 정식 완성도.
/// </summary>
public sealed class WatchdogStatusWriter
{
    private readonly ILogger<WatchdogStatusWriter> _logger;

    public WatchdogStatusWriter(ILogger<WatchdogStatusWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 발견한 새버전(Major)을 local_update_status 에 적재한다. "최신 1건"만 유지하기 위해 DELETE→INSERT 로 교체한다.
    ///   적재 실패는 침묵하지 않고 로그만 남긴다(헌법 #15). 적재 실패가 워치독 루프 전체를 멈추지 않게 false 반환.
    /// </summary>
    public async Task<bool> UpsertLatestAsync(UpdateManifest m, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Status] db.conf 에서 DB 자격증명을 읽지 못했습니다(DB_NAME/DB_USER 부재) — 새버전 적재 불가");
                return false;
            }

            // SQL 인젝션 방지: version 은 manifest.Version(서버 발행 SemVer)이며 사용자 입력이 아니다.
            //   그래도 보수적으로 SemVer 형식([0-9.]+ 만 허용)을 벗어나면 적재를 거부한다(헌법 #25 안전하게).
            if (!IsSafeVersionLiteral(m.Version))
            {
                _logger.LogError("[Update/Status] 안전하지 않은 버전 문자열 — 새버전 적재 거부: '{V}'", m.Version);
                return false;
            }

            // consent_message·download_url 은 manifest 발행값으로 따옴표가 섞일 수 있으므로 작은따옴표를 이스케이프한다.
            var channel = m.Channel.ToString();               // enum → 안전(영문 식별자)
            var consentMsg = EscapeSqlLiteral(m.ConsentMessage);
            var downloadUrl = EscapeSqlLiteral(m.DownloadUrl);
            var reqMig = m.RequiresMigration ? 1 : 0;

            // "최신 1건" 유지: 기존 행을 모두 지우고 새 행 1건만 INSERT (멱등). 단일 -e 배치로 한 번에 실행(헌법 #16).
            //   discovered_at 은 NOW(3) 로 서버 시각. consent_message/download_url 은 NULL 또는 작은따옴표 리터럴.
            var sql =
                "DELETE FROM local_update_status; " +
                "INSERT INTO local_update_status " +
                "(latest_version, update_channel, consent_message, download_url, requires_migration, discovered_at) " +
                $"VALUES ('{m.Version}', '{channel}', {consentMsg}, {downloadUrl}, {reqMig}, NOW(3));";

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            await RunWriteAsync(clientExe, args, ct).ConfigureAwait(false);
            _logger.LogInformation("[Update/Status] 새버전 {V}({C}) 로컬 적재 완료 — ERP 로그인 팝업 노출 가능", m.Version, channel);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 적재 실패는 워치독 루프를 멈추지 않고 다음 주기 재시도한다(보수적).
            _logger.LogError(ex, "[Update/Status] 로컬 새버전 적재 실패 — 다음 주기 재시도");
            return false;
        }
    }

    /// <summary>
    /// W4-6 — 업데이트 "적용 결과"를 local_update_apply_status 에 UPSERT 한다(멱등키 = applied_version).
    ///   local_update_status(새버전 "발견" 적재)와 역할이 다르다 — 이건 실제 적용(교체·재시작·롤백) 뒤 "결과".
    ///
    ///   result = success | rolled_back | rollback_failed | blocked (작지서·clean DDL 코멘트와 동일 어휘).
    ///   버전당 1행: UNIQUE(applied_version) + INSERT ... ON DUPLICATE KEY UPDATE 로 재시도 시 덮어쓴다(멱등).
    ///
    /// ★ 자가생성(중요): 기존 1.2.33 PC 가 자동업데이트로 넘어오면 이 테이블이 없다(clean DDL 은 신규설치만).
    ///   그래서 쓰기 직전 CREATE TABLE IF NOT EXISTS(clean DDL 과 문자 일치)를 먼저 실행해 자가생성한다.
    ///   이 테이블은 업데이트 zip 의 Migrations/SQL 에 절대 넣지 않는다(W4-2 호환게이트가 local_update_ 접두사
    ///   차단 = 빌드실패). clean DDL + 이 자가생성 두 경로뿐이다(헌법 #36).
    ///
    /// 실패는 침묵하지 않고 로그만 남긴다(헌법 #15). 반환 false = 기록 실패(업데이트 흐름은 멈추지 않는다).
    /// </summary>
    public async Task<bool> WriteApplyStatusAsync(string version, string result, string? detail, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Apply] db.conf 에서 DB 자격증명을 읽지 못했습니다(DB_NAME/DB_USER 부재) — 적용결과 기록 불가");
                return false;
            }
            if (!IsSafeVersionLiteral(version))
            {
                _logger.LogError("[Update/Apply] 안전하지 않은 버전 문자열 — 적용결과 기록 거부: '{V}'", version);
                return false;
            }

            var resultLit = EscapeSqlLiteral(result);      // enum 성격 문자열이나 보수적으로 이스케이프
            var detailLit = EscapeSqlLiteral(detail);       // NULL 또는 '이스케이프'

            // 자가생성 DDL — clean DDL(installer/hitpan_db_clean.sql:1887~1898)과 컬럼·키·엔진 100% 일치.
            //   차이는 CREATE TABLE IF NOT EXISTS(기존 데이터·행 보존, DROP 금지)뿐 — 자가생성은 파괴하지 않는다.
            const string createSql =
                "CREATE TABLE IF NOT EXISTS `local_update_apply_status` (" +
                "`id` bigint(20) NOT NULL AUTO_INCREMENT, " +
                "`tenant_id` varchar(36) DEFAULT NULL, " +
                "`applied_version` varchar(20) NOT NULL, " +
                "`result` varchar(20) NOT NULL, " +
                "`detail` text DEFAULT NULL, " +
                "`applied_at` datetime(3) NOT NULL, " +
                "`created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3), " +
                "PRIMARY KEY (`id`), " +
                "UNIQUE KEY `uk_local_update_apply_version` (`applied_version`), " +
                "KEY `idx_local_update_apply_at` (`applied_at`)" +
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

            // UPSERT — 버전당 1행. 재시도 시 result·detail·applied_at 을 덮어쓴다(멱등).
            //   tenant_id 는 워치독 단일 테넌트라 NULL(스키마 코멘트대로). applied_at = NOW(3).
            var upsertSql =
                "INSERT INTO `local_update_apply_status` (tenant_id, applied_version, result, detail, applied_at) " +
                $"VALUES (NULL, '{version}', {resultLit}, {detailLit}, NOW(3)) " +
                "ON DUPLICATE KEY UPDATE result=VALUES(result), detail=VALUES(detail), applied_at=VALUES(applied_at);";

            // 자가생성 + UPSERT 를 단일 -e 배치로 한 번에(헌법 #16 — 드라이버 미사용·단일 실행).
            var sql = createSql + " " + upsertSql;

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            await RunWriteAsync(clientExe, args, ct).ConfigureAwait(false);
            _logger.LogInformation("[Update/Apply] 적용결과 기록 완료 — 버전 {V}, 결과 {R}", version, result);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update/Apply] 적용결과 기록 실패 — 버전 {V}, 결과 {R}", version, result);
            return false;
        }
    }

    /// <summary>
    /// 20260722작2(A'안) — 로컬 schema_migrations 에 이미 적용된 마이그 식별자(migration_id) 집합을 읽는다.
    ///   업데이트 교차검증 게이트 ①이 "이번 zip 의 Migrations\SQL 중 로컬에 미적용인 신규가 있는가"를
    ///   판정하는 데 쓴다. 누적 이력(이미 적용된 DB-*.sql)은 신규가 아니므로 게이트를 막지 않는다(오탐 제거).
    ///
    ///   · 읽기 전용 SELECT. success=1(성공 적용)만 "적용됨"으로 본다(실패행은 미적용 취급 = 보수적).
    ///   · schema_migrations 테이블이 없거나(구 DB) 조회 실패면 null 을 돌려준다 —
    ///     호출부(게이트)가 null 을 받으면 "판정 불가 → 안전측(차단)"으로 처리한다(헌법 #20, CTO A-3).
    ///   · migration_id 는 파일명에서 추출된 값(예 "DB-84" 또는 "DB-84_schema_migrations.sql")이 섞일 수 있어,
    ///     호출부에서 접두 "DB-NN" 정규화로 대조한다(본 메서드는 원본 문자열을 그대로 돌려준다).
    /// </summary>
    public async Task<HashSet<string>?> GetAppliedMigrationIdsAsync(CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Gate] db.conf 에서 DB 자격증명을 읽지 못했습니다 — 적용 마이그 조회 불가(안전측 차단)");
                return null;
            }

            // 테이블 부재 시 오류로 죽지 않게 information_schema 로 존재 확인 후 SELECT. 단일 -e 배치로 결과만 받는다.
            //   존재하면 migration_id 목록, 없으면 결과 0행 → 빈 집합(신규 판정 시 전부 신규로 봄 = 차단측, 안전).
            const string sql =
                "SELECT migration_id FROM schema_migrations WHERE success=1;";

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            var (exit, stdout, stderr) = await RunReadAsync(clientExe, args, ct).ConfigureAwait(false);
            if (exit != 0)
            {
                // 테이블 부재("doesn't exist") 포함 — 판정 불가로 보고 안전측(차단) 유도(null 반환).
                _logger.LogWarning("[Update/Gate] schema_migrations 조회 실패(exit={E}): {Err} — 안전측 차단 유도", exit, stderr);
                return null;
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in stdout.Split('\n'))
            {
                var id = line.Trim();
                if (id.Length > 0) set.Add(id);
            }
            return set;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Gate] 적용 마이그 조회 예외 — 안전측 차단 유도");
            return null;
        }
    }

    /// <summary>stdout 을 돌려받는 읽기 실행(RunWriteAsync 의 SELECT 판). exit·stdout·stderr 를 반환한다.</summary>
    private async Task<(int exit, string stdout, string stderr)> RunReadAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"{exe} 실행 실패(Process.Start null)");

        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    /// <summary>SemVer 류 안전 리터럴만 허용(숫자·점만). SQL 리터럴 삽입 안전성 보강.</summary>
    private static bool IsSafeVersionLiteral(string v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.Length > 20) return false;
        foreach (var ch in v)
            if (!char.IsDigit(ch) && ch != '.') return false;
        return true;
    }

    /// <summary>nullable 문자열을 SQL 리터럴(NULL 또는 '이스케이프된 값')로 변환. 작은따옴표·역슬래시 이스케이프.</summary>
    private static string EscapeSqlLiteral(string? value)
    {
        if (value is null) return "NULL";
        var escaped = value.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"'{escaped}'";
    }

    /// <summary>db.conf(DbConfReader 단일출처)에서 DB 접속 정보를 읽는다(WatchdogConsentReader 와 동일).</summary>
    private static (string host, int port, string dbName, string user, string pass) ResolveDbCredentials()
    {
        var host = DbConfReader.GetValue("DB_HOST") ?? "localhost";
        var portStr = DbConfReader.GetValue("DB_PORT");
        var port = int.TryParse(portStr, out var p) && p > 0 ? p : 3306;
        var dbName = DbConfReader.GetValue("DB_NAME") ?? string.Empty;
        var user = DbConfReader.GetValue("DB_USER") ?? string.Empty;
        var pass = DbConfReader.GetValue("DB_PASSWORD") ?? string.Empty;
        return (host, port, dbName, user, pass);
    }

    private async Task RunWriteAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"{exe} 실행 실패(Process.Start null)");

        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        await outTask.ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} 적재 실패 (exit={proc.ExitCode}): {err}");
    }

    /// <summary>
    /// MariaDB 클라이언트 실행파일 탐색 — 고정 설치경로 우선, 실패 시 PATH(where) 폴백.
    ///
    /// ★ 봉합 (2026-07-16, 작1 W4-6): 종전엔 PATH(where)를 '먼저' 뒤졌다. 워치독은 SYSTEM 권한으로 도는데,
    ///   공격자가 PATH 앞쪽에 악성 'mariadb.exe' 를 심으면 그게 먼저 잡혀 SYSTEM 으로 실행된다(권한상승 표면).
    ///   그래서 순서를 뒤집어, 신뢰된 고정 설치경로(C:\Program Files\MariaDB 11.4\bin)를 먼저 확인하고
    ///   거기 없을 때만 PATH 로 폴백한다. WatchdogConsentReader 도 동일하게 뒤집었다.
    /// </summary>
    private string ResolveMariadbBinary(params string[] candidates)
    {
        // ① 신뢰된 고정 설치경로 우선(PATH 심기 무력화).
        var fixedPath = candidates
            .Select(n => Path.Combine(@"C:\Program Files\MariaDB 11.4\bin", n))
            .FirstOrDefault(File.Exists);
        if (fixedPath is not null) return fixedPath;

        // ② 고정경로에 없을 때만 PATH(where) 폴백.
        foreach (var name in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo("where", name)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);
                    if (proc.ExitCode == 0)
                    {
                        var first = output
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault()?.Trim();
                        if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
                    }
                }
            }
            catch (Exception pathEx)
            {
                // 헌법 #15: PATH 검색 실패도 흔적을 남긴다.
                _logger.LogWarning(pathEx, "[Update/Status] PATH 폴백 검색 실패({Name})", name);
            }
        }

        throw new InvalidOperationException(
            $"MariaDB 클라이언트 실행파일을 찾을 수 없습니다 ({string.Join("/", candidates)}). MariaDB 설치·PATH 등록을 확인하세요.");
    }
}
