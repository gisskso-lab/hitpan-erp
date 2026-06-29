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
    /// MariaDB 클라이언트 실행파일 탐색(WatchdogConsentReader.ResolveMariadbBinary 와 동일 정신):
    /// PATH(where) 우선, 실패 시 MariaDB 11.4 기본 설치 경로 폴백.
    /// </summary>
    private string ResolveMariadbBinary(params string[] candidates)
    {
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
                // 헌법 #15: PATH 검색 실패도 흔적을 남기고 폴백으로 진행.
                _logger.LogWarning(pathEx, "[Update/Status] PATH 검색 실패({Name}) — 기본 경로 폴백", name);
            }
        }

        var fallback = candidates
            .Select(n => Path.Combine(@"C:\Program Files\MariaDB 11.4\bin", n))
            .FirstOrDefault(File.Exists);
        if (fallback is not null) return fallback;

        throw new InvalidOperationException(
            $"MariaDB 클라이언트 실행파일을 찾을 수 없습니다 ({string.Join("/", candidates)}). MariaDB 설치·PATH 등록을 확인하세요.");
    }
}
