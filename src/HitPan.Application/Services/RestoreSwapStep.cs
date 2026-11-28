using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>걸음 ④ · ④-끝 · ⑤ 결과 — 응답 문구는 고정 표에서만(P3-9). <b>기본값 = 실패 쪽</b>.</summary>
public sealed class RestoreSwapResult<T> : RestoreStepResult where T : class
{
    /// <summary>통과면 값 · 아니면 null.</summary>
    public T? Value { get; init; }

    /// <summary>쉬운 말 1줄(<see cref="RestoreOverlayRules.MessageFor"/> 로만 채움).</summary>
    public string Message { get; init; } = RestoreOverlayRules.UnknownMessage;

    /// <summary>D1 — 남는 멈춤 대상 계정 이름(<see cref="SafeRestoreDenyCodes.NewerAccountsMissing"/> 일 때만).</summary>
    public IReadOnlyList<string> AccountNames { get; init; } = Array.Empty<string>();
}

/// <summary>④ 본 대조 결과.</summary>
public sealed class RestoreVerifyReport
{
    /// <summary>교체할 「백업 값」 표(칸 · 회사 표지 칸).</summary>
    public IReadOnlyList<RestoreSwapTable> Tables { get; init; } = Array.Empty<RestoreSwapTable>();

    /// <summary>⑤ 문장 차례(외래키 그래프).</summary>
    public RestoreSwapOrder Order { get; init; } = new(Array.Empty<string>(), Array.Empty<string>());

    /// <summary>「ⓑ 후보」 — 라이브에만 있고 활성 계정과 이은 사원 행 수(ⓑ′ 판정 전 · 적기만 · ④-끝 ③ 에서 확정).</summary>
    public long BPrimeCandidates { get; init; }
}

/// <summary>④-끝 ③ 셈 — 프로세스 안에 둠 · ⑤ C1′ 이 대조 · 감사 2벌에 숫자.</summary>
public sealed class RestoreOverlayCounts
{
    public long RoleDiff { get; init; }
    public long ActiveDiff { get; init; }
    public long LinkDiff { get; init; }
    public long LeftAtDiff { get; init; }
    public long ChatNarrowed { get; init; }
    public long LinesOff { get; init; }
    public long DocsRecalled { get; init; }
    public long BothEmployees { get; init; }
    public long LiveOnlyEmployees { get; init; }
    public long BothChat { get; init; }
    public long LiveOnlyChat { get; init; }
    public long BPrimeExempted { get; init; }

    /// <summary>④-끝 시간(G16 · 따로 기록).</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>감사 덧말(글자 · 숫자 · <c>: = .</c> 만 · 200자 이하 · 개인정보 0).</summary>
    public string AuditDetail() => string.Create(CultureInfo.InvariantCulture,
        $"role={RoleDiff}:act={ActiveDiff}:link={LinkDiff}:left={LeftAtDiff}:narrow={ChatNarrowed}:lines={LinesOff}:docs={DocsRecalled}:both={BothEmployees}:liveonly={LiveOnlyEmployees}:chatboth={BothChat}:chatlive={LiveOnlyChat}:bp={BPrimeExempted}");
}

/// <summary>⑤ 교체 결과.</summary>
public sealed class RestoreSwapReport
{
    public RestoreOverlayCounts Counts { get; init; } = new();

    /// <summary>D2 — 거래 안에서 읽은 격리 수준(<c>REPEATABLE-READ</c> 여야 함).</summary>
    public string Isolation { get; init; } = "";

    /// <summary>지운 줄 · 부은 줄(전 표 합).</summary>
    public long DeletedRows { get; init; }
    public long InsertedRows { get; init; }

    /// <summary>⑤ 거래 시간(G16).</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>커밋 뒤 감사 끝 줄을 남겼나(못 남기면 정본 = <c>restore_history</c> · 로그 경고).</summary>
    public bool AuditCompleted { get; init; }
}

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — 안전 경로 복원 걸음 ④ 본 대조 · ④-끝 덧씌움 · ⑤ 교체(작업지시서 §4-1 A-2③ 줄 · 본 §15-3 · 보1 덧1-4 순서 그대로).
/// </summary>
/// <remarks>
/// <para>깃발 <c>BackupService.RestoreTemporarilyClosed</c> 닫힌 채 = 제품 길에서 부르는 곳 0. 시험은 DB 게이트에서 직접 부른다(§9 ②).
/// 원장 표 교체 = 이 클래스의 <see cref="SwapAsync"/> 한 곳(G9 · 부르는 곳 울타리 = 정적 게이트).</para>
/// <para>점검 상태(A-2① <c>IRestoreMaintenance</c>)는 이 클래스가 켜고 끄지 않는다 — 합류 때 지휘(점검 켬 → ② → ③ → 이 클래스 → 끊기 → 점검 끔)가 감싼다.</para>
/// <para>시각 계약(D3): <c>restore_history</c> = <b>서버 지역 시각</b>(<see cref="LocalNow"/> · A-2① 표지가 지역 시각으로 읽음). UTC 로 쓰면 9시간 이르게 읽힘.</para>
/// <para>격리 수준(D2): ④-끝 · ⑤ 거래 = <see cref="IsolationLevel.RepeatableRead"/> <b>명시</b>(세션 · 거래 둘 다 · 기본값 기대 0).</para>
/// </remarks>
public sealed class RestoreSwapStep
{
    /// <summary>D2 — 거래 격리 수준.</summary>
    public const IsolationLevel SwapIsolation = IsolationLevel.RepeatableRead;

    /// <summary>거래 안 읽은 격리 수준이 이 글자여야 함(MariaDB <c>@@transaction_isolation</c> 꼴).</summary>
    public const string ExpectedIsolation = "REPEATABLE-READ";

    private readonly IDbConnection _db;
    private readonly IRestoreSafePath _safePath;
    private readonly ILocalAuditFile _audit;
    private readonly ILogger<RestoreSwapStep> _logger;
    private readonly IRestoreBusyProbe? _busy;
    private readonly Func<string, CancellationToken, Task>? _testHook;
    private readonly Func<DateTime> _localNow;

    /// <param name="testHook">시험 걸이(G6 · G21 ⑫) — 제품 DI 는 넘기지 않음(null · 제품 길 0). ⑤ 거래 안 지우기 뒤 <c>"after_delete"</c> 로 부름.</param>
    /// <param name="localNow">서버 지역 시각(D3 · D9 시험이 주입) — 기본 <see cref="DateTime.Now"/>.</param>
    public RestoreSwapStep(
        IDbConnection db,
        IRestoreSafePath safePath,
        ILocalAuditFile audit,
        ILogger<RestoreSwapStep> logger,
        IRestoreBusyProbe? busy = null,
        Func<string, CancellationToken, Task>? testHook = null,
        Func<DateTime>? localNow = null)
    {
        _db = db;
        _safePath = safePath;
        _audit = audit;
        _logger = logger;
        _busy = busy;
        _testHook = testHook;
        _localNow = localNow ?? (() => DateTime.Now);
    }

    /// <summary>D3 — <c>restore_history</c> 시각(서버 지역 시각).</summary>
    public DateTime LocalNow() => _localNow();

    // ═══ restore_history(지금 값 · 정본) ═══════════════════════════════

    /// <summary>도는 기록 1줄(<c>running</c>) — 복원 ID 를 돌려줌. 시각 = 서버 지역 시각(D3).</summary>
    public async Task<string> BeginHistoryAsync(string tenantId, string userId, string sourceLabel, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var restoreId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.InsertHistoryRunning,
            new { R = restoreId, t = tenantId, Now = LocalNow(), Src = sourceLabel, U = userId }, cancellationToken: ct)).ConfigureAwait(false);
        return restoreId;
    }

    /// <summary>사전 백업 ID 기록(되돌릴 자리).</summary>
    public async Task RecordPreBackupAsync(string tenantId, string restoreId, string preBackupId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.MarkHistoryPreBackup,
            new { Pre = preBackupId, R = restoreId, t = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>멈춤 기록 — <c>error_message</c> = DenyCode 글자만(P3-9).</summary>
    public async Task FailHistoryAsync(string tenantId, string restoreId, string denyCode, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.MarkHistoryFailed,
            new { Now = LocalNow(), Code = denyCode, R = restoreId, t = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ═══ ④ → ④-끝 → ⑤ 한 줄로 ═══════════════════════════════════════

    /// <summary>
    /// ③ 뒤 — ④ 본 대조 → ④-끝 덧씌움 → ⑤ 교체. 멈추면 기록 <c>failed</c> + 임시 표 지움. 성공이면 임시 표 지움(끊기 · 점검 끔은 부르는 쪽 · 합류).
    /// </summary>
    public async Task<RestoreSwapResult<RestoreSwapReport>> RunAsync(
        string tenantId, string userId, string restoreId, RestoreStaging staging, CancellationToken ct = default)
    {
        var verify = await VerifyAsync(tenantId, staging, ct).ConfigureAwait(false);
        if (!verify.Ok) return await StopAsync(tenantId, restoreId, Relay<RestoreSwapReport>(verify)).ConfigureAwait(false);

        var overlay = await OverlayAsync(tenantId, LocalNow(), ct).ConfigureAwait(false);
        if (!overlay.Ok) return await StopAsync(tenantId, restoreId, Relay<RestoreSwapReport>(overlay)).ConfigureAwait(false);

        var swap = await SwapAsync(tenantId, userId, restoreId, verify.Value!, overlay.Value!, ct).ConfigureAwait(false);
        if (!swap.Ok && swap.DataUnchanged) return await StopAsync(tenantId, restoreId, swap).ConfigureAwait(false);
        return swap;
    }

    // ═══ ④ 본 대조 ══════════════════════════════════════════════════

    /// <summary>
    /// ④ — 바쁨(D4 자료이관 · D13 업데이트) → 판 같음 → 표 짝 → 칸 같음(이름 · 차례 · 꼴 · 문자셋 · 정렬 · 생성 칸) → 라이브도 이 회사 표지뿐 →
    /// 외래키 전수 고아(경계 2표 포함) → 교체 차례 → 「ⓑ 후보」 셈. 덧씌우기 <b>전</b> 값 · 라이브 손 0.
    /// </summary>
    public async Task<RestoreSwapResult<RestoreVerifyReport>> VerifyAsync(string tenantId, RestoreStaging staging, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(staging);

        // D4 · D13 — 걸음 ④ 앞에서 멈춤(읽기만 · 자료 그대로)
        if (MigrationJobStore.AnyRunning(tenantId))
        {
            _logger.LogWarning("안전 복원 ④ 앞 멈춤 — 자료이관이 도는 중 tenantId={TenantId}", tenantId);
            return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.MigrationRunning, "자료이관 도는 중");
        }
        var busy = _busy?.BusyCode(tenantId);
        if (busy is not null)
        {
            _logger.LogWarning("안전 복원 ④ 앞 멈춤 — 업데이트가 도는 중 tenantId={TenantId} code={Code}", tenantId, busy);
            return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, busy, "업데이트 도는 중");
        }

        try
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);

            // 판 같음(임시 표의 판 대조용 2 = 라이브 판)
            if (!staging.StagedTables.TryGetValue("schema_migrations", out var mig)
                || !staging.StagedTables.TryGetValue("__efmigrationshistory", out var ef))
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "판 대조 표 없음");
            var live = await RestoreSafePath.ComputeSchemaFingerprintAsync(_db, ct).ConfigureAwait(false);
            var staged = await FingerprintFromAsync(mig, ef, ct).ConfigureAwait(false);
            if (!string.Equals(live, staged, StringComparison.Ordinal))
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VersionMismatch, "임시 표 판 다름");

            // 표 짝 — 라이브 「백업 값」 표 전부가 임시 표로 있어야(빠지면 그 표만 라이브로 남아 섞임)
            var liveTables = (await _db.QueryAsync<string>(new CommandDefinition(
                "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'",
                cancellationToken: ct)).ConfigureAwait(false))
                .Where(n => RestoreTableClassification.Classify(n) == RestoreTableClass.FromBackup)
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            var stagedBackup = staging.StagedTables.Keys
                .Where(n => RestoreTableClassification.Classify(n) == RestoreTableClass.FromBackup).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (liveTables.Any(n => !stagedBackup.Contains(n)) || stagedBackup.Any(n => !liveTables.Contains(n, StringComparer.OrdinalIgnoreCase)))
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "표 짝 다름");
            if (liveTables.Any(n => !RestoreSwapSql.PlainName.IsMatch(n)))
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "표 이름 꼴 밖");

            // 칸 같음(양쪽 · 본 §15-3 ⑤ R5 「칸 꼴 포함」)
            var colDiff = await _db.ExecuteScalarAsync<long>(new CommandDefinition(ColumnDiffSql,
                new { T = liveTables }, cancellationToken: ct, commandTimeout: 300)).ConfigureAwait(false);
            if (colDiff > 0)
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, $"칸 다름 {colDiff}");

            var cols = (await _db.QueryAsync<(string Table, string Column, string? Generated)>(new CommandDefinition(
                """
                SELECT TABLE_NAME, COLUMN_NAME, IS_GENERATED FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN @T ORDER BY TABLE_NAME, ORDINAL_POSITION
                """,
                new { T = liveTables }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            var tables = liveTables.Select(t =>
            {
                var c = cols.Where(x => string.Equals(x.Table, t, StringComparison.OrdinalIgnoreCase)).ToList();
                return new RestoreSwapTable(t,
                    c.Where(x => !string.Equals(x.Generated, "ALWAYS", StringComparison.OrdinalIgnoreCase)).Select(x => x.Column).ToList(),
                    c.Any(x => string.Equals(x.Column, "tenant_id", StringComparison.OrdinalIgnoreCase)));
            }).ToList();
            if (tables.Any(t => t.Columns.Any(c => !RestoreSwapSql.PlainName.IsMatch(c))))
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "칸 이름 꼴 밖");

            // 라이브도 이 회사 표지뿐(한 질의 · #16)
            var withTenant = tables.Where(t => t.HasTenantId).ToList();
            if (withTenant.Count > 0)
            {
                var union = string.Join("\nUNION ALL\n", withTenant.Select(t =>
                    $"SELECT COUNT(*) AS n FROM `{t.Name}` WHERE tenant_id IS NOT NULL AND NOT (tenant_id = @t)"));
                var others = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"SELECT CAST(COALESCE(SUM(n), 0) AS SIGNED) FROM (\n{union}\n) z", new { t = tenantId },
                    cancellationToken: ct, commandTimeout: 600)).ConfigureAwait(false);
                if (others > 0)
                    return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, $"라이브에 다른 회사 줄 {others}");
            }

            // 외래키 전수(라이브 information_schema 정의로 만든 SQL)
            var fks = (await _db.QueryAsync<FkRow>(new CommandDefinition(
                """
                SELECT CONSTRAINT_NAME AS Name, TABLE_NAME AS Child, COLUMN_NAME AS ChildColumn,
                       REFERENCED_TABLE_NAME AS Parent, REFERENCED_COLUMN_NAME AS ParentColumn, ORDINAL_POSITION AS Ordinal
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL
                  AND NOT (LEFT(TABLE_NAME, 8) = 'rs_new__')
                ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
                """, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            var backupSet = liveTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groups = fks.GroupBy(f => (f.Child, f.Name)).ToList();
            var edges = new List<(string Child, string Parent)>();
            var probes = new List<(string Sql, bool Boundary)>();
            foreach (var g in groups)
            {
                var first = g.First();
                var childBackup = backupSet.Contains(first.Child);
                var parentBackup = backupSet.Contains(first.Parent);
                if (!childBackup && parentBackup)
                    return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "지금 값 자식 → 백업 값 부모 외래키");
                if (!childBackup) continue;
                if (g.Any(x => !RestoreSwapSql.PlainName.IsMatch(x.Child) || !RestoreSwapSql.PlainName.IsMatch(x.ChildColumn)
                               || !RestoreSwapSql.PlainName.IsMatch(x.Parent) || !RestoreSwapSql.PlainName.IsMatch(x.ParentColumn)))
                    return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "외래키 이름 꼴 밖");
                if (parentBackup) edges.Add((first.Child, first.Parent));
                var parentTable = parentBackup ? RestoreTableClassification.StagingPrefix + first.Parent : first.Parent;
                var notNull = string.Join(" AND ", g.Select(x => $"c.`{x.ChildColumn}` IS NOT NULL"));
                var match = string.Join(" AND ", g.Select(x => $"p.`{x.ParentColumn}` = c.`{x.ChildColumn}`"));
                probes.Add(($"SELECT COUNT(*) AS n FROM `{RestoreTableClassification.StagingPrefix}{first.Child}` c WHERE {notNull} AND NOT EXISTS (SELECT 1 FROM `{parentTable}` p WHERE {match})",
                            !parentBackup));
            }
            foreach (var boundary in new[] { false, true })
            {
                var part = probes.Where(p => p.Boundary == boundary).Select(p => p.Sql).ToList();
                if (part.Count == 0) continue;
                var orphans = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"SELECT CAST(COALESCE(SUM(n), 0) AS SIGNED) FROM (\n{string.Join("\nUNION ALL\n", part)}\n) z",
                    cancellationToken: ct, commandTimeout: 600)).ConfigureAwait(false);
                if (orphans > 0)
                {
                    return new RestoreSwapResult<RestoreVerifyReport>
                    {
                        Step = SafeRestoreStep.Verify, DenyCode = SafeRestoreDenyCodes.VerifyFailed, DataUnchanged = true,
                        LogDetail = boundary ? $"경계 외래키 고아 {orphans}" : $"외래키 고아 {orphans}",
                        Message = RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.VerifyFailed, boundaryOrphan: boundary),
                    };
                }
            }

            var order = RestoreSwapPlan.Build(tables, edges);
            if (order is null)
                return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "교체 차례가 서지 않음(고리)");

            var candidates = await _db.ExecuteScalarAsync<long>(new CommandDefinition(RestoreSwapSql.CountBCandidates,
                new { t = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

            return new RestoreSwapResult<RestoreVerifyReport>
            {
                Ok = true, Step = SafeRestoreStep.Verify, DataUnchanged = true, Message = "",
                Value = new RestoreVerifyReport { Tables = tables, Order = order, BPrimeCandidates = candidates },
            };
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ④ 본 대조 실패 tenantId={TenantId}", tenantId);
            return Stop<RestoreVerifyReport>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "대조 질의 실패");
        }
    }

    // ═══ ④-끝 덧씌움(rs_new__ 끼리 거래 1) ══════════════════════════════

    /// <summary>
    /// ④-끝 — 사본 → ① 8칸 → ② ⓐ″ → ②′ C4 → ②″ (가′) · C8 → ③ 겹침 0 · C3′ · ⓐ″ 다시 셈 · ⓑ′ 확정 · 셈. 대상 = <c>rs_new__</c> 표만(라이브 읽기만).
    /// 실패면 거래 되돌림(라이브 손 0).
    /// </summary>
    public async Task<RestoreSwapResult<RestoreOverlayCounts>> OverlayAsync(string tenantId, DateTime restoreAtLocal, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        DbTransaction? tx = null;
        try
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);
            await SetSessionAsync(ct).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.DropSnapshot, cancellationToken: ct)).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.CreateSnapshot, new { t = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

            tx = await ((DbConnection)_db).BeginTransactionAsync(SwapIsolation, ct).ConfigureAwait(false);
            var p = new { t = tenantId };

            var before = await _db.QuerySingleAsync<DiffRow>(new CommandDefinition(RestoreSwapSql.CountEmployeeDiffBefore, p, tx, cancellationToken: ct)).ConfigureAwait(false);
            var leftDiff = await Scalar(RestoreSwapSql.CountChatDiffBefore, p, tx, ct).ConfigureAwait(false);

            // ① 덧씌움(사원 7 · 방 1)
            await Exec(RestoreSwapSql.ClearStagedLinks, p, tx, ct).ConfigureAwait(false);
            await Exec(RestoreSwapSql.OverlayEmployees, p, tx, ct).ConfigureAwait(false);
            await Exec(RestoreSwapSql.OverlayChatMembers, p, tx, ct).ConfigureAwait(false);
            // ② ⓐ″
            await Exec(RestoreSwapSql.LinkBackupOnly, p, tx, ct).ConfigureAwait(false);
            // ②′ C4
            var narrowed = await Exec(RestoreSwapSql.NarrowChatLeave, new { t = tenantId, RestoreAt = restoreAtLocal }, tx, ct).ConfigureAwait(false);
            // ②″ (가′) · C8 — 사원마다 같은 두 문장 · 같은 차례
            var targets = (await _db.QueryAsync<string>(new CommandDefinition(RestoreSwapSql.ResignTargets, p, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            long linesOff = 0, docs = 0;
            foreach (var e in targets)
            {
                var q = new { TenantId = tenantId, EmployeeId = e };
                linesOff += await Exec(RestoreSwapSql.ResignLinesOff, q, tx, ct).ConfigureAwait(false);
                docs += await Exec(RestoreSwapSql.ResignDocsRecall, q, tx, ct).ConfigureAwait(false);
            }

            // ③ 덧씌운 뒤 검사
            if (await Scalar(RestoreSwapSql.CountLinkOverlap, p, tx, ct).ConfigureAwait(false) > 0)
                return await RollbackStop<RestoreOverlayCounts>(tx, SafeRestoreDenyCodes.VerifyFailed, "계정 연결 겹침").ConfigureAwait(false);
            if (await Scalar(RestoreSwapSql.CountOverlayMismatch, p, tx, ct).ConfigureAwait(false) > 0)
                return await RollbackStop<RestoreOverlayCounts>(tx, SafeRestoreDenyCodes.VerifyFailed, "C3′ 덧씌움 같음 어긋남").ConfigureAwait(false);
            if (await Scalar(RestoreSwapSql.CountBackupOnlyLinkMismatch, p, tx, ct).ConfigureAwait(false) > 0)
                return await RollbackStop<RestoreOverlayCounts>(tx, SafeRestoreDenyCodes.VerifyFailed, "ⓐ″ 다시 셈 어긋남").ConfigureAwait(false);
            if (await Scalar(RestoreSwapSql.CountLinkToMissingUser, p, tx, ct).ConfigureAwait(false) > 0)
                return await RollbackStop<RestoreOverlayCounts>(tx, SafeRestoreDenyCodes.VerifyFailed, "없는 계정 연결").ConfigureAwait(false);

            var candidates = await Scalar(RestoreSwapSql.CountBCandidates, p, tx, ct).ConfigureAwait(false);
            var newer = (await _db.QueryAsync<string>(new CommandDefinition(RestoreSwapSql.ListNewerAccountsAfterBPrime, p, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            if (newer.Count > 0)
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return new RestoreSwapResult<RestoreOverlayCounts>
                {
                    Step = SafeRestoreStep.Verify, DenyCode = SafeRestoreDenyCodes.NewerAccountsMissing, DataUnchanged = true,
                    LogDetail = $"ⓑ′ 밖 남는 계정 {newer.Count}", AccountNames = newer,
                    Message = RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.NewerAccountsMissing, newer),
                };
            }

            var counts = new RestoreOverlayCounts
            {
                RoleDiff = before.RoleDiff, ActiveDiff = before.ActiveDiff, LinkDiff = before.LinkDiff, LeftAtDiff = leftDiff,
                ChatNarrowed = narrowed, LinesOff = linesOff, DocsRecalled = docs,
                BothEmployees = await Scalar(RestoreSwapSql.CountBothEmployees, p, tx, ct).ConfigureAwait(false),
                LiveOnlyEmployees = await Scalar(RestoreSwapSql.CountLiveOnlyEmployees, p, tx, ct).ConfigureAwait(false),
                BothChat = await Scalar(RestoreSwapSql.CountBothChat, p, tx, ct).ConfigureAwait(false),
                LiveOnlyChat = await Scalar(RestoreSwapSql.CountLiveOnlyChat, p, tx, ct).ConfigureAwait(false),
                BPrimeExempted = candidates,
                Elapsed = sw.Elapsed,
            };
            await tx.CommitAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("안전 복원 ④-끝 덧씌움 끝 tenantId={TenantId} {Detail} 걸린 시간={Ms}ms", tenantId, counts.AuditDetail(), (long)sw.Elapsed.TotalMilliseconds);
            return new RestoreSwapResult<RestoreOverlayCounts> { Ok = true, Step = SafeRestoreStep.Verify, DataUnchanged = true, Message = "", Value = counts };
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 ④-끝 덧씌움 실패(임시 표만 · 라이브 손 0) tenantId={TenantId}", tenantId);
            if (tx is not null) await TryRollbackAsync(tx).ConfigureAwait(false);
            return Stop<RestoreOverlayCounts>(SafeRestoreStep.Verify, SafeRestoreDenyCodes.VerifyFailed, "덧씌움 질의 실패");
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
            await ResetSessionAsync().ConfigureAwait(false);
        }
    }

    // ═══ ⑤ 교체(한 거래) ═════════════════════════════════════════════

    /// <summary>
    /// ⑤ — 감사 「교체 시작」(실패 = 멈춤 · DB 손 0 · H1) → 세션(sql_mode · 외래키 켬 · 격리 명시) → 거래 → 격리 읽기 → C1′ 네 셈 → 지우기(자식 먼저) →
    /// 붓기(부모 먼저) → 기록 success(1줄) → 커밋 → 감사 「교체 끝」 에 셈(거래 안 감사 0) → 사본 · 임시 표 지움.
    /// </summary>
    public async Task<RestoreSwapResult<RestoreSwapReport>> SwapAsync(
        string tenantId, string userId, string restoreId, RestoreVerifyReport verify, RestoreOverlayCounts counts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(counts);

        try
        {
            await _audit.AppendAsync(new LocalAuditRecord(tenantId, userId, LocalAuditAction.SwapStarted, LocalAuditResult.Started, restoreId,
                string.Create(CultureInfo.InvariantCulture, $"tables={verify.Tables.Count}")), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is LocalAuditWriteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "안전 복원 ⑤ 앞 감사 쓰기 실패 = 멈춤(DB 손 0) tenantId={TenantId}", tenantId);
            return Stop<RestoreSwapReport>(SafeRestoreStep.Swap, SafeRestoreDenyCodes.AuditWriteFailed, "감사 교체 시작 실패");
        }

        var sw = Stopwatch.StartNew();
        DbTransaction? tx = null;
        var committing = false;
        string isolation = "";
        long deleted = 0, inserted = 0;
        try
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION sql_mode = @Mode, foreign_key_checks = 1, unique_checks = 1, autocommit = 1",
                new { Mode = RestoreSwapSql.SwapSqlMode }, cancellationToken: ct)).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition("SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ", cancellationToken: ct)).ConfigureAwait(false);

            tx = await ((DbConnection)_db).BeginTransactionAsync(SwapIsolation, ct).ConfigureAwait(false);
            var p = new { t = tenantId };

            isolation = await Guarded<string>(RestoreSwapSql.ReadIsolation, p, tx, ct).ConfigureAwait(false) ?? "";
            if (!string.Equals(isolation, ExpectedIsolation, StringComparison.OrdinalIgnoreCase))
                return await RollbackStop<RestoreSwapReport>(tx, SafeRestoreDenyCodes.SwapFailed, $"격리 수준 {isolation}", SafeRestoreStep.Swap).ConfigureAwait(false);

            // C1′ 네 셈(사원 · 방 참여) — 잠그며 읽기 · 쓰기 0
            var aE = await GuardedCount(RestoreSwapSql.C1EmployeesDiff, p, tx, ct).ConfigureAwait(false);
            var bE = await GuardedCount(RestoreSwapSql.C1EmployeesBoth, p, tx, ct).ConfigureAwait(false);
            var cE = await GuardedCount(RestoreSwapSql.C1EmployeesLiveOnly, p, tx, ct).ConfigureAwait(false);
            var dE = await GuardedCount(RestoreSwapSql.C1EmployeesNewerLinked, p, tx, ct).ConfigureAwait(false);
            var aC = await GuardedCount(RestoreSwapSql.C1ChatDiff, p, tx, ct).ConfigureAwait(false);
            var bC = await GuardedCount(RestoreSwapSql.C1ChatBoth, p, tx, ct).ConfigureAwait(false);
            var cC = await GuardedCount(RestoreSwapSql.C1ChatLiveOnly, p, tx, ct).ConfigureAwait(false);
            if (aE != 0 || bE != counts.BothEmployees || cE != counts.LiveOnlyEmployees || dE != 0
                || aC != 0 || bC != counts.BothChat || cC != counts.LiveOnlyChat)
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                await AuditQuietlyAsync(tenantId, userId, LocalAuditAction.SwapFailed, LocalAuditResult.Failed, restoreId, "c1recheck").ConfigureAwait(false);
                return new RestoreSwapResult<RestoreSwapReport>
                {
                    Step = SafeRestoreStep.Swap, DenyCode = SafeRestoreDenyCodes.VerifyFailed, DataUnchanged = true,
                    LogDetail = string.Create(CultureInfo.InvariantCulture, $"C1′ 어긋남 a={aE} b={bE}/{counts.BothEmployees} c={cE}/{counts.LiveOnlyEmployees} d={dE} ca={aC} cb={bC}/{counts.BothChat} cc={cC}/{counts.LiveOnlyChat}"),
                    Message = RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.VerifyFailed, dataChanged: true),
                };
            }

            foreach (var sql in verify.Order.DeleteStatements)
                deleted += await GuardedExec(sql, p, tx, ct).ConfigureAwait(false);
            if (_testHook is not null) await _testHook("after_delete", ct).ConfigureAwait(false);
            foreach (var sql in verify.Order.InsertStatements)
                inserted += await GuardedExec(sql, p, tx, ct).ConfigureAwait(false);

            var marked = await GuardedExec(RestoreSwapSql.MarkHistorySuccess, new { Now = LocalNow(), R = restoreId, t = tenantId }, tx, ct).ConfigureAwait(false);
            if (marked != 1)
                return await RollbackStop<RestoreSwapReport>(tx, SafeRestoreDenyCodes.SwapFailed, $"기록 갱신 줄 {marked}", SafeRestoreStep.Swap).ConfigureAwait(false);

            committing = true;
            await tx.CommitAsync(ct).ConfigureAwait(false);
            committing = false;
        }
        catch (Exception ex) when (ex is DbException or RestoreSwapRejectedException or InvalidOperationException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "안전 복원 ⑤ 교체 실패 tenantId={TenantId} restoreId={RestoreId} 커밋중={Committing}", tenantId, restoreId, committing);
            if (tx is not null && !committing) await TryRollbackAsync(tx).ConfigureAwait(false);
            await AuditQuietlyAsync(tenantId, userId, LocalAuditAction.SwapFailed, LocalAuditResult.Failed, restoreId, committing ? "commit-unknown" : "rollback").ConfigureAwait(false);
            // 커밋 도중 끊김 = 커밋됐는지 모름 → 「확인 필요」(거짓 안심 0) · 그 밖 = 되돌림(커밋 전 끊김은 서버가 되돌림 · 본 §7)
            return committing
                ? new RestoreSwapResult<RestoreSwapReport>
                {
                    Step = SafeRestoreStep.Swap, DenyCode = SafeRestoreDenyCodes.CheckNeeded, DataUnchanged = false, LogDetail = "커밋 결과 모름",
                    Message = RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.CheckNeeded),
                }
                : Stop<RestoreSwapReport>(SafeRestoreStep.Swap, SafeRestoreDenyCodes.SwapFailed, "교체 거래 되돌림");
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
            await ResetSessionAsync().ConfigureAwait(false);   // 모든 길(멈춤 · 실패 · 성공)에서 세션 되돌림
        }

        // 커밋 뒤 — 감사 끝 줄(셈) · 사본 · 임시 표 지움(실패해도 교체는 끝 · 정본 = restore_history)
        var elapsed = sw.Elapsed;
        var audited = await AuditQuietlyAsync(tenantId, userId, LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded, restoreId, counts.AuditDetail()).ConfigureAwait(false);
        await CleanupQuietlyAsync(tenantId).ConfigureAwait(false);
        _logger.LogInformation("안전 복원 ⑤ 교체 끝 tenantId={TenantId} restoreId={RestoreId} 지움={Deleted} 부음={Inserted} 걸린 시간={Ms}ms {Detail}",
            tenantId, restoreId, deleted, inserted, (long)elapsed.TotalMilliseconds, counts.AuditDetail());
        return new RestoreSwapResult<RestoreSwapReport>
        {
            Ok = true, Step = SafeRestoreStep.Swap, DataUnchanged = false, Message = "",
            Value = new RestoreSwapReport
            {
                Counts = counts, Isolation = isolation, DeletedRows = deleted, InsertedRows = inserted, Elapsed = elapsed, AuditCompleted = audited,
            },
        };
    }

    // ═══ 안쪽 ═════════════════════════════════════════════════════════

    /// <summary>칸 같음 — 라이브 ↔ 임시 표(양쪽 · 이름 · 차례 · 꼴 · 빈 값 · 문자셋 · 정렬 · 생성 칸). 0 이어야 함.</summary>
    private const string ColumnDiffSql =
        """
        SELECT CAST(COALESCE(SUM(n), 0) AS SIGNED) FROM (
          SELECT COUNT(*) AS n FROM information_schema.COLUMNS l
          LEFT JOIN information_schema.COLUMNS s
            ON s.TABLE_SCHEMA = l.TABLE_SCHEMA AND s.TABLE_NAME = CONCAT('rs_new__', l.TABLE_NAME) AND s.COLUMN_NAME = l.COLUMN_NAME
          WHERE l.TABLE_SCHEMA = DATABASE() AND l.TABLE_NAME IN @T
            AND (s.COLUMN_NAME IS NULL
                 OR NOT (s.ORDINAL_POSITION <=> l.ORDINAL_POSITION AND s.COLUMN_TYPE <=> l.COLUMN_TYPE
                         AND s.IS_NULLABLE <=> l.IS_NULLABLE AND s.CHARACTER_SET_NAME <=> l.CHARACTER_SET_NAME
                         AND s.COLLATION_NAME <=> l.COLLATION_NAME AND s.IS_GENERATED <=> l.IS_GENERATED
                         AND s.GENERATION_EXPRESSION <=> l.GENERATION_EXPRESSION))
          UNION ALL
          SELECT COUNT(*) AS n FROM information_schema.COLUMNS s
          WHERE s.TABLE_SCHEMA = DATABASE() AND LEFT(s.TABLE_NAME, 8) = 'rs_new__'
            AND SUBSTRING(s.TABLE_NAME, 9) IN @T
            AND NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS l
                            WHERE l.TABLE_SCHEMA = s.TABLE_SCHEMA AND l.TABLE_NAME = SUBSTRING(s.TABLE_NAME, 9) AND l.COLUMN_NAME = s.COLUMN_NAME)
        ) z
        """;

    private async Task<string> FingerprintFromAsync(string migTable, string efTable, CancellationToken ct)
    {
        if (!RestoreSwapSql.PlainName.IsMatch(migTable) || !RestoreSwapSql.PlainName.IsMatch(efTable))
            throw new InvalidOperationException("판 대조 표 이름 꼴 밖");
        var mig = (await _db.QueryAsync<string>(new CommandDefinition(
            $"SELECT migration_id FROM `{migTable}` WHERE success = 1", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var ef = (await _db.QueryAsync<string>(new CommandDefinition(
            $"SELECT MigrationId FROM `{efTable}`", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        mig.Sort(StringComparer.Ordinal);
        ef.Sort(StringComparer.Ordinal);
        // RestoreSafePath.ComputeSchemaFingerprintAsync 와 같은 글 꼴(표 이름표는 원래 이름)
        var text = "schema_migrations\n" + string.Join("\n", mig) + "\n__efmigrationshistory\n" + string.Join("\n", ef);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private Task<int> Exec(string sql, object p, IDbTransaction tx, CancellationToken ct) =>
        _db.ExecuteAsync(new CommandDefinition(sql, p, tx, commandTimeout: 600, cancellationToken: ct));

    private Task<long> Scalar(string sql, object p, IDbTransaction tx, CancellationToken ct) =>
        _db.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, tx, commandTimeout: 600, cancellationToken: ct));

    /// <summary>⑤ 거래 안 쓰기 — 허용 검사를 지난 것만.</summary>
    private Task<int> GuardedExec(string sql, object p, IDbTransaction tx, CancellationToken ct)
    {
        RestoreSwapStatementCheck.Verify(sql);
        return _db.ExecuteAsync(new CommandDefinition(sql, p, tx, commandTimeout: 3600, cancellationToken: ct));
    }

    private Task<long> GuardedCount(string sql, object p, IDbTransaction tx, CancellationToken ct)
    {
        RestoreSwapStatementCheck.Verify(sql);
        return _db.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, tx, commandTimeout: 600, cancellationToken: ct));
    }

    private Task<T?> Guarded<T>(string sql, object p, IDbTransaction tx, CancellationToken ct)
    {
        RestoreSwapStatementCheck.Verify(sql);
        return _db.ExecuteScalarAsync<T?>(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    private async Task SetSessionAsync(CancellationToken ct)
    {
        await _db.ExecuteAsync(new CommandDefinition("SET SESSION autocommit = 1", cancellationToken: ct)).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition("SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ", cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task ResetSessionAsync()
    {
        try
        {
            if (_db.State != ConnectionState.Open) return;
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION sql_mode = @@GLOBAL.sql_mode, foreign_key_checks = 1, unique_checks = 1, autocommit = @@GLOBAL.autocommit, transaction_isolation = @@GLOBAL.transaction_isolation",
                cancellationToken: CancellationToken.None)).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 세션 설정 되돌리기 실패 — 연결을 닫음");
            _db.Close();
        }
    }

    private async Task TryRollbackAsync(DbTransaction tx)
    {
        try
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // 연결이 끊겼으면 서버가 커밋 안 된 거래를 되돌림(본 §7) — 기록만
            _logger.LogWarning(ex, "안전 복원 거래 되돌리기 응답 없음(연결 끊김이면 서버가 되돌림)");
        }
    }

    private async Task<RestoreSwapResult<T>> RollbackStop<T>(DbTransaction tx, string code, string detail, SafeRestoreStep step = SafeRestoreStep.Verify) where T : class
    {
        await TryRollbackAsync(tx).ConfigureAwait(false);
        return Stop<T>(step, code, detail);
    }

    private async Task<bool> AuditQuietlyAsync(string tenantId, string userId, LocalAuditAction action, LocalAuditResult result, string opId, string detail)
    {
        try
        {
            await _audit.AppendAsync(new LocalAuditRecord(tenantId, userId, action, result, opId, detail), CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is LocalAuditWriteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "안전 복원 감사 줄 쓰기 실패(정본 = restore_history) tenantId={TenantId} action={Action}", tenantId, action);
            return false;
        }
    }

    private async Task CleanupQuietlyAsync(string tenantId)
    {
        try
        {
            if (_db.State == ConnectionState.Open)
                await _db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.DropSnapshot, cancellationToken: CancellationToken.None)).ConfigureAwait(false);
            await _safePath.DropStagingTablesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            // 다음 기동 때 청소(본 §7 · Program 기동 블록) — 라이브 손 0
            _logger.LogWarning(ex, "안전 복원 임시 표 지우기 실패 — 다음 기동 때 다시 지움 tenantId={TenantId}", tenantId);
        }
    }

    private async Task<RestoreSwapResult<RestoreSwapReport>> StopAsync(string tenantId, string restoreId, RestoreSwapResult<RestoreSwapReport> r)
    {
        try
        {
            await FailHistoryAsync(tenantId, restoreId, r.DenyCode ?? SafeRestoreDenyCodes.SwapFailed, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "안전 복원 멈춤 기록 실패 tenantId={TenantId} restoreId={RestoreId}", tenantId, restoreId);
        }
        await CleanupQuietlyAsync(tenantId).ConfigureAwait(false);
        return r;
    }

    private static RestoreSwapResult<TOut> Relay<TOut>(RestoreStepResult r) where TOut : class => new()
    {
        Step = r.Step, DenyCode = r.DenyCode, DataUnchanged = r.DataUnchanged, LogDetail = r.LogDetail,
        Message = r switch
        {
            RestoreSwapResult<RestoreVerifyReport> v => v.Message,
            RestoreSwapResult<RestoreOverlayCounts> o => o.Message,
            _ => RestoreOverlayRules.MessageFor(r.DenyCode),
        },
        AccountNames = r is RestoreSwapResult<RestoreOverlayCounts> oc ? oc.AccountNames : Array.Empty<string>(),
    };

    private static RestoreSwapResult<T> Stop<T>(SafeRestoreStep step, string code, string detail) where T : class =>
        new() { Step = step, DenyCode = code, DataUnchanged = true, LogDetail = detail, Message = RestoreOverlayRules.MessageFor(code) };

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Broken) _db.Close();
        if (_db.State != ConnectionState.Open)
        {
            if (_db is DbConnection dc) await dc.OpenAsync(ct).ConfigureAwait(false);
            else _db.Open();
        }
    }

    private sealed class DiffRow
    {
        public long RoleDiff { get; set; }
        public long ActiveDiff { get; set; }
        public long LinkDiff { get; set; }
    }

    private sealed class FkRow
    {
        public string Name { get; set; } = "";
        public string Child { get; set; } = "";
        public string ChildColumn { get; set; } = "";
        public string Parent { get; set; } = "";
        public string ParentColumn { get; set; } = "";
        public long Ordinal { get; set; }
    }
}
