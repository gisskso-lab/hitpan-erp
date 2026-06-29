using System.Diagnostics;
using System.Text;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 워치독 전용 백업 실행기 (작1 고리3, 2026-06-29).
///
/// 왜 BackupService(HitPan.Application)를 직접 안 쓰고 여기 별도로 두나:
///   워치독은 ERP 와 별개 프로세스(Windows 서비스)다. 업데이트 적용 직전에는 ERP API 가
///   재기동 중이거나 다운 상태일 수 있어, "ERP API 의 /api/backup/run(JWT 필요)" 을 호출하는
///   방식은 ① 인증 토큰을 워치독이 들고 있어야 하고 ② API 가 떠 있어야만 동작하는 의존이 생긴다.
///   업데이트의 핵심은 "백업이 확실히 성공해야만 적용 진입"(헌법 #20·데이터 무결성)이므로,
///   API 생존에 의존하지 않는 자족적(self-contained) mysqldump 직접 실행을 택한다.
///   DB 자격증명은 {app}\db.conf 단일출처(DbConfReader)에서 읽는다(설치 시 .iss 가 기록).
///
/// 로직은 BackupService.RunMysqldumpAsync 와 동일한 정신:
///   --single-transaction --routines --triggers --default-character-set=utf8mb4.
///   단 워치독은 backup_history DB 이력을 쓰지 않는다(업데이트 안전망용 스냅샷이며, ERP 의
///   사용자 백업 이력과 섞지 않는다 — 책임 분리). 산출 .sql 파일 경로를 반환해 롤백(고리4)이 쓴다.
///
/// 헌법 정합:
///   #15 — 모든 실패 경로 로그(침묵 금지) / #18·#22 — 로컬에서만, 본사 전송 0 / #34 — 정식 완성도.
/// </summary>
public sealed class WatchdogBackupRunner
{
    private readonly ILogger<WatchdogBackupRunner> _logger;

    public WatchdogBackupRunner(ILogger<WatchdogBackupRunner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 업데이트 적용 직전 안전 백업을 1회 실행한다.
    /// 성공 시 (true, 백업파일경로), 실패 시 (false, null) 을 반환한다.
    /// 호출부(UpdateOrchestrator.ApplyUpdateAsync)는 false 면 업데이트 전 과정을 물리적으로 차단한다.
    /// </summary>
    public async Task<(bool ok, string? backupFile)> RunPreUpdateBackupAsync(CancellationToken ct)
    {
        try
        {
            var (host, port, dbName, user, pass) = ResolveDbCredentials();
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("[Update/Backup] db.conf 에서 DB 자격증명을 읽지 못했습니다(DB_NAME/DB_USER 부재) — 백업 불가, 업데이트 차단");
                return (false, null);
            }

            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HitPan", "Updates", "pre-update-backup");
            Directory.CreateDirectory(backupDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outFile = Path.Combine(backupDir, $"hitpan_preupdate_{stamp}.sql");

            var dumpExe = ResolveMariadbBinary("mysqldump.exe", "mariadb-dump.exe");
            var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" --single-transaction --routines --triggers --default-character-set=utf8mb4 {dbName}";

            _logger.LogInformation("[Update/Backup] 적용 전 안전 백업 시작 → {File}", outFile);
            await RunDumpAsync(dumpExe, args, outFile, ct).ConfigureAwait(false);

            // 백업 성공 검증 — 파일이 존재하고 크기 > 0 이어야만 성공으로 인정(헌법 #34 정식 완성도).
            var fi = new FileInfo(outFile);
            if (!fi.Exists || fi.Length == 0)
            {
                _logger.LogError("[Update/Backup] 백업 파일이 비어 있음(크기 0) — 업데이트 차단: {File}", outFile);
                return (false, null);
            }

            _logger.LogInformation("[Update/Backup] 백업 성공 ({Size:N0} bytes): {File}", fi.Length, outFile);
            return (true, outFile);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 백업 실패 = 업데이트 물리적 차단(호출부가 false 로 차단).
            _logger.LogError(ex, "[Update/Backup] 적용 전 백업 실패 — 업데이트 전 과정 차단");
            return (false, null);
        }
    }

    /// <summary>db.conf(DbConfReader 단일출처)에서 DB 접속 정보를 읽는다. 키 부재 시 보수적 기본값.</summary>
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

    private async Task RunDumpAsync(string exe, string args, string outFile, CancellationToken ct)
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

        var errTask = proc.StandardError.ReadToEndAsync(ct);

        await using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await proc.StandardOutput.BaseStream.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} 실패 (exit={proc.ExitCode}): {err}");
    }

    /// <summary>
    /// MariaDB 클라이언트 실행파일 탐색(BackupService.ResolveMariadbBinary 와 동일 정신):
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
                _logger.LogWarning(pathEx, "[Update/Backup] PATH 검색 실패({Name}) — 기본 경로 폴백", name);
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
