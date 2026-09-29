using System.Diagnostics;
using System.Text;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 워치독 로컬 동의 리더 (작1 고리2 워치독 측, 2026-06-29).
///
/// A안(2026-06-29 결재): 업데이트 동의는 본사를 거치지 않고 고객 PC 안에서 완결된다(헌법 #30).
///   ① ERP 로그인 시 새 버전(Major) 동의 팝업 → ② ERP 가 로컬 DB local_update_consents(DB-82)에 INSERT →
///   ③ 워치독(고객 PC 별개 프로세스)이 본 테이블을 로컬에서 SELECT 해 적용 가부를 판단한다.
///
/// 왜 MySqlConnection 패키지를 안 붙이고 mariadb 클라이언트 CLI 로 읽나(WatchdogBackupRunner 와 동일 정신):
///   워치독 프로젝트(.csproj)는 DB 드라이버 의존이 0이다(현재 mysqldump CLI 만 사용). 동의 1행 조회를 위해
///   패키지를 새로 추가하면 의존·빌드 표면이 커진다. 백업 실행기가 이미 db.conf 자격증명 + MariaDB 클라이언트
///   CLI 직접 실행 패턴을 쓰므로, 같은 자족(self-contained) 방식으로 동의도 읽는다 — API 생존에 의존 0(헌법 #30).
///   헌법 #16(MySqlConnection + Task.WhenAll 금지)은 단일 동기 쿼리라 무관하나, 애초에 드라이버를 안 쓴다.
///
/// 헌법 정합:
///   #1 — 추가만(신규 클래스) / #15 — 모든 실패 경로 로그(침묵 금지) /
///   #18·#22·#30 — 로컬 DB 만 읽음, 본사 전송·의존 0 / #34 — 정식 완성도.
/// </summary>
public sealed class WatchdogConsentReader
{
    private readonly ILogger<WatchdogConsentReader> _logger;

    public WatchdogConsentReader(ILogger<WatchdogConsentReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 해당 update_version 의 '최신' 동의 결과를 로컬 DB(local_update_consents)에서 읽는다.
    ///   approve → ConsentDecision.Approve / reject → Reject / 행 없음 → None(아직 미응답) / 조회 실패 → Error.
    /// 같은 버전에 거부 후 재로그인 시 승인처럼 여러 행이 쌓일 수 있으므로 consented_at(동률 시 id) 최신 1행만 본다.
    /// </summary>
    public async Task<ConsentDecision> ReadLatestAsync(string updateVersion, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Consent] db.conf 에서 DB 자격증명을 읽지 못했습니다(DB_NAME/DB_USER 부재) — 동의 조회 불가");
                return ConsentDecision.Error;
            }

            // SQL 인젝션 방지: update_version 은 manifest.Version(서버 발행 SemVer)이며 사용자 입력이 아니다.
            //   그래도 보수적으로 SemVer 형식([0-9.]+ 만 허용)을 벗어나면 조회를 거부한다(헌법 #25 안전하게).
            if (!IsSafeVersionLiteral(updateVersion))
            {
                _logger.LogError("[Update/Consent] 안전하지 않은 버전 문자열 — 동의 조회 거부: '{V}'", updateVersion);
                return ConsentDecision.Error;
            }

            // mariadb 클라이언트로 단일 행 조회. -N(컬럼 헤더 제거) -B(탭 구분, raw) 로 한 줄 결과만 받는다.
            //   ORDER BY consented_at DESC, id DESC LIMIT 1 → 동일 버전 다중 응답 중 최신 1건.
            var sql = $"SELECT action FROM local_update_consents WHERE update_version = '{updateVersion}' " +
                      "ORDER BY consented_at DESC, id DESC LIMIT 1;";

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql}\" {dbName}";

            var output = await RunQueryAsync(clientExe, args, ct).ConfigureAwait(false);
            var action = output.Trim();

            if (string.IsNullOrEmpty(action))
            {
                _logger.LogDebug("[Update/Consent] 버전 {V} 동의 기록 없음 — 미응답(적용 보류)", updateVersion);
                return ConsentDecision.None;
            }

            if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Update/Consent] 버전 {V} 로컬 동의=승인 — 적용 진입 가능", updateVersion);
                return ConsentDecision.Approve;
            }

            if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Update/Consent] 버전 {V} 로컬 동의=거부 — 적용 안 함(다음 로그인 재제시는 ERP 몫)", updateVersion);
                return ConsentDecision.Reject;
            }

            _logger.LogWarning("[Update/Consent] 버전 {V} action 값 해석 불가('{A}') — 보류 처리", updateVersion, action);
            return ConsentDecision.None;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 동의 조회 실패는 적용을 시작하지 않고(Error) 다음 주기 재시도(보수적).
            _logger.LogError(ex, "[Update/Consent] 로컬 동의 조회 실패 — 적용 보류, 다음 주기 재시도");
            return ConsentDecision.Error;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    // 20260929작3 절W3 — 규칙 C 판독: 「최신 동의 1건」 + 「그 버전 결과행이 이미 쓴 동의 id」
    //
    //   ReadLatestAsync(위)는 남긴다(헌법 #1) — 다만 규칙 C 경로(Worker.ConsumeConsentForMajorAsync)는
    //   이 메서드만 쓴다.
    //   🔴 [3-V] 병렬이슈 02 봉합(PM 결재 9/29) — 정렬은 **id DESC 만**. 설계 §5-1 초안은
    //      「consented_at DESC, id DESC」였으나 consented_at 은 API 프로세스 시계(DateTime.Now)라
    //      PC 시계가 뒤로 가면 새 [예](id 더 큼)가 옛 행 뒤로 밀려 「이미 쓴 [예]」로 무시된다.
    //      id 는 한 표 안의 순번이라 시계와 무관하다(설계 §3 「왜 시각이 아니라 id 인가」와 같은 이유).
    //   🔴 HitPan.Tests/Integrity/WatchdogApplyStatusDdlGateTests 가 아래 표식 사이 문자열을 원문 그대로
    //      읽어 격리 DB 에서 돌린다({0} = 버전 리터럴).
    // ##W3-READ-BEGIN##
    internal const string LatestWithUsageSqlFormat =
        "SELECT c.id, c.action, COALESCE(a.consent_id, 0) FROM local_update_consents c " +
        "LEFT JOIN local_update_apply_status a ON a.applied_version = c.update_version " +
        "WHERE c.update_version = '{0}' ORDER BY c.id DESC LIMIT 1;";
    // ##W3-READ-END##

    // 표·칸 보강(자가생성 DDL)을 이 프로세스에서 한 번 성공했는가. 성공 뒤엔 매 루프 DDL 을 다시 던지지 않는다.
    private bool _applyStatusSchemaEnsured;

    /// <summary>
    /// 20260929작3 절W3 — 규칙 C(설계 §3)의 입력 3값을 한 번의 조회로 읽는다.
    ///   반환: (판정, 최신 동의 id, 그 버전 결과행의 consent_id — 행 없음·NULL = 0).
    ///   · 행 없음 → None(0,0) · 조회 실패·해석 실패 → Error(적용 보류 · 다음 루프 재시도).
    ///   🔴 해석 실패를 0 으로 읽지 않는다(PM 지시 9/29) — used=0 으로 읽으면 쓴 [예]가 「새 [예]」로 둔갑해
    ///      무질문 재시도가 된다(B-3 재발). 모르면 적용하지 않는 쪽.
    /// 🔴 결과표(local_update_apply_status)는 워치독이 첫 기록 때 만드는 표라 옛 PC 에 없을 수 있다 ⇒
    ///    조회 전에 자가생성 DDL(WatchdogStatusWriter.ApplyStatusSchemaSql)을 **한 번** 따로 던진다.
    ///    그 DDL 이 실패해도(권한 등) 조회는 시도한다 — 표·칸이 이미 있으면 조회는 된다.
    /// </summary>
    public async Task<ConsentUsage> ReadLatestWithUsageAsync(string updateVersion, CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Consent] db.conf 에서 DB 자격증명을 읽지 못했습니다(DB_NAME/DB_USER 부재) — 동의 조회 불가");
                return ConsentUsage.Error;
            }
            if (!IsSafeVersionLiteral(updateVersion))
            {
                _logger.LogError("[Update/Consent] 안전하지 않은 버전 문자열 — 동의 조회 거부: '{V}'", updateVersion);
                return ConsentUsage.Error;
            }

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");

            if (!_applyStatusSchemaEnsured)
            {
                try
                {
                    var ddl = WatchdogStatusWriter.ApplyStatusSchemaSql;
                    var ddlArgs = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{ddl.Replace("\"", "\\\"")}\" {dbName}";
                    await RunQueryAsync(clientExe, ddlArgs, ct).ConfigureAwait(false);
                    _applyStatusSchemaEnsured = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ddlEx)
                {
                    _logger.LogWarning(ddlEx, "[Update/Consent] 결과표 자가생성·칸 보강 실패 — 조회는 계속 시도합니다(표·칸이 이미 있으면 된다)");
                }
            }

            var sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, LatestWithUsageSqlFormat, updateVersion);
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql}\" {dbName}";
            var output = (await RunQueryAsync(clientExe, args, ct).ConfigureAwait(false)).Trim();

            var usage = ParseLatestWithUsage(output);
            if (usage.Decision == ConsentDecision.Error)
                _logger.LogWarning("[Update/Consent] 버전 {V} 동의 판독 결과를 해석하지 못했습니다('{Raw}') — 적용 보류", updateVersion, output);
            else
                _logger.LogDebug("[Update/Consent] 버전 {V} 판독 — {D} · 최신 id {Id} · 이미 쓴 id {Used}",
                    updateVersion, usage.Decision, usage.ConsentId, usage.UsedConsentId);
            return usage;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update/Consent] 로컬 동의 판독 실패 — 적용 보류, 다음 주기 재시도");
            return ConsentUsage.Error;
        }
    }

    /// <summary>
    /// 판독 한 줄(탭 구분 `id  action  used`)을 해석한다(순수 — 시험이 직접 부른다).
    ///   빈 출력 = 동의 없음(None) · 칸 수·숫자·음수 이상 = Error · action 이 approve/reject 가 아니면 None(종전과 같음).
    /// </summary>
    internal static ConsentUsage ParseLatestWithUsage(string output)
    {
        var line = output.Trim();
        if (line.Length == 0) return ConsentUsage.NoConsent;

        var parts = line.Split('\t');
        if (parts.Length != 3) return ConsentUsage.Error;
        if (!long.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
            return ConsentUsage.Error;
        if (!long.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var used) || used < 0)
            return ConsentUsage.Error;

        var action = parts[1].Trim();
        if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
            return new ConsentUsage(ConsentDecision.Approve, id, used);
        if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
            return new ConsentUsage(ConsentDecision.Reject, id, used);
        return new ConsentUsage(ConsentDecision.None, id, used);
    }

    /// <summary>
    /// W4-6 — 로컬에 "발견해 적재해 둔" Major 새버전(local_update_status)이 있으면 그 버전 문자열을 돌려준다.
    ///
    /// 왜 필요한가(펜딩 소실 버그): Worker._pendingConsentUpdate 는 인메모리라, 워치독이 재시작되면 Major
    ///   펜딩이 날아간다. 그런데 업데이트 평가는 확인 게이트(Worker._lastUpdateCheckUtc)로 묶여 있어, 방금
    ///   평가했으면 다음 주기까지 재발견이 막혀 동의 폴링이 그동안 죽는다. 그래서 기동 시 이 테이블을 보고
    ///   '발견해 둔 Major 가 있으면' 확인 게이트를 즉시 만료시켜(재조회) manifest 를 다시 받아 펜딩을 정상 복원한다.
    ///   ※ 20260807작2 N-10: 그 게이트가 "하루 1회(_lastUpdateCheckDate)" 에서 N시간 주기(기본 60분)로 바뀌었다.
    ///     즉 이 복원이 메우는 공백은 종전 최대 하루에서 최대 N시간으로 줄었으나, 이 함수의 역할은 그대로다.
    ///
    /// ★ 여기서 부분 manifest 를 만들지 않는다 — local_update_status 에는 Sha256·Signature 가 없어(적용에 필수)
    ///   여기서 만든 manifest 로는 안전하게 적용할 수 없다. '재조회 트리거' 역할만 한다(호출부가 게이트를 연다).
    ///   조회 실패·행 없음이면 null(재조회를 강제하지 않음).
    /// </summary>
    public async Task<string?> TryGetPendingMajorVersionAsync(CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("[Update/Consent] db.conf 자격증명 부재 — 펜딩 Major 복원 조회 생략");
                return null;
            }

            // 최신 발견 1건 중 Major 채널만. UpsertLatestAsync 가 "최신 1건"만 유지하므로 사실상 0/1행.
            var sql = "SELECT latest_version FROM local_update_status " +
                      "WHERE update_channel = 'Major' ORDER BY discovered_at DESC, id DESC LIMIT 1;";

            var clientExe = ResolveMariadbBinary("mariadb.exe", "mysql.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" -N -B --default-character-set=utf8mb4 -e \"{sql}\" {dbName}";

            var output = (await RunQueryAsync(clientExe, args, ct).ConfigureAwait(false)).Trim();
            if (string.IsNullOrEmpty(output)) return null;

            // 안전 리터럴 검증(방어). 형식이 이상하면 복원 트리거하지 않는다.
            return IsSafeVersionLiteral(output) ? output : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Consent] 펜딩 Major 복원 조회 실패 — 재조회 강제하지 않음");
            return null;
        }
    }

    /// <summary>SemVer 류 안전 리터럴만 허용(숫자·점만). SQL 리터럴 삽입 안전성 보강.</summary>
    private static bool IsSafeVersionLiteral(string v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.Length > 20) return false;
        foreach (var ch in v)
            if (!char.IsDigit(ch) && ch != '.') return false;
        return true;
    }

    /// <summary>db.conf(DbConfReader 단일출처)에서 DB 접속 정보를 읽는다(WatchdogBackupRunner 와 동일).</summary>
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

    private async Task<string> RunQueryAsync(string exe, string args, CancellationToken ct)
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
        var err = await errTask.ConfigureAwait(false);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} 조회 실패 (exit={proc.ExitCode}): {err}");

        return stdout;
    }

    /// <summary>
    /// MariaDB 클라이언트 실행파일 탐색 — 고정 설치경로 우선, 실패 시 PATH(where) 폴백.
    ///
    /// ★ 봉합 (2026-07-16, 작1 W4-6): 종전 PATH 우선을 뒤집었다. 워치독은 SYSTEM 권한이라 PATH 앞쪽에
    ///   심긴 악성 mariadb.exe 가 먼저 잡히면 권한상승이 된다. 신뢰된 고정경로를 먼저 확인한다
    ///   (WatchdogStatusWriter 와 동일 봉합).
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
                _logger.LogWarning(pathEx, "[Update/Consent] PATH 폴백 검색 실패({Name})", name);
            }
        }

        throw new InvalidOperationException(
            $"MariaDB 클라이언트 실행파일을 찾을 수 없습니다 ({string.Join("/", candidates)}). MariaDB 설치·PATH 등록을 확인하세요.");
    }
}

/// <summary>
/// 20260929작3 — 규칙 C 입력(설계 §3): 최신 동의 판정 · 그 동의 id · 그 버전 결과행이 이미 쓴 동의 id(없음=0).
/// </summary>
public readonly record struct ConsentUsage(ConsentDecision Decision, long ConsentId, long UsedConsentId)
{
    public static ConsentUsage Error => new(ConsentDecision.Error, 0, 0);
    public static ConsentUsage NoConsent => new(ConsentDecision.None, 0, 0);

    /// <summary>「새 [예]」인가 — 최신이 approve 이고 그 id 가 이미 쓴 id 보다 크다.</summary>
    public bool IsFreshApprove => Decision == ConsentDecision.Approve && ConsentId > UsedConsentId;
}

/// <summary>로컬 동의 조회 결과(고리2 워치독 측 판단 입력).</summary>
public enum ConsentDecision
{
    /// <summary>해당 버전 동의 기록 없음 — 아직 고객이 ERP 에서 응답 안 함(적용 보류).</summary>
    None,
    /// <summary>최신 동의=승인 — 적용 진입 가능(영업시간 게이트는 별도).</summary>
    Approve,
    /// <summary>최신 동의=거부 — 적용 안 함(재제시는 ERP 몫).</summary>
    Reject,
    /// <summary>조회 자체 실패(자격증명·DB·클라이언트 부재) — 보수적으로 적용 보류, 다음 주기 재시도.</summary>
    Error
}
