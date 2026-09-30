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

    // ══════════════════════════════════════════════════════════════════════════════════
    // 20260929작3 갈래 R(9/30 재작업 · 설계 §12) — 「시도 기록」 새 표 local_update_attempts
    //
    //   ⬛ 갈래 W 의 「local_update_apply_status 에 consent_id 칸 + ADD COLUMN 자가 보강」은 폐기했다(작업지시서 §8).
    //     apply_status 는 옛 워치독이 직접 읽고 쓰는 **보호 표**다 — 칸을 늘리면 옛 워치독 교차검증 ②가
    //     그 릴리스와 이후 모든 릴리스를 막는다. 그래서 기존 표 3개는 무접촉 · 시도 기록은 새 표에만 둔다.
    //   · 한 [예](local_update_consents.id) = 한 시도 = 한 행. consent_id UNIQUE ⇒ 같은 [예]로 두 번 시작하면 DB 가 막는다.
    //   · 생성 경로 3개(설계 §12-4): DB-135 마이그 · 출하 DDL · 여기 자가생성. 셋 다 CREATE TABLE IF NOT EXISTS 만.
    //   🔴 아래 표식 사이 문자열은 HitPan.Tests/Integrity/WatchdogApplyStatusDdlGateTests(G-R3·G-R4)가
    //      **원문 그대로** 읽어 격리 DB 에서 돌리고, 칸·키·엔진을 DB-135·출하 DDL 과 대조한다.
    //      표식을 지우거나 옮기면 게이트가 FAIL 로 알린다.
    //   🔴 이 상수에 보호 표 이름을 넣지 마라 — 새 표만 만든다.
    // ##R-SCHEMA-BEGIN##
    internal const string AttemptsCreateSql =
        "CREATE TABLE IF NOT EXISTS `local_update_attempts` (" +
        "`id` bigint(20) NOT NULL AUTO_INCREMENT, " +
        "`consent_id` bigint(20) NOT NULL, " +
        "`update_version` varchar(20) NOT NULL, " +
        "`result` varchar(20) NOT NULL, " +
        "`detail` text DEFAULT NULL, " +
        "`started_at` datetime(3) NOT NULL, " +
        "`ended_at` datetime(3) DEFAULT NULL, " +
        "`created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3), " +
        "PRIMARY KEY (`id`), " +
        "UNIQUE KEY `uk_local_update_attempts_consent` (`consent_id`), " +
        "KEY `idx_local_update_attempts_ver` (`update_version`,`consent_id`)" +
        ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";
    // ##R-SCHEMA-END##

    /// <summary>
    /// 시도 행 열기 — 이 [예]로 적용을 **시작한다**는 기록(설계 §12-1 · 규칙 C ①).
    ///   INSERT(덮어쓰기 아님) — 같은 동의 id 로 두 번 쓰면 UNIQUE 위반 ⇒ <see cref="AttemptOpenResult.AlreadyUsed"/>
    ///   (그 [예]는 이미 썼다 — 적용하지 않는다 · G-R7). 그 밖의 실패는 <see cref="AttemptOpenResult.Failed"/>(규칙 Z-1 대상).
    /// 실패는 로그만(헌법 #15) — 예외를 밖으로 던지지 않는다(취소 제외).
    /// </summary>
    public async Task<AttemptOpenResult> OpenAttemptAsync(long consentId, string version, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("[Update/Attempt] db.conf 자격증명 부재 — 시도 행을 열지 못했습니다(버전 {V} · 동의 {Id})", version, consentId);
                return AttemptOpenResult.Failed;
            }
            if (!IsSafeVersionLiteral(version) || consentId <= 0)
            {
                _logger.LogWarning("[Update/Attempt] 안전하지 않은 값 — 시도 행 열기 거부(버전 '{V}' · 동의 {Id})", version, consentId);
                return AttemptOpenResult.Failed;
            }

            var sql = AttemptsCreateSql + " " + BuildOpenAttemptSql(consentId, version);
            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            var (exit, _, stderr) = await RunReadAsync(clientExe, args, ct).ConfigureAwait(false);
            if (exit == 0)
            {
                _logger.LogInformation("[Update/Attempt] 시도 행 열림 — 버전 {V} · 동의 {Id} · in_progress", version, consentId);
                return AttemptOpenResult.Opened;
            }
            if (IsDuplicateKeyError(stderr))
            {
                _logger.LogWarning("[Update/Attempt] 동의 {Id} 는 이미 시도한 [예]입니다(시도 표 UNIQUE) — 다시 적용하지 않습니다(버전 {V})", consentId, version);
                return AttemptOpenResult.AlreadyUsed;
            }
            _logger.LogWarning("[Update/Attempt] 시도 행 열기 실패(exit={E}) — 버전 {V} · 동의 {Id}: {Err}", exit, version, consentId, stderr);
            return AttemptOpenResult.Failed;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Attempt] 시도 행 열기 예외 — 버전 {V} · 동의 {Id}", version, consentId);
            return AttemptOpenResult.Failed;
        }
    }

    /// <summary>
    /// 시도 행 닫기 — 그 버전의 **진행 중(in_progress) 행만** 결과로 닫는다(설계 §12-1 ①).
    ///   동의 없는 경로(Normal·Emergency 채널)는 열린 행이 없어 0행 = 무해. 이미 닫힌 행은 안 건드린다
    ///   (오케스트레이터 종점이 먼저 닫았으면 그 결과가 남는다 · [4] F-3).
    /// 실패는 로그만(헌법 #15). 반환 = 문장 실행 성공 여부.
    /// </summary>
    public async Task<bool> CloseAttemptAsync(string version, string result, string? detail, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("[Update/Attempt] db.conf 자격증명 부재 — 시도 행을 닫지 못했습니다(버전 {V} · 결과 {R})", version, result);
                return false;
            }
            if (!IsSafeVersionLiteral(version))
            {
                _logger.LogWarning("[Update/Attempt] 안전하지 않은 버전 문자열 — 시도 행 닫기 거부: '{V}'", version);
                return false;
            }

            var sql = AttemptsCreateSql + " " + BuildCloseAttemptSql(version, result, detail);
            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            await RunWriteAsync(clientExe, args, ct).ConfigureAwait(false);
            _logger.LogInformation("[Update/Attempt] 시도 행 닫기 완료 — 버전 {V} · 결과 {R}(진행 중 행만)", version, result);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Attempt] 시도 행 닫기 실패 — 버전 {V} · 결과 {R}", version, result);
            return false;
        }
    }

    /// <summary>시도 행 INSERT 문(순수 — 시험이 문장을 본다). UPSERT 아님(G-R7).</summary>
    internal static string BuildOpenAttemptSql(long consentId, string version) =>
        "INSERT INTO `local_update_attempts` (consent_id, update_version, result, detail, started_at) " +
        $"VALUES ({consentId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{version}', 'in_progress', " +
        $"{EscapeSqlLiteral($"동의 {consentId.ToString(System.Globalization.CultureInfo.InvariantCulture)} 로 적용 시작")}, NOW(3));";

    /// <summary>시도 행 닫기 UPDATE 문(순수). 진행 중 행만 · ended_at=NOW(3).</summary>
    internal static string BuildCloseAttemptSql(string version, string result, string? detail) =>
        "UPDATE `local_update_attempts` " +
        $"SET result={EscapeSqlLiteral(result)}, detail={EscapeSqlLiteral(detail)}, ended_at=NOW(3) " +
        $"WHERE update_version='{version}' AND result='in_progress';";

    /// <summary>mariadb 클라이언트 stderr 가 UNIQUE 위반(1062)인가.</summary>
    internal static bool IsDuplicateKeyError(string? stderr) =>
        !string.IsNullOrEmpty(stderr)
        && (stderr.Contains("ERROR 1062", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 20260929작3 절W6 ② — 워치독 **기동 시** 남아 있는 in_progress 시도 행을 닫는다(설계 §5-4 · §6 · §12 R5).
    ///   기동 시점엔 적용이 돌 수 없다 — 적용은 이 프로세스 안에서만 돈다. 그러니 in_progress 는 전부
    ///   「전원 종료·강제 종료로 중단된 시도」다.
    ///   · 폴더를 되돌렸으면(<paramref name="restoredFolders"/>) → rolled_back 「업데이트 중 중단 — 이전 버전으로 되돌림」
    ///   · 아니면 → failed 「업데이트 중 중단」
    ///   detail 머리 「업데이트 중 중단」은 ERP 판정(설계 §7)이 알아보는 **고정 문구**다 — 바꾸려면 설계 개정 먼저.
    ///   ⬛ 갈래 R — 대상 표를 apply_status → local_update_attempts 로 바꿨다. apply_status 를 향한 UPDATE 는 0.
    ///      본사 보고도 0(깔때기 RecordApplyStatusAsync 를 거치지 않는다 · 설계 §12-1 ③).
    /// 실패는 로그만(헌법 #15·#20) — 기동을 막지 않는다. 반환 = 기록 성공 여부.
    /// </summary>
    public async Task<bool> CloseInterruptedAttemptsAsync(bool restoredFolders, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("[Update/Apply] db.conf 자격증명 부재 — 기동 시 중단된 시도 정리 생략");
                return false;
            }

            var sql = AttemptsCreateSql + " " + BuildCloseInterruptedSql(restoredFolders);
            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql.Replace("\"", "\\\"")}\" {dbName}";

            await RunWriteAsync(clientExe, args, ct).ConfigureAwait(false);
            _logger.LogInformation("[Update/Apply] 기동 시 중단된 시도 정리 완료 — in_progress → {R}",
                restoredFolders ? "rolled_back" : "failed");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Apply] 기동 시 중단된 시도 정리 실패 — 워치독 기동은 계속합니다");
            return false;
        }
    }

    /// <summary>고정 detail 문구(설계 §7 계약). ERP 판정이 머리 「업데이트 중 중단」을 알아본다.</summary>
    internal const string DetailInterrupted = "업데이트 중 중단";
    internal const string DetailInterruptedRestored = "업데이트 중 중단 — 이전 버전으로 되돌림";

    /// <summary>기동 정리 UPDATE 문(순수 — 시험이 문장을 본다). 대상 = 시도 표 · 진행 중 행만.</summary>
    internal static string BuildCloseInterruptedSql(bool restoredFolders)
    {
        var (result, detail) = restoredFolders
            ? ("rolled_back", DetailInterruptedRestored)
            : ("failed", DetailInterrupted);
        return "UPDATE `local_update_attempts` " +
               $"SET result='{result}', detail={EscapeSqlLiteral(detail)}, ended_at=NOW(3) " +
               "WHERE result='in_progress';";
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

/// <summary>
/// 20260929작3 갈래 R — 시도 행 열기 결과(<see cref="WatchdogStatusWriter.OpenAttemptAsync"/>).
///   Opened = 열림(적용 진행) · AlreadyUsed = 그 [예]는 이미 시도했다(UNIQUE 위반 — 적용 안 함 · 펜딩 해제) ·
///   Failed = 그 밖의 기록 실패(적용 보류 · 규칙 Z-1 대상).
/// </summary>
public enum AttemptOpenResult
{
    Opened,
    AlreadyUsed,
    Failed,
}
