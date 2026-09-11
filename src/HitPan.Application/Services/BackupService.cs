using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Dapper;
using HitPan.Application.Common;
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
            // 20260911작5 ③ — 서비스 계정은 문서 폴더가 빈 값이라 Path.Combine("", "HitpanBackup") = 상대 경로가 저장되고
            //   작업 폴더(C:\Windows\System32) 아래에 백업이 생겼다(선행검증 20260911 격리재현 §6 P2).
            //   문서 폴더가 비었거나 절대 경로가 아니거나 Windows 폴더 하위면 %ProgramData%\HitPan\Backup (K2).
            var defaultPath = ResolveBackupFolder(null);
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

        // 20260911작5 ③ K3 — 이미 저장된 쓸 수 없는 경로(빈 값 · 상대 경로 · Windows 폴더 하위)는
        //   실행 시 교정하고 설정 행도 교정해 저장한다. 쓸 수 있는 경로(사용자가 고른 값)는 그대로 둔다.
        var resolvedPrimary = ResolveBackupFolder(row.PrimaryPath);
        if (!string.Equals(resolvedPrimary, row.PrimaryPath, StringComparison.Ordinal))
        {
            _logger.LogWarning("백업 1차 폴더 설정이 쓸 수 없는 경로라 교정합니다 tenantId={TenantId} 저장값={Stored} 교정={Resolved}",
                tenantId, row.PrimaryPath, resolvedPrimary);
            const string fixSql = "UPDATE backup_settings SET primary_path = @PrimaryPath WHERE tenant_id = @TenantId";
            await _db.ExecuteAsync(new CommandDefinition(fixSql,
                new { PrimaryPath = resolvedPrimary, TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);
            row.PrimaryPath = resolvedPrimary;
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
            //    20260911작5 C-8 — 코드가 정한 기본 폴더(%ProgramData%\HitPan\Backup)만 제한 권한으로 만들고 검사한다.
            EnsureBackupFolder(settings.PrimaryPath);
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

            // 5) 보관정책 — 20260911작5 C-9: 성공 기록(6·7) 뒤로 옮겼다. 끝난 백업을 먼저 지우지 않는다.

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

            // 8) 보관정책 — 일반 백업만 (pre_restore 는 제외) · 성공 기록 뒤 (C-9)
            //    정리 실패는 이미 끝난 백업을 실패로 바꾸지 않는다(다음 회차에 다시 정리).
            try
            {
                ApplyRetention(settings.PrimaryPath, settings.RetentionCount);
                if (!string.IsNullOrWhiteSpace(settings.MirrorPath))
                    ApplyRetention(settings.MirrorPath!, settings.RetentionCount);
            }
            catch (IOException retEx)
            {
                _logger.LogWarning(retEx, "보관정책 정리 실패(백업은 성공) tenantId={TenantId} backupId={BackupId}", tenantId, backupId);
            }
            catch (UnauthorizedAccessException retEx)
            {
                _logger.LogWarning(retEx, "보관정책 정리 실패(백업은 성공) tenantId={TenantId} backupId={BackupId}", tenantId, backupId);
            }

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
            // 20260911작5 ④ — 요청이 끊기면(ct 취소) 실패 UPDATE 도 같은 ct 로 돌아 다시 예외 → 이력이 'running' 에 고정됐다
            //   (선행검증 20260911 격리재현 §4 X3). 기록은 취소되지 않은 토큰으로 · 연결이 닫혔으면 다시 열고 ·
            //   기록 자체가 실패해도 원래 실패 결과를 돌려준다. 취소면 이유를 「요청이 끊겨」로 구분한다.
            var canceled = ex is OperationCanceledException && ct.IsCancellationRequested;
            var reason = canceled ? BackupCanceledReason : ex.Message;
            if (canceled)
                _logger.LogWarning(ex, "백업 중단 — 요청이 끊김 tenantId={TenantId} backupId={BackupId}", tenantId, backupId);
            else
                _logger.LogError(ex, "백업 실패 tenantId={TenantId} backupId={BackupId}", tenantId, backupId);

            try
            {
                await EnsureOpenForRecordAsync().ConfigureAwait(false);

                // C-9 — 이미 끝난(success) 기록을 'failed' 로 덮지 않는다.
                const string failSql = """
                    UPDATE backup_history
                    SET finished_at = @FinishedAt, status = 'failed', error_message = @Err
                    WHERE backup_id = @BackupId AND status = 'running'
                    """;
                var marked = await _db.ExecuteAsync(new CommandDefinition(failSql,
                    new { FinishedAt = DateTime.Now, Err = reason, BackupId = backupId },
                    cancellationToken: CancellationToken.None)).ConfigureAwait(false);

                if (marked > 0)
                {
                    const string lastFailSql = """
                        UPDATE backup_settings SET last_run_at = @Now, last_status = 'failed', last_error = @Err
                        WHERE tenant_id = @TenantId
                        """;
                    await _db.ExecuteAsync(new CommandDefinition(lastFailSql,
                        new { Now = DateTime.Now, Err = reason, TenantId = tenantId },
                        cancellationToken: CancellationToken.None)).ConfigureAwait(false);
                }
            }
            catch (Exception recordEx)
            {
                // #15 — 실패 기록을 못 남겨도 호출자에게는 원래 실패를 돌려준다.
                _logger.LogWarning(recordEx, "백업 실패 기록을 남기지 못했습니다 tenantId={TenantId} backupId={BackupId}", tenantId, backupId);
            }

            return new RunBackupResponse { Success = false, BackupId = backupId, Error = reason };
        }
    }

    // ─── 복원 실행 ───────────────────────────────────────
    public async Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default)
    {
        // 🔴 20260911작5 C-11 사장님 결정 (가) 2026-09-11 — 1.3.40 에서 복원은 잠시 막는다.
        //   비번을 고치면 복원이 처음으로 실제로 돈다. 트랜잭션 없는 가져넣기가 요청 100초에 끊기면 DB 가 반쪽이 된다(병렬이슈26).
        //   비번 확인·파일 확인·복원 이력 INSERT·사전 백업·가져넣기 **모두보다 먼저** 막는다.
        //   아래 옛 복원 코드는 지우지 않는다(#1) — 안전장치를 보강하는 다음 업데이트에서 이 차단만 걷는다.
        if (RestoreTemporarilyClosed)
        {
            _logger.LogWarning("복원 요청을 막았습니다(1.3.40 잠시 막기) tenantId={TenantId} userId={UserId}", tenantId, userId);
            return new RestoreResponse { Success = false, Error = RestoreClosedNotice };
        }

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
        // 20260911작5 ① — 자격증명은 연결 문자열이 아니라 설정 원본(TenantConfigReader)에서 읽는다.
        //   열린 연결의 ConnectionString 은 드라이버가 Password 를 빼고 돌려주고, 열기 전 문자열은 키가 User= 라
        //   옛 파서(Uid/User Id)로는 사용자명이 빈 값이다 — 어느 시점에 읽어도 하나가 빠진다(선행검증 §2).
        // 20260911작5 ② K1 — 비번은 명령줄 "-p…" 가 아니라 자식 환경변수 MYSQL_PWD 로만 넘긴다.
        //   "-p" 가 없으면 빈 비번이 와도 입력 요청이 구조적으로 불가(선행검증 §5 K1-3: 2초 안에 1045).
        var (host, port, db, user, pass) = ResolveDbCredentialsFromConfig();
        var dumpExe = ResolveDumpBinary();
        _logger.LogInformation("백업 덤프 실행파일: {DumpExe}", dumpExe);
        var args = $"-h {host} -P {port} -u {user} --single-transaction --routines --triggers --default-character-set=utf8mb4 {db}";
        await RunProcessRedirectedAsync(dumpExe, args, pass, outFile, ct).ConfigureAwait(false);
    }

    private async Task RunMysqlImportAsync(string sqlFile, CancellationToken ct)
    {
        var (host, port, db, user, pass) = ResolveDbCredentials();
        var mysqlExe = ResolveMariadbBinary("mysql.exe", "mariadb.exe");
        var args = $"-h {host} -P {port} -u {user} \"-p{pass}\" --default-character-set=utf8mb4 {db}";
        await RunProcessRedirectedFromFileAsync(mysqlExe, args, sqlFile, ct).ConfigureAwait(false);
    }

    private async Task RunProcessRedirectedAsync(string exe, string args, string password, string outFile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            // 20260911작5 ② — 표준입력을 연결하고 곧바로 닫는다: 입력을 기다릴 곳을 남기지 않는다.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        // K1 — 이 자식 프로세스의 환경에만 넣는다. 부모(API) 프로세스 환경변수는 건드리지 않는다.
        psi.Environment["MYSQL_PWD"] = password;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} 실행 실패");
        try
        {
            proc.StandardInput.Close();
            var errTask = proc.StandardError.ReadToEndAsync(ct);

            await using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await proc.StandardOutput.BaseStream.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var err = await errTask.ConfigureAwait(false);
            // 병렬이슈28 C28-1 — 성공·실패는 exit 코드로만 가른다. MariaDB 클라이언트의
            //   「ssl-verify-server-cert is disabled … passwordless login」 경고 줄은 실패 이유에 섞지 않는다.
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(exe)} 실패 (exit={proc.ExitCode}): {WithoutPasswordlessTlsWarning(err)}");
        }
        finally
        {
            // 20260911작5 ② — 취소·예외로 빠질 때 덤프가 살아 있으면 자식(숨은 콘솔)까지 끝낸다.
            //   `using var proc` 은 프로세스를 죽이지 않는다 → 고아 덤프가 부모 핸들을 쥔 채 남았다(선행검증 §4 X1·X4).
            StopProcessTree(proc, exe);
        }
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

    // ─── 20260911작5 백업 비밀번호 결함 핫픽스 (1.3.40) ─────────────
    //   근거: 작업지시서 docs/운영기록/20260911작5_백업비번결함_핫픽스_작업지시서.md §3 · §11-1 · §11-2
    //   게이트: HitPan.Tests/Integrity/BackupCredentialGateTests.cs (G-BK1~6)

    private const string BackupCanceledReason = "요청이 끊겨 백업을 중단했습니다. 다시 시도해 주세요.";
    private const string RestoreClosedNotice = "복원은 안전장치를 보강한 다음 업데이트에서 열립니다.";

    /// <summary>C-11 (가) — 1.3.40 복원 잠시 막기. 상수가 아닌 필드로 두는 이유: 아래 옛 복원 코드가 도달 불가 경고(#19)로 잡히지 않게.</summary>
    private static readonly bool RestoreTemporarilyClosed = true;

    /// <summary>
    /// ① 덤프 자격증명 — 설정 원본(<c>TenantConfigReader</c>: 연결 문자열을 조립하는 것과 같은 키
    /// <c>DB_HOST·DB_PORT·DB_NAME·DB_USER·DB_PASSWORD</c>, InfrastructureExtensions.cs:20-24).
    /// 비면 프로세스를 띄우기 전에 실패하고, 값은 이유에 절대 넣지 않는다.
    /// </summary>
    private (string host, int port, string db, string user, string pass) ResolveDbCredentialsFromConfig()
    {
        var db = TenantConfigReader.Get("DB_NAME");
        if (string.IsNullOrWhiteSpace(db))
            throw new InvalidOperationException("DB 이름 설정을 읽지 못했습니다. 백업을 시작하지 않았습니다.");
        var user = TenantConfigReader.Get("DB_USER");
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("DB 사용자 설정을 읽지 못했습니다. 백업을 시작하지 않았습니다.");
        var pass = TenantConfigReader.Get("DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(pass))
            throw new InvalidOperationException("DB 비밀번호 설정을 읽지 못했습니다. 백업을 시작하지 않았습니다.");

        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var portText = TenantConfigReader.Get("DB_PORT") ?? "3306";
        if (!int.TryParse(portText, out var port) || port <= 0)
            throw new InvalidOperationException("DB 포트 설정이 올바르지 않습니다. 백업을 시작하지 않았습니다.");

        // 읽는 곳을 바꾸는 데 따른 안전장치 — 설정이 가리키는 DB 와 지금 열린 연결의 DB 가 다르면 다른 DB 를 뜨는 사고다.
        var openDb = _db.Database;
        if (!string.IsNullOrEmpty(openDb) && !string.Equals(openDb, db, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("설정의 DB 이름과 지금 연결된 DB 이름이 달라 백업을 멈췄습니다(다른 DB 를 백업하지 않기 위해).");

        return (host, port, db, user, pass);
    }

    /// <summary>
    /// ③ 덤프 실행파일 — ⓐ MariaDB 11.4 설치 폴더의 <c>mariadb-dump</c> → 같은 폴더 <c>mysqldump</c>
    /// → ⓑ PATH 에서 <c>mariadb-dump</c> 이름만(PATH 의 Oracle MySQL <c>mysqldump</c> 배제 — 선행검증 §5 K1-2 COLUMN_STATISTICS 실패)
    /// → 없으면 이유와 함께 실패. PATH 는 <c>where</c> 프로세스 대신 폴더를 직접 확인한다(OS 무관).
    /// </summary>
    private static string ResolveDumpBinary()
    {
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var dir in MariaDbInstallBinDirs())
        {
            foreach (var name in new[] { "mariadb-dump", "mysqldump" })
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var fromPath = FindOnPath("mariadb-dump" + ext);
        if (fromPath is not null) return fromPath;

        throw new InvalidOperationException(
            "MariaDB 백업 도구(mariadb-dump)를 찾을 수 없습니다. MariaDB 11.4 설치 폴더(bin)를 확인하세요. (PATH 에 있는 다른 제품의 mysqldump 는 쓰지 않습니다)");
    }

    private static IEnumerable<string> MariaDbInstallBinDirs()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var dir = Path.Combine(programFiles, "MariaDB 11.4", "bin");
            if (seen.Add(dir)) yield return dir;
        }
        const string fixedDir = @"C:\Program Files\MariaDB 11.4\bin";
        if (seen.Add(fixedDir)) yield return fixedDir;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar)) return null;
        foreach (var raw in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = raw.Trim('"');
            // 상대 경로 PATH 항목은 작업 폴더에 따라 뜻이 바뀐다 — 보지 않는다.
            if (dir.Length == 0 || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(dir)) continue;
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>③ 백업 1차 폴더 결정 — 실제 폴더 값으로 <see cref="ResolveBackupFolderCore"/> 를 부른다.</summary>
    private static string ResolveBackupFolder(string? stored) =>
        ResolveBackupFolderCore(stored,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    /// ③ 저장값이 쓸 수 있으면 그대로 → 아니면 문서 폴더\HitpanBackup → 그것도 못 쓰면 %ProgramData%\HitPan\Backup (K2).
    /// 「쓸 수 있다」 = 절대 경로 · Windows 폴더 밖. (입력 주입 가능 — G-BK4)
    /// </summary>
    private static string ResolveBackupFolderCore(string? stored, string? documentsFolder, string? windowsFolder, string? programDataFolder)
    {
        if (!string.IsNullOrWhiteSpace(stored) && IsUsableBackupFolder(stored, windowsFolder)) return stored;

        if (!string.IsNullOrWhiteSpace(documentsFolder))
        {
            var fromDocuments = Path.Combine(documentsFolder, "HitpanBackup");
            if (IsUsableBackupFolder(fromDocuments, windowsFolder)) return fromDocuments;
        }

        if (!string.IsNullOrWhiteSpace(programDataFolder))
        {
            var k2 = Path.Combine(programDataFolder, "HitPan", "Backup");
            if (IsUsableBackupFolder(k2, windowsFolder)) return k2;
        }

        // 마지막 안전망 — 실행파일 폴더 기준 절대 경로(상대 경로를 절대 돌려주지 않는다).
        return Path.Combine(AppContext.BaseDirectory, "HitpanBackup");
    }

    private static bool IsUsableBackupFolder(string path, string? windowsFolder)
    {
        try
        {
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(path)) return false;
            if (string.IsNullOrWhiteSpace(windowsFolder) || !Path.IsPathFullyQualified(windowsFolder)) return true;
            return !IsSameOrUnder(path, windowsFolder);
        }
        catch (ArgumentException) { return false; }        // 경로로 해석할 수 없는 값 = 쓸 수 없는 경로
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// C-8 — 백업 폴더 보장. 코드가 정한 기본 폴더(%ProgramData%\HitPan\Backup)일 때만 제한 권한으로 만들고 검사한다.
    /// 사용자가 화면에서 고른 다른 경로는 종전대로 만들기만 한다(권한 손대지 않음).
    /// </summary>
    private static void EnsureBackupFolder(string path)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData)
            && IsUsableBackupFolder(path, null)
            && string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                             Path.GetFullPath(Path.Combine(programData, "HitPan", "Backup")),
                             StringComparison.OrdinalIgnoreCase))
        {
            EnsureRestrictedBackupFolder(path);
            return;
        }
        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// C-8 — 없으면 상속을 끊고 SYSTEM · Administrators · 현재 실행 계정만 모든 권한으로 만든다.
    /// 이미 있으면 소유자·쓰기 권한자가 그 셋 밖일 때 덤프를 쓰지 않고 이유와 함께 실패한다.
    /// (%ProgramData%\HitPan 은 Users 가 하위 폴더를 만들 수 있다 — 먼저 만들어 가지는 길을 막는다. 병렬이슈25)
    /// Windows 밖에서는 권한 모델이 달라 만들기만 한다(리눅스 CI 한계 — 개발명세서).
    /// </summary>
    private static void EnsureRestrictedBackupFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureRestrictedBackupFolderWindows(path);
            return;
        }
        Directory.CreateDirectory(path);
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureRestrictedBackupFolderWindows(string path)
    {
        const int GenericWrite = 0x40000000;
        const int GenericAll = 0x10000000;
        var writeLike = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData
                              | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes
                              | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
                              | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)
                        | GenericWrite | GenericAll;

        var allowed = AllowedBackupFolderPrincipals();
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
        {
            // 상위(%ProgramData%\HitPan)는 다른 기능(워치독)도 쓰므로 종전 상속대로 두고, Backup 한 칸만 제한한다.
            if (dir.Parent is { Exists: false } parent) Directory.CreateDirectory(parent.FullName);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in allowed)
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            dir.Create(security);
            dir.Refresh();
        }

        // 만든 직후에도 검사한다 — 확인과 생성 사이에 남이 먼저 만든 경우까지 같은 판정을 받는다.
        var acl = dir.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
            throw new InvalidOperationException(
                $"백업 폴더의 소유자가 허용된 계정(SYSTEM·Administrators·실행 계정)이 아니라 백업 파일을 쓰지 않았습니다. 폴더 권한을 확인하세요: {path}");

        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid) continue;
            if (allowed.Contains(sid) || sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid)) continue;
            if (((int)rule.FileSystemRights & writeLike) != 0)
                throw new InvalidOperationException(
                    $"백업 폴더에 허용되지 않은 계정의 쓰기 권한이 있어 백업 파일을 쓰지 않았습니다. 폴더 권한을 확인하세요: {path}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static HashSet<SecurityIdentifier> AllowedBackupFolderPrincipals()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } current) set.Add(current);
        return set;
    }

    /// <summary>② 살아 있는 덤프를 자식까지 끝낸다 · 종료 확인은 취소되지 않은 짧은 대기.</summary>
    private void StopProcessTree(Process proc, string exe)
    {
        var name = Path.GetFileName(exe);
        try
        {
            if (proc.HasExited) return;
            proc.Kill(entireProcessTree: true);
            if (proc.WaitForExit(5000))
                _logger.LogWarning("덤프 프로세스를 끝냈습니다(취소 또는 실패) {Exe}", name);
            else
                _logger.LogWarning("덤프 프로세스가 종료 요청 뒤 5초 안에 끝나지 않았습니다 {Exe}", name);
        }
        catch (InvalidOperationException ex) { _logger.LogWarning(ex, "덤프 프로세스가 이미 끝났습니다 {Exe}", name); }
        catch (System.ComponentModel.Win32Exception ex) { _logger.LogWarning(ex, "덤프 프로세스 종료 실패 {Exe}", name); }
        catch (AggregateException ex) { _logger.LogWarning(ex, "덤프 자식 프로세스 일부 종료 실패 {Exe}", name); }
    }

    /// <summary>C28-1 — MariaDB 클라이언트가 MYSQL_PWD 로 로그인할 때 내는 인증서 검증 끔 경고 줄을 실패 이유에서 뺀다.</summary>
    private static string WithoutPasswordlessTlsWarning(string stderr) =>
        string.Join('\n', stderr.Split('\n')
            .Where(line => !line.Contains("ssl-verify-server-cert is disabled", StringComparison.OrdinalIgnoreCase)))
            .Trim();

    /// <summary>④ 실패 기록용 연결 보장 — 취소되지 않은 토큰으로 · 끊긴 연결은 닫고 다시 연다.</summary>
    private async Task EnsureOpenForRecordAsync()
    {
        if (_db.State == ConnectionState.Broken) _db.Close();
        if (_db.State != ConnectionState.Open)
        {
            if (_db is DbConnection dc) await dc.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            else _db.Open();
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
