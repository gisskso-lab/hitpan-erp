using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 자료 백업·복원 서비스 (사장님 결재 2026-04-29).
/// 로컬 미러 백업: 1차 폴더 + 2차 폴더(외장/USB, 선택) 동시 저장.
/// §#18 본사 미수신 — 모든 파일 입출력은 고객사 로컬에서만.
/// §#3 INSERT ONLY — backup_history / restore_history 는 INSERT/UPDATE만, DELETE 없음.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly IDbConnection _db;
    private readonly ILogger<BackupService> _logger;

    public BackupService(IDbConnection db, ILogger<BackupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── 설정 ────────────────────────────────────────────
    public async Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT primary_path AS PrimaryPath, mirror_path AS MirrorPath,
                   schedule_mode AS ScheduleMode, retention_count AS RetentionCount,
                   last_run_at AS LastRunAt, last_status AS LastStatus, last_error AS LastError
            FROM backup_settings WHERE tenant_id = @TenantId
            """;
        var row = await _db.QuerySingleOrDefaultAsync<BackupSettingsDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);

        // 첫 진입(설정 행 없음)이면 기본값 INSERT 후 재조회
        if (row is null)
        {
            const string insertSql = """
                INSERT INTO backup_settings (tenant_id, primary_path, mirror_path, schedule_mode, retention_count)
                VALUES (@TenantId, @PrimaryPath, NULL, 'manual', 30)
                """;
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HitpanBackup");
            await _db.ExecuteAsync(new CommandDefinition(insertSql,
                new { TenantId = tenantId, PrimaryPath = defaultPath }, cancellationToken: ct))
                .ConfigureAwait(false);
            return new BackupSettingsDto
            {
                PrimaryPath = defaultPath,
                MirrorPath = null,
                ScheduleMode = "manual",
                RetentionCount = 30
            };
        }
        return row;
    }

    public async Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.PrimaryPath))
            throw new ArgumentException("1차 백업 폴더 경로는 필수입니다.");

        const string sql = """
            INSERT INTO backup_settings (tenant_id, primary_path, mirror_path, schedule_mode, retention_count)
            VALUES (@TenantId, @PrimaryPath, @MirrorPath, @ScheduleMode, @RetentionCount)
            ON DUPLICATE KEY UPDATE
                primary_path = VALUES(primary_path),
                mirror_path = VALUES(mirror_path),
                schedule_mode = VALUES(schedule_mode),
                retention_count = VALUES(retention_count)
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            req.PrimaryPath,
            req.MirrorPath,
            req.ScheduleMode,
            req.RetentionCount
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ─── 이력 ────────────────────────────────────────────
    public async Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT backup_id AS BackupId, started_at AS StartedAt, finished_at AS FinishedAt,
                   primary_file AS PrimaryFile, mirror_file AS MirrorFile,
                   file_size_bytes AS FileSizeBytes, status AS Status,
                   error_message AS ErrorMessage, triggered_by AS TriggeredBy
            FROM backup_history WHERE tenant_id = @TenantId
            ORDER BY started_at DESC LIMIT @Limit
            """;
        var rows = await _db.QueryAsync<BackupHistoryDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, Limit = limit }, cancellationToken: ct))
            .ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT restore_id AS RestoreId, started_at AS StartedAt, finished_at AS FinishedAt,
                   source_file AS SourceFile, pre_restore_backup AS PreRestoreBackup,
                   status AS Status, error_message AS ErrorMessage,
                   triggered_by_user AS TriggeredByUser
            FROM restore_history WHERE tenant_id = @TenantId
            ORDER BY started_at DESC LIMIT @Limit
            """;
        var rows = await _db.QueryAsync<RestoreHistoryDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, Limit = limit }, cancellationToken: ct))
            .ConfigureAwait(false);
        return rows.ToList();
    }

    // ─── 백업 실행 ───────────────────────────────────────
    public async Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(tenantId, ct).ConfigureAwait(false);
        var backupId = Guid.NewGuid().ToString();
        var startedAt = DateTime.Now;
        var stamp = startedAt.ToString("yyyyMMdd_HHmmss");
        var fileName = $"hitpan_backup_{stamp}.sql";

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 1) 이력 INSERT (running)
        const string insSql = """
            INSERT INTO backup_history (backup_id, tenant_id, started_at, status, triggered_by)
            VALUES (@BackupId, @TenantId, @StartedAt, 'running', @TriggeredBy)
            """;
        await _db.ExecuteAsync(new CommandDefinition(insSql,
            new { BackupId = backupId, TenantId = tenantId, StartedAt = startedAt, TriggeredBy = triggeredBy },
            cancellationToken: ct)).ConfigureAwait(false);

        try
        {
            // 2) 1차 폴더 보장
            Directory.CreateDirectory(settings.PrimaryPath);
            var primaryFile = Path.Combine(settings.PrimaryPath, fileName);

            // 3) mysqldump 실행
            await RunMysqldumpAsync(primaryFile, ct).ConfigureAwait(false);

            var fileInfo = new FileInfo(primaryFile);
            if (!fileInfo.Exists || fileInfo.Length == 0)
                throw new InvalidOperationException("백업 파일 생성 실패 (mysqldump 출력 없음).");

            // 4) 2차 미러 복사 (선택)
            string? mirrorFile = null;
            if (!string.IsNullOrWhiteSpace(settings.MirrorPath))
            {
                Directory.CreateDirectory(settings.MirrorPath);
                mirrorFile = Path.Combine(settings.MirrorPath, fileName);
                File.Copy(primaryFile, mirrorFile, overwrite: true);
            }

            // 5) 보관정책 — 일반 백업만 (pre_restore 는 제외)
            ApplyRetention(settings.PrimaryPath, settings.RetentionCount);
            if (!string.IsNullOrWhiteSpace(settings.MirrorPath))
                ApplyRetention(settings.MirrorPath!, settings.RetentionCount);

            // 6) 이력 UPDATE (success)
            const string okSql = """
                UPDATE backup_history
                SET finished_at = @FinishedAt, primary_file = @PrimaryFile, mirror_file = @MirrorFile,
                    file_size_bytes = @Size, status = 'success'
                WHERE backup_id = @BackupId
                """;
            await _db.ExecuteAsync(new CommandDefinition(okSql, new
            {
                FinishedAt = DateTime.Now,
                PrimaryFile = primaryFile,
                MirrorFile = mirrorFile,
                Size = fileInfo.Length,
                BackupId = backupId
            }, cancellationToken: ct)).ConfigureAwait(false);

            // 7) settings.last_* 갱신
            const string lastSql = """
                UPDATE backup_settings SET last_run_at = @Now, last_status = 'success', last_error = NULL
                WHERE tenant_id = @TenantId
                """;
            await _db.ExecuteAsync(new CommandDefinition(lastSql,
                new { Now = DateTime.Now, TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);

            return new RunBackupResponse
            {
                Success = true,
                BackupId = backupId,
                PrimaryFile = primaryFile,
                MirrorFile = mirrorFile,
                FileSizeBytes = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "백업 실패 tenantId={TenantId} backupId={BackupId}", tenantId, backupId);
            const string failSql = """
                UPDATE backup_history
                SET finished_at = @FinishedAt, status = 'failed', error_message = @Err
                WHERE backup_id = @BackupId
                """;
            await _db.ExecuteAsync(new CommandDefinition(failSql,
                new { FinishedAt = DateTime.Now, Err = ex.Message, BackupId = backupId },
                cancellationToken: ct)).ConfigureAwait(false);

            const string lastFailSql = """
                UPDATE backup_settings SET last_run_at = @Now, last_status = 'failed', last_error = @Err
                WHERE tenant_id = @TenantId
                """;
            await _db.ExecuteAsync(new CommandDefinition(lastFailSql,
                new { Now = DateTime.Now, Err = ex.Message, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

            return new RunBackupResponse { Success = false, BackupId = backupId, Error = ex.Message };
        }
    }

    // ─── 복원 실행 ───────────────────────────────────────
    public async Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default)
    {
        var restoreId = Guid.NewGuid().ToString();
        var startedAt = DateTime.Now;

        // 1) 복원 대상 .sql 파일 결정
        string sourceFile;
        if (!string.IsNullOrWhiteSpace(req.BackupId))
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);
            const string findSql = """
                SELECT primary_file FROM backup_history
                WHERE backup_id = @BackupId AND tenant_id = @TenantId AND status = 'success'
                """;
            var found = await _db.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(findSql,
                    new { BackupId = req.BackupId, TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(found))
                return new RestoreResponse { Success = false, Error = "선택한 백업 이력을 찾을 수 없습니다." };
            sourceFile = found;
        }
        else if (!string.IsNullOrWhiteSpace(req.ExternalFilePath))
        {
            sourceFile = req.ExternalFilePath;
        }
        else
        {
            return new RestoreResponse { Success = false, Error = "복원할 백업 파일이 지정되지 않았습니다." };
        }

        if (!File.Exists(sourceFile))
            return new RestoreResponse { Success = false, Error = $"파일을 찾을 수 없습니다: {sourceFile}" };

        // 2) 안전 확인 — 회사명 일치
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string companySql = "SELECT company_name FROM local_company WHERE tenant_id = @TenantId";
        var companyName = await _db.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(companySql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(companyName))
            return new RestoreResponse { Success = false, Error = "테넌트 회사명을 확인할 수 없습니다." };
        if (!string.Equals(req.ConfirmCompanyName?.Trim(), companyName.Trim(), StringComparison.Ordinal))
            return new RestoreResponse { Success = false, Error = "확인용 회사명이 일치하지 않습니다." };

        // 3) 복원 이력 INSERT (running)
        const string insSql = """
            INSERT INTO restore_history (restore_id, tenant_id, started_at, source_file, status, triggered_by_user)
            VALUES (@RestoreId, @TenantId, @StartedAt, @SourceFile, 'running', @UserId)
            """;
        await _db.ExecuteAsync(new CommandDefinition(insSql, new
        {
            RestoreId = restoreId, TenantId = tenantId, StartedAt = startedAt, SourceFile = sourceFile, UserId = userId
        }, cancellationToken: ct)).ConfigureAwait(false);

        try
        {
            // 4) 안전망 — 복원 직전 자동 백업 (pre_restore_*.sql)
            var settings = await GetSettingsAsync(tenantId, ct).ConfigureAwait(false);
            Directory.CreateDirectory(settings.PrimaryPath);
            var preStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var preFile = Path.Combine(settings.PrimaryPath, $"hitpan_pre_restore_{preStamp}.sql");
            await RunMysqldumpAsync(preFile, ct).ConfigureAwait(false);

            // 5) mysql.exe < sourceFile 으로 복원
            await RunMysqlImportAsync(sourceFile, ct).ConfigureAwait(false);

            const string okSql = """
                UPDATE restore_history
                SET finished_at = @FinishedAt, pre_restore_backup = @Pre, status = 'success'
                WHERE restore_id = @RestoreId
                """;
            await _db.ExecuteAsync(new CommandDefinition(okSql,
                new { FinishedAt = DateTime.Now, Pre = preFile, RestoreId = restoreId },
                cancellationToken: ct)).ConfigureAwait(false);

            return new RestoreResponse { Success = true, RestoreId = restoreId, PreRestoreBackup = preFile };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "복원 실패 tenantId={TenantId} restoreId={RestoreId}", tenantId, restoreId);
            const string failSql = """
                UPDATE restore_history
                SET finished_at = @FinishedAt, status = 'failed', error_message = @Err
                WHERE restore_id = @RestoreId
                """;
            await _db.ExecuteAsync(new CommandDefinition(failSql,
                new { FinishedAt = DateTime.Now, Err = ex.Message, RestoreId = restoreId },
                cancellationToken: ct)).ConfigureAwait(false);
            return new RestoreResponse { Success = false, RestoreId = restoreId, Error = ex.Message };
        }
    }

    // ─── 외부 프로세스 헬퍼 ─────────────────────────────
    private async Task RunMysqldumpAsync(string outFile, CancellationToken ct)
    {
        var (host, port, db, user, pass) = ResolveDbCredentials();
        var dumpExe = ResolveMariadbBinary("mysqldump.exe", "mariadb-dump.exe");
        var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" --single-transaction --routines --triggers --default-character-set=utf8mb4 {db}";
        await RunProcessRedirectedAsync(dumpExe, args, outFile, ct).ConfigureAwait(false);
    }

    private async Task RunMysqlImportAsync(string sqlFile, CancellationToken ct)
    {
        var (host, port, db, user, pass) = ResolveDbCredentials();
        var mysqlExe = ResolveMariadbBinary("mysql.exe", "mariadb.exe");
        var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" --default-character-set=utf8mb4 {db}";
        await RunProcessRedirectedFromFileAsync(mysqlExe, args, sqlFile, ct).ConfigureAwait(false);
    }

    private static async Task RunProcessRedirectedAsync(string exe, string args, string outFile, CancellationToken ct)
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
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} 실행 실패");
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

    private static async Task RunProcessRedirectedFromFileAsync(string exe, string args, string inFile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} 실행 실패");
        var errTask = proc.StandardError.ReadToEndAsync(ct);

        await using (var fs = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await fs.CopyToAsync(proc.StandardInput.BaseStream, ct).ConfigureAwait(false);
        }
        proc.StandardInput.Close();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} 실패 (exit={proc.ExitCode}): {err}");
    }

    private (string host, int port, string db, string user, string pass) ResolveDbCredentials()
    {
        var conn = _db.ConnectionString;
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("DB ConnectionString 비어 있음");
        // "Server=localhost;Port=3306;Database=hitpan_erp;Uid=hitpan;Pwd=Hitpan2025!;..."
        string Get(string key)
        {
            foreach (var part in conn.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var k = part[..idx].Trim();
                var v = part[(idx + 1)..].Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return "";
        }
        var host = Get("Server"); if (string.IsNullOrEmpty(host)) host = "localhost";
        var portStr = Get("Port"); var port = int.TryParse(portStr, out var p) ? p : 3306;
        var db = Get("Database");
        var user = Get("Uid"); if (string.IsNullOrEmpty(user)) user = Get("User Id");
        var pass = Get("Pwd"); if (string.IsNullOrEmpty(pass)) pass = Get("Password");
        return (host, port, db, user, pass);
    }

    private static string ResolveMariadbBinary(params string[] candidates)
    {
        // 우선 PATH 검색 (Windows where), 실패 시 MariaDB 11.4 기본 경로
        foreach (var name in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo("where", name)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);
                    if (proc.ExitCode == 0)
                    {
                        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                        if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
                    }
                }
            }
            catch (Exception pathEx)
            {
                // PATH 검색 실패는 fallback 으로 진행 (헌법 #15 정합 로그)
                Console.Error.WriteLine($"[BackupService] PATH search failed: {pathEx.Message}");
            }
        }
        // Fallback — MariaDB 11.4 기본 설치 경로
        var fallback = candidates
            .Select(n => Path.Combine(@"C:\Program Files\MariaDB 11.4\bin", n))
            .FirstOrDefault(File.Exists);
        if (fallback is not null) return fallback;

        throw new InvalidOperationException(
            $"MariaDB 클라이언트 실행파일을 찾을 수 없습니다 ({string.Join("/", candidates)}). MariaDB가 설치되어 있고 PATH에 등록되었는지 확인하세요.");
    }

    private static void ApplyRetention(string folder, int keep)
    {
        if (!Directory.Exists(folder) || keep <= 0) return;
        // 일반 백업만 대상 — pre_restore_*.sql 은 제외
        var files = Directory.GetFiles(folder, "hitpan_backup_*.sql")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();
        foreach (var old in files.Skip(keep))
        {
            try { old.Delete(); }
            catch (IOException ex) { /* 사용 중이면 다음 회차에 정리 */ _ = ex; }
        }
    }

    // ─── DB 연결 ────────────────────────────────────────
    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dc)
        {
            await dc.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}
