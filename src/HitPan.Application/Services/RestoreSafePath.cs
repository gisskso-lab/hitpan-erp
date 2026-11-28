using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2② — 안전 경로 복원 걸음 ① ~ ③(계약 <see cref="IRestoreSafePath"/> · 설계 §4 · §15-3 · 작업지시서 §4-1 A-2② 줄).
/// </summary>
/// <remarks>
/// <para>깃발 <c>BackupService.RestoreTemporarilyClosed</c> 닫힌 채 = 제품 길에서 부르는 곳 0. 시험은 DB 게이트에서 직접 부른다(§9 ②).</para>
/// <para>회사 = JWT 에서 온 <c>tenantId</c> 하나(#2) · 걸음 ① ~ ③ 은 라이브 「백업 값」 표 손 0 · 쓰는 곳 = 비번 실패 칸 · 백업 목록(사전 백업) · <c>rs_new__</c> 임시 표뿐.</para>
/// <para>붓기 꼴 = (ㄴ) 앱 연결 직접 실행(설계 §15-3 ③-3 검토 권고) — 변환기가 이미 문장을 나누므로 클라이언트 명령 면 · 실행 파일 찾기 · 비번 환경 변수 길이 통째로 없다.</para>
/// <para>임시 표 지우기 = <see cref="DropStagingTablesAsync"/> 한 곳(G9) · 원장 교체 함수는 A-2③ 이 이 클래스에 private 으로 더한다.</para>
/// </remarks>
public sealed class RestoreSafePath : IRestoreSafePath
{
    /// <summary>사전 백업의 <c>backup_history.triggered_by</c> 글자 — 파일 이름이 <c>hitpan_pre_restore_*</c> 라 보관정책 밖(G8).</summary>
    public const string PreRestoreTrigger = "pre_restore";

    /// <summary>판 대조용으로만 붓는 표(교체 안 함 · 설계 §15-3 ③-2).</summary>
    public static readonly string[] VersionCompareTables = ["schema_migrations", "__efmigrationshistory"];

    /// <summary>비번 실패 잠금 — 로그인과 같은 셈(AuthService: 5회 · 15분).</summary>
    public const int LockoutThreshold = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly Regex StagingNameRx = new("^rs_new__[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IDbConnection _db;
    private readonly IBackupService _backup;
    private readonly ILogger<RestoreSafePath> _logger;

    public RestoreSafePath(IDbConnection db, IBackupService backup, ILogger<RestoreSafePath> logger)
    {
        _db = db;
        _backup = backup;
        _logger = logger;
    }

    // ═══ 문 ═══════════════════════════════════════════════════════════
    public async Task<RestoreStepResult> CheckDoorAsync(string tenantId, string userId, SafeRestoreRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 겹 2 — 대표 판정(대표 0 · 본인 아님 · 대표 2+ = 거절 · DataResetService 판정과 같은 셈 · G1)
        var gate = await _db.QuerySingleAsync<ParentRow>(new CommandDefinition(
            """
            SELECT
              CAST(COALESCE(SUM(is_parent = 1 AND is_deleted = 0 AND is_active = 1), 0) AS SIGNED) AS ParentCount,
              CAST(COALESCE(SUM(user_id = @UserId AND is_parent = 1 AND is_deleted = 0 AND is_active = 1
                                AND account_type = 'tenant_admin'), 0) AS SIGNED) AS SelfCount
            FROM users
            WHERE tenant_id = @TenantId
            """,
            new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (gate.ParentCount != 1 || gate.SelfCount != 1)
            return Door(SafeRestoreDenyCodes.NotParent, $"대표 판정 거절 parent={gate.ParentCount} self={gate.SelfCount}");

        // 화면 2장 확인(조건 6) · 회사명(겹 4)
        if (!request.AcknowledgedLaterDataLoss) return Door(SafeRestoreDenyCodes.NotAcknowledged, "확인 체크 안 함");
        var company = await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT company_name FROM local_company WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(company)
            || !string.Equals(request.ConfirmCompanyName?.Trim(), company.Trim(), StringComparison.Ordinal))
            return Door(SafeRestoreDenyCodes.CompanyMismatch, "회사명 다름");

        // 겹 3 — 비번 다시 입력(잠김 · 틀림 = 실패 칸 +1 · G2)
        var user = await _db.QuerySingleOrDefaultAsync<PasswordRow>(new CommandDefinition(
            """
            SELECT password_hash AS PasswordHash, failed_login_count AS FailedLoginCount, lockout_end AS LockoutEnd
            FROM users
            WHERE user_id = @UserId AND tenant_id = @TenantId
              AND account_type = 'tenant_admin' AND is_active = 1 AND is_parent = 1 AND is_deleted = 0
            LIMIT 1
            """,
            new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (user is null) return Door(SafeRestoreDenyCodes.NotParent, "대표 행 없음");

        var now = DateTime.UtcNow;
        if (user.LockoutEnd is { } until && until > now)
        {
            return new RestoreStepResult
            {
                Step = SafeRestoreStep.None, DenyCode = SafeRestoreDenyCodes.LockedOut, DataUnchanged = true,
                LockoutRemainingSeconds = (int)Math.Ceiling((until - now).TotalSeconds), LogDetail = "잠김",
            };
        }

        if (string.IsNullOrEmpty(request.Password) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // 왼쪽부터 셈 — lockout_end 를 먼저(옛 실패 칸 + 1 로 판정) · 그다음 실패 칸 +1
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE users
                SET lockout_end = CASE WHEN failed_login_count + 1 >= @Threshold THEN @LockUntil ELSE lockout_end END,
                    failed_login_count = failed_login_count + 1
                WHERE user_id = @UserId AND tenant_id = @TenantId
                """,
                new { Threshold = LockoutThreshold, LockUntil = now.Add(LockoutDuration), UserId = userId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);
            return Door(SafeRestoreDenyCodes.WrongPassword, "비번 틀림");
        }

        if (user.FailedLoginCount > 0 || user.LockoutEnd is not null)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE users SET failed_login_count = 0, lockout_end = NULL WHERE user_id = @UserId AND tenant_id = @TenantId",
                new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        }
        return new RestoreStepResult { Ok = true, Step = SafeRestoreStep.None, DataUnchanged = true };
    }

    // ═══ 화면 1장 목록 ═════════════════════════════════════════════════
    public async Task<IReadOnlyList<SafeRestoreCandidateDto>> ListCandidatesAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var folder = await BackupFolderAsync(tenantId, ct).ConfigureAwait(false);
        var live = await ComputeSchemaFingerprintAsync(_db, ct).ConfigureAwait(false);
        var rows = await _db.QueryAsync<HistoryRow>(new CommandDefinition(
            HistorySelect + " WHERE tenant_id = @TenantId AND status = 'success' ORDER BY started_at DESC LIMIT 50",
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        var list = new List<SafeRestoreCandidateDto>();
        foreach (var r in rows)
        {
            var deny = RowDeny(r, folder, live);
            list.Add(new SafeRestoreCandidateDto
            {
                BackupId = r.BackupId, StartedAt = r.StartedAt, FileSizeBytes = r.FileSizeBytes,
                TriggeredBy = r.TriggeredBy ?? "manual", Selectable = deny is null, DenyCode = deny,
            });
        }
        return list;
    }

    // ═══ ① 고르기 ═════════════════════════════════════════════════════
    public async Task<RestoreStepResult<RestoreSelection>> SelectAsync(string tenantId, string backupId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var row = string.IsNullOrWhiteSpace(backupId) ? null : await _db.QuerySingleOrDefaultAsync<HistoryRow>(new CommandDefinition(
            HistorySelect + " WHERE backup_id = @BackupId AND tenant_id = @TenantId AND status = 'success'",
            new { BackupId = backupId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.BackupNotInList, "목록에 없음");

        var folder = await BackupFolderAsync(tenantId, ct).ConfigureAwait(false);
        var live = await ComputeSchemaFingerprintAsync(_db, ct).ConfigureAwait(false);
        var deny = RowDeny(row, folder, live);
        if (deny is not null) return Fail<RestoreSelection>(SafeRestoreStep.Select, deny, $"목록 행 거절 {deny}");

        // 라이브 표 분류 전수(뷰 제외 · SEQUENCE = 멈춤 · P3-3) — 분류 밖 표가 하나라도 있으면 멈춤(G13 와 같은 판정 함수)
        var tables = (await _db.QueryAsync<(string Name, string Type)>(new CommandDefinition(
            "SELECT TABLE_NAME, TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()",
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
        if (tables.Any(t => string.Equals(t.Type, "SEQUENCE", StringComparison.OrdinalIgnoreCase)))
            return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.UnclassifiedTable, "SEQUENCE 있음");
        var real = tables.Where(t => !string.Equals(t.Type, "VIEW", StringComparison.OrdinalIgnoreCase))
                         .Select(t => t.Name)
                         .Where(n => !RestoreDumpConverter.ReservedPrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        var unclassified = RestoreTableClassification.FindUnclassified(real);
        if (unclassified.Count > 0)
            return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.UnclassifiedTable, $"분류 밖 표 {unclassified.Count}");

        // 파일 — 쓰기 막고 연 그 손잡이로 지문(K6 · ③-1)
        FileStream? fs = null;
        try
        {
            fs = new FileStream(row.PrimaryFile!, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            var sha = await Sha256HexAsync(fs, ct).ConfigureAwait(false);
            if (!string.Equals(sha, row.FileSha256, StringComparison.OrdinalIgnoreCase))
            {
                await fs.DisposeAsync().ConfigureAwait(false);
                return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.FingerprintMismatch, "지문 다름");
            }
            fs.Position = 0;
            var selection = new RestoreSelection(row.BackupId, row.StartedAt, sha, row.SchemaFingerprint!, fs);
            fs = null;
            return new RestoreStepResult<RestoreSelection> { Ok = true, Step = SafeRestoreStep.Select, DataUnchanged = true, Value = selection };
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ① 백업 파일을 열지 못함 tenantId={TenantId} backupId={BackupId}", tenantId, row.BackupId);
            return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.BackupNotInList, "파일 열기 실패");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ① 백업 파일 권한 없음 tenantId={TenantId} backupId={BackupId}", tenantId, row.BackupId);
            return Fail<RestoreSelection>(SafeRestoreStep.Select, SafeRestoreDenyCodes.BackupNotInList, "파일 권한 없음");
        }
        finally
        {
            if (fs is not null) await fs.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ═══ ② 사전 백업 ══════════════════════════════════════════════════
    public async Task<RestoreStepResult<RestorePreBackup>> PreBackupAsync(string tenantId, string restoreId, bool maintenanceActive, CancellationToken ct = default)
    {
        // 점검 상태 켠 **뒤**에만(설계 §4 ② · §15-5 ①) — 켜지 않았으면 덤프 0
        if (!maintenanceActive)
            return Fail<RestorePreBackup>(SafeRestoreStep.PreBackup, SafeRestoreDenyCodes.PreBackupFailed, "점검 상태 꺼짐");

        var run = await _backup.RunBackupAsync(tenantId, PreRestoreTrigger, ct).ConfigureAwait(false);
        if (!run.Success)
        {
            _logger.LogWarning("안전 복원 ② 사전 백업 실패 tenantId={TenantId} restoreId={RestoreId}", tenantId, restoreId);
            return Fail<RestorePreBackup>(SafeRestoreStep.PreBackup, SafeRestoreDenyCodes.PreBackupFailed, "덤프 실패");
        }

        // 목록 등록 확인 — 성공 · 지문 · 판 칸이 다 있어야 「되돌릴 수 있는 백업」(G8 · 등록 뺌 = 빨강)
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var registered = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM backup_history
            WHERE backup_id = @BackupId AND tenant_id = @TenantId AND status = 'success' AND triggered_by = @Trigger
              AND file_sha256 IS NOT NULL AND schema_fingerprint IS NOT NULL
            """,
            new { BackupId = run.BackupId, TenantId = tenantId, Trigger = PreRestoreTrigger }, cancellationToken: ct)).ConfigureAwait(false);
        if (registered != 1)
            return Fail<RestorePreBackup>(SafeRestoreStep.PreBackup, SafeRestoreDenyCodes.PreBackupFailed, "목록 등록 없음");

        return new RestoreStepResult<RestorePreBackup>
        {
            Ok = true, Step = SafeRestoreStep.PreBackup, DataUnchanged = true, Value = new RestorePreBackup { BackupId = run.BackupId },
        };
    }

    // ═══ ③ 임시 자리 ══════════════════════════════════════════════════
    public async Task<RestoreStepResult<RestoreStaging>> StageAsync(string tenantId, RestoreSelection selection, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await DropStagingTablesAsync(ct).ConfigureAwait(false);

        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versionOnly = new List<string>();

        // 첫 바퀴 — 변환 + 출력 2차 검사 전부(붓기 0)
        try
        {
            selection.DumpStream.Position = 0;
            foreach (var s in RestoreDumpConverter.Convert(selection.DumpStream, DecideTable))
                RestoreStageOutputCheck.Verify(s.Sql);
        }
        catch (RestoreDumpRejectedException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ③ 변환기 멈춤(붓기 0) tenantId={TenantId} backupId={BackupId}", tenantId, selection.BackupId);
            return Fail<RestoreStaging>(SafeRestoreStep.Stage, SafeRestoreDenyCodes.StageFailed, "변환기 멈춤");
        }

        // 둘째 바퀴 — 같은 손잡이 되감아 실행(문장마다 2차 검사 다시)
        var executed = 0;
        var sessionChanged = false;
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION sql_mode = 'NO_AUTO_VALUE_ON_ZERO', time_zone = '+00:00', foreign_key_checks = 0, unique_checks = 0",
                cancellationToken: ct)).ConfigureAwait(false);
            sessionChanged = true;

            selection.DumpStream.Position = 0;
            foreach (var s in RestoreDumpConverter.Convert(selection.DumpStream, DecideTable))
            {
                RestoreStageOutputCheck.Verify(s.Sql);
                var sql = StrictUtf8.GetString(s.Sql);          // 바이트 그대로(올바른 UTF-8 만 · 아니면 멈춤 — 바이너리는 --hex-blob 로 16진)
                await _db.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 600, cancellationToken: ct)).ConfigureAwait(false);
                executed++;
                if (!staged.ContainsKey(s.SourceTable))
                {
                    staged[s.SourceTable] = RestoreDumpConverter.Prefix + s.SourceTable;
                    if (VersionCompareTables.Contains(s.SourceTable, StringComparer.OrdinalIgnoreCase)) versionOnly.Add(s.SourceTable);
                }
            }
        }
        catch (Exception ex) when (ex is RestoreDumpRejectedException or DecoderFallbackException or DbException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "안전 복원 ③ 붓기 실패 tenantId={TenantId} backupId={BackupId} 실행={Executed}", tenantId, selection.BackupId, executed);
            await DropStagingQuietlyAsync(tenantId).ConfigureAwait(false);
            return Fail<RestoreStaging>(SafeRestoreStep.Stage, SafeRestoreDenyCodes.StageFailed, "붓기 실패");
        }
        finally
        {
            if (sessionChanged) await ResetSessionAsync().ConfigureAwait(false);
        }

        // 붓기 뒤 회사 표지 — 임시 표에 다른 회사 줄 0(조건 4 · G3) · 대조 ④ 본체는 A-2③
        long others;
        try
        {
            others = await CountOtherTenantRowsAsync(tenantId, ct).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ③ 회사 표지 셈 실패 tenantId={TenantId}", tenantId);
            await DropStagingQuietlyAsync(tenantId).ConfigureAwait(false);
            return Fail<RestoreStaging>(SafeRestoreStep.Stage, SafeRestoreDenyCodes.StageFailed, "회사 표지 셈 실패");
        }
        if (others > 0)
        {
            await DropStagingQuietlyAsync(tenantId).ConfigureAwait(false);
            return Fail<RestoreStaging>(SafeRestoreStep.Stage, SafeRestoreDenyCodes.OtherCompanyBackup, $"다른 회사 줄 {others}");
        }

        return new RestoreStepResult<RestoreStaging>
        {
            Ok = true, Step = SafeRestoreStep.Stage, DataUnchanged = true,
            Value = new RestoreStaging { StagedTables = staged, VersionOnlyTables = versionOnly, StatementsExecuted = executed },
        };
    }

    // ═══ 임시 표 지우기 — 한 곳(G9) ═══════════════════════════════════
    public async Task<int> DropStagingTablesAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var names = (await _db.QueryAsync<string>(new CommandDefinition(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, 8) = @P AND TABLE_TYPE = 'BASE TABLE'",
            new { P = RestoreTableClassification.StagingPrefix }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var dropped = 0;
        foreach (var n in names)
        {
            if (!StagingNameRx.IsMatch(n))
            {
                _logger.LogWarning("임시 표 이름 꼴 밖이라 지우지 않음 {Name}", n);
                continue;
            }
            await _db.ExecuteAsync(new CommandDefinition($"DROP TABLE IF EXISTS `{n}`", cancellationToken: ct)).ConfigureAwait(false);
            dropped++;
        }
        return dropped;
    }

    // ═══ 공용 셈(BackupService 도 부름) ════════════════════════════════

    /// <summary>
    /// 판 표지 — <c>schema_migrations</c>(success = 1) 식별자 + EF 이력 식별자를 서수 정렬해 SHA-256(소문자 16진 64자 · 설계 §15-7 · K5).
    /// 파일 이름 · 날짜로 추정하지 않는다.
    /// </summary>
    public static async Task<string> ComputeSchemaFingerprintAsync(IDbConnection db, CancellationToken ct)
    {
        var mig = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT migration_id FROM schema_migrations WHERE success = 1", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var ef = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT MigrationId FROM __efmigrationshistory", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        mig.Sort(StringComparer.Ordinal);
        ef.Sort(StringComparer.Ordinal);
        var text = "schema_migrations\n" + string.Join("\n", mig) + "\n__efmigrationshistory\n" + string.Join("\n", ef);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>스트림 SHA-256(소문자 16진 64자) — 처음부터 끝까지 읽는다.</summary>
    public static async Task<string> Sha256HexAsync(Stream s, CancellationToken ct)
    {
        s.Position = 0;
        var hash = await SHA256.HashDataAsync(s, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ═══ 안쪽 ═════════════════════════════════════════════════════════

    private const string HistorySelect =
        """
        SELECT backup_id AS BackupId, started_at AS StartedAt, primary_file AS PrimaryFile,
               file_size_bytes AS FileSizeBytes, triggered_by AS TriggeredBy,
               file_sha256 AS FileSha256, schema_fingerprint AS SchemaFingerprint
        FROM backup_history
        """;

    /// <summary>목록 행 판정 — 폴더 밖 · 파일 없음 = 목록 밖 · 지문 칸 빔 = 지문 · 판 칸 빔 · 다름 = 판(G3 · G19).</summary>
    private static string? RowDeny(HistoryRow r, string folder, string liveFingerprint)
    {
        if (string.IsNullOrWhiteSpace(r.PrimaryFile) || !IsUnder(r.PrimaryFile, folder) || !File.Exists(r.PrimaryFile))
            return SafeRestoreDenyCodes.BackupNotInList;
        if (string.IsNullOrWhiteSpace(r.FileSha256)) return SafeRestoreDenyCodes.FingerprintMismatch;
        if (string.IsNullOrWhiteSpace(r.SchemaFingerprint)
            || !string.Equals(r.SchemaFingerprint, liveFingerprint, StringComparison.OrdinalIgnoreCase))
            return SafeRestoreDenyCodes.VersionMismatch;
        return null;
    }

    private static bool IsUnder(string file, string folder)
    {
        try
        {
            var f = Path.GetFullPath(file);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return f.StartsWith(root, cmp) && string.Equals(Path.GetDirectoryName(f), Path.TrimEndingDirectorySeparator(root), cmp);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task<string> BackupFolderAsync(string tenantId, CancellationToken ct) =>
        (await _backup.GetSettingsAsync(tenantId, ct).ConfigureAwait(false)).PrimaryPath;

    private static RestoreStageDecision DecideTable(string name)
    {
        if (VersionCompareTables.Contains(name, StringComparer.OrdinalIgnoreCase)) return RestoreStageDecision.Stage;
        return RestoreTableClassification.Classify(name) switch
        {
            RestoreTableClass.FromBackup => RestoreStageDecision.Stage,
            RestoreTableClass.KeepCurrent => RestoreStageDecision.Skip,
            _ => RestoreStageDecision.Stop,
        };
    }

    private async Task<long> CountOtherTenantRowsAsync(string tenantId, CancellationToken ct)
    {
        var tables = (await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT TABLE_NAME FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME = 'tenant_id' AND LEFT(TABLE_NAME, 8) = @P
            """,
            new { P = RestoreTableClassification.StagingPrefix }, cancellationToken: ct)).ConfigureAwait(false))
            .Where(n => StagingNameRx.IsMatch(n)).ToList();
        if (tables.Count == 0) return 0;
        // 회사 표지가 있는 임시 표마다 「이 회사 아닌 줄」 — 한 질의(#16) · 이름은 information_schema 에서 온 것 + 꼴 검사
        var union = string.Join("\nUNION ALL\n",
            tables.Select(n => $"SELECT COUNT(*) AS n FROM `{n}` WHERE tenant_id IS NOT NULL AND NOT (tenant_id = @TenantId)"));
        return await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT CAST(COALESCE(SUM(n), 0) AS SIGNED) FROM (\n{union}\n) z",
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task DropStagingQuietlyAsync(string tenantId)
    {
        try
        {
            await DropStagingTablesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            // 다음 기동 때 청소(설계 §4 ③) — 라이브 손 0
            _logger.LogWarning(ex, "안전 복원 임시 표 지우기 실패 — 다음에 다시 지움 tenantId={TenantId}", tenantId);
        }
    }

    private async Task ResetSessionAsync()
    {
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION sql_mode = @@GLOBAL.sql_mode, time_zone = @@GLOBAL.time_zone, foreign_key_checks = 1, unique_checks = 1",
                cancellationToken: CancellationToken.None)).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ③ 세션 설정 되돌리기 실패 — 연결을 닫음");
            _db.Close();
        }
    }

    private static RestoreStepResult Door(string code, string detail) =>
        new() { Step = SafeRestoreStep.None, DenyCode = code, DataUnchanged = true, LogDetail = detail };

    private static RestoreStepResult<T> Fail<T>(SafeRestoreStep step, string code, string detail) where T : class =>
        new() { Step = step, DenyCode = code, DataUnchanged = true, LogDetail = detail };

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Broken) _db.Close();
        if (_db.State != ConnectionState.Open)
        {
            if (_db is DbConnection dc) await dc.OpenAsync(ct).ConfigureAwait(false);
            else _db.Open();
        }
    }

    private sealed class ParentRow
    {
        public long ParentCount { get; set; }
        public long SelfCount { get; set; }
    }

    private sealed class PasswordRow
    {
        public string PasswordHash { get; set; } = "";
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutEnd { get; set; }
    }

    private sealed class HistoryRow
    {
        public string BackupId { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public string? PrimaryFile { get; set; }
        public long? FileSizeBytes { get; set; }
        public string? TriggeredBy { get; set; }
        public string? FileSha256 { get; set; }
        public string? SchemaFingerprint { get; set; }
    }
}
