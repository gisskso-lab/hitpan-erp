using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 이 게이트는 프로세스 환경변수(DB_* · PATH)를 바꾼다 — 다른 시험과 동시에 돌면 서로를 오염시킨다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackupCredentialGateCollection
{
    public const string Name = "BackupCredentialGate";
}

/// <summary>
/// 🔴 <b>BackupCredentialGate</b> — 20260911작5 백업 비밀번호 결함 핫픽스(1.3.40).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 재나</b> — 실물 사고: 열린 연결의 <c>ConnectionString</c> 에서 비번이 빠져 덤프가 <c>"-p"</c> 빈 값으로
/// 떠서 숨은 콘솔에서 비번 입력을 기다리며 멈췄다(선행검증 20260911 격리재현 §2·§4).
/// 이 게이트는 <b>글자가 아니라 동작</b>을 잰다 — 실제 MariaDB · 실제 덤프 · 실제 프로세스 · 실제 이력 행.
/// </para>
/// <para>
/// 🔴 <b>운영과 같은 연결 조립</b> — 서비스에 주는 연결은 <c>InfrastructureExtensions.cs:55</c> 와 같은 모양
/// (<c>User=…;Password=…</c>)으로 만들고 <b>연 뒤</b> 넘긴다. <c>Pwd=</c> 형식을 따로 만들면 원인을 못 잰다.
/// 설정 원본(<c>TenantConfigReader</c>)은 환경변수 <c>DB_*</c> 로 준다 — 시작 때 db.conf 가 가리지 않는지 확인한다.
/// </para>
/// <para>
/// ⚠️ 리눅스 CI 한계(조용한 축소 금지): 숨은 콘솔의 비번 입력 대기 · <c>C:\Windows\System32</c> 경로의 모양 ·
/// 폴더 권한(ACL)은 리눅스에서 재현되지 않는다. 원인 대조(수정 전 FAIL)는 Windows 격리 인스턴스 실측이 근거다(개발명세서).
/// </para>
/// <para>
/// ⚠️ 헌법 #39 — 로컬에서는 <c>HITPAN_DB_PORT</c> 를 명시한 격리 인스턴스에서만 돈다(기본값으로 운영 3306 에 붙지 않는다).
/// 자기 회사(<c>GATE-BK-…</c>) 행과 자기 표·폴더만 만들고 지운다. 죽이는 프로세스는 이 시험 프로세스의 자손 중
/// 시험 시작 뒤에 생긴 덤프만이다.
/// </para>
/// </remarks>
[Collection(BackupCredentialGateCollection.Name)]
public sealed class BackupCredentialGateTests
{
    private static readonly string[] DumpNames = { "mariadb-dump", "mysqldump" };
    private static readonly string[] ClientNames = { "mariadb-dump", "mysqldump", "mariadb", "mysql" };
    private const string RestoreClosedNotice = "복원은 안전장치를 보강한 다음 업데이트에서 열립니다";
    private static readonly string[] ConfigKeys = { "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD" };

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK1 — 연결을 연 뒤에도 실제 덤프가 끝난다
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK1 — 연결을 <b>연 뒤</b> <c>RunBackupAsync</c> 가 60초 안에 성공하고, 0바이트 아닌 파일에 <c>CREATE TABLE</c> 이 있고, 이력이 success.
    /// 고른 덤프 실행파일 로그가 MariaDB 쪽이고, 비밀번호 값은 로그·이유·이력 어디에도 없다.
    /// <para>무력화: 자격증명을 다시 <c>_db.ConnectionString</c> 에서 읽으면 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_BK1_연결을_연_뒤에도_실제_덤프가_끝나고_파일이_생긴다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK1_연결을_연_뒤에도_실제_덤프가_끝나고_파일이_생긴다)); return; }
        AssertNoDbConfShadow();

        using var fx = GateFixture.Create(passwordOverride: null);
        var outcome = await RunBoundedAsync(
            () => fx.Service.RunBackupAsync(fx.TenantId, "gate", CancellationToken.None), TimeSpan.FromSeconds(60));
        var alive = fx.SurvivingDumps();
        if (!outcome.Finished) { fx.KillNewDumps(); await SettleAsync(outcome.Task); }

        var v = new List<string>();
        if (!outcome.Finished) v.Add($"60초 안에 안 끝났다(덤프 멈춤) · 살아 있는 덤프 {alive.Count}개");
        if (outcome.Thrown is not null) v.Add($"예외로 빠졌다: {outcome.Thrown.GetType().Name}: {outcome.Thrown.Message}");
        if (outcome.Response is { } r)
        {
            // 성공 판정은 반환값·파일·이력으로만 한다 — stderr 경고 줄 유무와 무관(병렬이슈28 C28-1).
            if (!r.Success) v.Add($"Success=false · Error={r.Error}");
            var file = r.PrimaryFile;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) v.Add("백업 파일이 없다");
            else
            {
                var len = new FileInfo(file).Length;
                if (len == 0) v.Add("백업 파일 0바이트");
                else if (!File.ReadAllText(file).Contains("CREATE TABLE", StringComparison.Ordinal))
                    v.Add($"백업 파일({len}B)에 CREATE TABLE 이 없다");
            }
        }
        var files = fx.BackupFiles();
        var hist = fx.LatestHistory();
        if (hist.Status != "success") v.Add($"backup_history.status={hist.Status ?? "(없음)"} · 파일 {files.Count}개({string.Join(",", files.Select(f => new FileInfo(f).Length + "B"))})");
        else if (!string.IsNullOrEmpty(hist.Error)) v.Add($"성공인데 error_message 가 남았다: {hist.Error}");

        var exeLine = fx.Logger.Lines.FirstOrDefault(l => l.Contains("덤프 실행파일", StringComparison.Ordinal));
        if (exeLine is null) v.Add("고른 덤프 실행파일 로그 1줄이 없다");
        else if (!(exeLine.Contains("mariadb-dump", StringComparison.OrdinalIgnoreCase) || exeLine.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
                 || exeLine.Contains("MySQL Server", StringComparison.OrdinalIgnoreCase))
            v.Add($"고른 덤프 실행파일이 MariaDB 쪽이 아니다: {exeLine}");
        fx.CheckNoSecretLeak(v, outcome.Response, hist.Error);

        AssertNoViolations("G-BK1", v, outcome);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK2 — 설정 비번이 비면 프로세스를 띄우기 전에 즉시 실패
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK2 — 설정의 <c>DB_PASSWORD</c> 가 빈 값이면 5초 안에 Success=false · 이유에 「비밀번호」 · 덤프 프로세스 0 · 백업 파일 0 · 이력 failed.
    /// <para>무력화: 빈 비번 사전 차단을 빼면 빨간불(1045 로 떨어지며 0바이트 파일이 남고 이유에 설정 문구가 없다).</para>
    /// </summary>
    [Fact]
    public async Task G_BK2_설정_비번이_비면_덤프를_띄우기_전에_즉시_실패한다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK2_설정_비번이_비면_덤프를_띄우기_전에_즉시_실패한다)); return; }
        AssertNoDbConfShadow();

        using var fx = GateFixture.Create(passwordOverride: "");
        var outcome = await RunBoundedAsync(
            () => fx.Service.RunBackupAsync(fx.TenantId, "gate", CancellationToken.None), TimeSpan.FromSeconds(5));
        var alive = fx.SurvivingDumps();
        if (!outcome.Finished) { fx.KillNewDumps(); await SettleAsync(outcome.Task); }

        var v = new List<string>();
        if (!outcome.Finished) v.Add($"5초 안에 안 끝났다(비번 입력 대기) · 살아 있는 덤프 {alive.Count}개");
        else if (alive.Count > 0) v.Add($"반환 뒤에도 덤프 {alive.Count}개가 살아 있다");
        if (outcome.Thrown is not null) v.Add($"예외로 빠졌다: {outcome.Thrown.GetType().Name}: {outcome.Thrown.Message}");
        if (outcome.Response is { } r)
        {
            if (r.Success) v.Add("빈 비번인데 Success=true");
            if (r.Error is null || !r.Error.Contains("비밀번호", StringComparison.Ordinal))
                v.Add($"이유에 비밀번호 설정 문구가 없다: {r.Error}");
        }
        var files = fx.BackupFiles();
        if (files.Count > 0) v.Add($"백업 파일이 생겼다 {files.Count}개({string.Join(",", files.Select(f => new FileInfo(f).Length + "B"))})");
        var hist = fx.LatestHistory();
        if (hist.Status != "failed") v.Add($"backup_history.status={hist.Status ?? "(없음)"}");
        fx.CheckNoSecretLeak(v, outcome.Response, hist.Error);

        AssertNoViolations("G-BK2", v, outcome);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK3 — 덤프 도중 요청이 끊기면 덤프도 끝나고 기록은 failed
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK3 — 게이트 전용 표를 다른 세션이 <c>LOCK TABLES … WRITE</c> 로 잡아 덤프를 붙잡고 취소하면
    /// 10초 안에 (예외 아닌) 반환 · 시험 뒤 생긴 덤프 프로세스 전부 종료 · 이력 failed + 「요청이 끊겨」.
    /// <para>무력화: 취소 시 프로세스 종료를 빼면 「덤프 생존」, 실패 UPDATE 를 다시 <c>ct</c> 로 하면 「running」·예외로 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_BK3_덤프_도중_취소하면_덤프가_끝나고_기록은_failed()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK3_덤프_도중_취소하면_덤프가_끝나고_기록은_failed)); return; }
        AssertNoDbConfShadow();

        using var fx = GateFixture.Create(passwordOverride: null);
        var table = fx.CreateGateTable();
        using var lockConn = OpenAdmin();
        lockConn.Execute($"LOCK TABLES `{table}` WRITE");

        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => fx.Service.RunBackupAsync(fx.TenantId, "gate", cts.Token));

        // 덤프가 잠금에 걸린 것을 서버에서 본 뒤(최소 3초 · 최대 15초) 취소한다.
        var lockObserved = false;
        while (sw.Elapsed < TimeSpan.FromSeconds(15) && !task.IsCompleted)
        {
            if (!lockObserved) lockObserved = fx.DumpWaitingOnLock(table);
            if (lockObserved && sw.Elapsed >= TimeSpan.FromSeconds(3)) break;
            await Task.Delay(250);
        }
        var completedBeforeCancel = task.IsCompleted;
        cts.Cancel();
        var cancelAt = sw.Elapsed;

        var outcome = await AwaitBoundedAsync(task, TimeSpan.FromSeconds(10));
        var alive = fx.SurvivingDumps();
        if (!outcome.Finished || alive.Count > 0) fx.KillNewDumps();
        fx.ReleaseGateTable(lockConn, table);
        if (!outcome.Finished) await SettleAsync(task);

        var v = new List<string>();
        if (completedBeforeCancel) v.Add("취소 전에 끝나 버렸다 — 덤프 도중 취소를 잰 것이 아니다");
        if (!lockObserved) v.Add("덤프가 잠금에 걸린 것을 서버에서 못 봤다(덤프가 DB 에 붙기 전에 멈췄거나 못 떴다)");
        if (!outcome.Finished) v.Add($"취소({cancelAt.TotalSeconds:F1}s) 뒤 10초 안에 반환하지 않았다");
        if (alive.Count > 0) v.Add($"취소 뒤 덤프 프로세스가 살아 있다: {string.Join(",", alive.Select(a => $"{a.Name}#{a.Pid}"))}");
        if (outcome.Thrown is not null) v.Add($"반환이 아니라 예외로 빠졌다: {outcome.Thrown.GetType().Name}: {outcome.Thrown.Message}");
        if (outcome.Response is { } r)
        {
            if (r.Success) v.Add("취소했는데 Success=true");
            if (r.Error is null || !r.Error.Contains("끊겨", StringComparison.Ordinal))
                v.Add($"이유에 「요청이 끊겨」가 없다: {r.Error}");
        }
        var hist = fx.LatestHistory();
        if (hist.Status != "failed") v.Add($"backup_history.status={hist.Status ?? "(없음)"}");
        else if (hist.Error is null || !hist.Error.Contains("끊겨", StringComparison.Ordinal))
            v.Add($"backup_history.error_message 에 「끊겨」가 없다: {hist.Error}");
        fx.CheckNoSecretLeak(v, outcome.Response, hist.Error);

        AssertNoViolations("G-BK3", v, outcome);
    }

    /// <summary>
    /// 대조군 — 같은 게이트 표를 만들되 <b>잠그지 않으면</b> 덤프가 끝나 Success=true 이고, 파일에 그 표가 들어 있다.
    /// (잠금 없이도 실패하면 G-BK3 은 잠금이 아니라 다른 것을 재고 있는 것이다.)
    /// </summary>
    [Fact]
    public async Task G_BK3_대조군_잠금이_없으면_같은_덤프가_끝난다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK3_대조군_잠금이_없으면_같은_덤프가_끝난다)); return; }
        AssertNoDbConfShadow();

        using var fx = GateFixture.Create(passwordOverride: null);
        var table = fx.CreateGateTable();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var outcome = await RunBoundedAsync(
            () => fx.Service.RunBackupAsync(fx.TenantId, "gate", cts.Token), TimeSpan.FromSeconds(60));
        var alive = fx.SurvivingDumps();
        if (!outcome.Finished || alive.Count > 0) fx.KillNewDumps();
        using (var admin = OpenAdmin()) fx.ReleaseGateTable(admin, table);
        if (!outcome.Finished) await SettleAsync(outcome.Task);

        var v = new List<string>();
        if (!outcome.Finished) v.Add("잠금 없이도 60초 안에 안 끝났다");
        if (outcome.Thrown is not null) v.Add($"예외로 빠졌다: {outcome.Thrown.GetType().Name}: {outcome.Thrown.Message}");
        if (outcome.Response is { } r)
        {
            if (!r.Success) v.Add($"잠금 없이도 Success=false · Error={r.Error}");
            else if (r.PrimaryFile is null || !File.Exists(r.PrimaryFile)
                     || !File.ReadAllText(r.PrimaryFile).Contains(table, StringComparison.Ordinal))
                v.Add("백업 파일에 게이트 표가 없다 — 덤프가 그 표를 지나가지 않았다");
        }
        var hist = fx.LatestHistory();
        if (hist.Status != "success") v.Add($"backup_history.status={hist.Status ?? "(없음)"}");

        AssertNoViolations("G-BK3 대조군", v, outcome);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK6 — 복원은 1.3.40 에서 잠시 막는다 (C-11 사장님 결정 (가) 2026-09-11)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK6 — 회사명이 맞고 실제 파일이 있는 복원 요청을 보내도 5초 안에 Success=false + 안내 문구 ·
    /// 덤프·가져넣기 프로세스 0 · <c>restore_history</c> 새 줄 0 · 사전 백업 파일 0.
    /// (비번을 고치면 복원이 처음으로 실제로 돈다 → 트랜잭션 없는 가져넣기가 끊기면 DB 반쪽 — 병렬이슈26)
    /// <para>무력화: <c>RestoreAsync</c> 맨 앞 차단을 빼면 빨간불(이력 줄 · 사전 백업 파일 · 덤프 프로세스).</para>
    /// </summary>
    [Fact]
    public async Task G_BK6_복원은_맨_앞에서_막히고_아무것도_시작하지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK6_복원은_맨_앞에서_막히고_아무것도_시작하지_않는다)); return; }
        AssertNoDbConfShadow();

        using var fx = GateFixture.Create(passwordOverride: null);
        var company = fx.CreateCompany();
        var source = Path.Combine(fx.Folder, "gate_restore_source.sql");
        File.WriteAllText(source, "SELECT 1;\n");   // 차단이 빠져 가져넣기까지 가도 무해한 내용
        var req = new RestoreRequest { ExternalFilePath = source, ConfirmCompanyName = company };

        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => fx.Service.RestoreAsync(fx.TenantId, "gate-user", req, CancellationToken.None));
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) == task;
        var elapsed = sw.Elapsed;
        var alive = fx.SurvivingProcesses(ClientNames);
        if (!finished || alive.Count > 0) fx.KillNewDumps();
        if (!finished) await SettleAsync(task);

        var v = new List<string>();
        if (!finished) v.Add($"5초 안에 반환하지 않았다 · 살아 있는 덤프·가져넣기 {alive.Count}개");
        else if (alive.Count > 0) v.Add($"반환 뒤 덤프·가져넣기 프로세스가 살아 있다: {string.Join(",", alive.Select(a => $"{a.Name}#{a.Pid}"))}");
        if (finished)
        {
            if (task.IsFaulted) v.Add($"예외로 빠졌다: {task.Exception?.GetBaseException().Message}");
            else
            {
                var r = await task;
                if (r.Success) v.Add("복원이 Success=true");
                if (r.Error is null || !r.Error.Contains(RestoreClosedNotice, StringComparison.Ordinal))
                    v.Add($"안내 문구 「{RestoreClosedNotice}」가 없다: {r.Error}");
            }
        }
        var rows = fx.CountRestoreHistory();
        if (rows > 0) v.Add($"restore_history 에 새 줄 {rows}개");
        var pre = Directory.GetFiles(fx.Folder, "hitpan_pre_restore_*.sql");
        if (pre.Length > 0) v.Add($"사전 백업 파일이 생겼다 {pre.Length}개({string.Join(",", pre.Select(f => new FileInfo(f).Length + "B"))})");

        if (v.Count > 0) Assert.Fail($"G-BK6 위반 (경과 {elapsed.TotalSeconds:F1}s):\n  · " + string.Join("\n  · ", v));
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK7 — 실패한 덤프의 0바이트 파일이 보관 개수를 먹어 진짜 백업을 지우지 않는다 (C-16 · 병렬이슈30)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK7 — 보관 3 · 이전 성공 백업 3개 + 옛 버전이 남긴 0B 파일 3개(성공 백업보다 새것) →
    /// 틀린 비번으로 덤프 실패 3회 → 맞는 비번으로 성공 1회.
    /// 기대: 성공 · 이번 실패들이 남긴 파일 0 (C-16 ①) · 이전 성공 백업 중 새것 2개는 남는다 (②) ·
    /// 가장 오래된 1개만 지워진다(대조: 보관 개수는 여전히 지킨다) · 옛 0B 파일은 세지도 지우지도 않는다 (②).
    /// <para>무력화: 보관 계산의 0바이트 제외를 빼면 빨간불(0B 파일이 자리를 차지해 이전 성공 백업이 지워진다) ·
    /// 실패 경로의 출력 파일 삭제를 빼면 빨간불(이번 실패의 0B 파일이 남는다).</para>
    /// </summary>
    [Fact]
    public async Task G_BK7_실패한_덤프의_0바이트_파일이_보관개수를_먹지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_BK7_실패한_덤프의_0바이트_파일이_보관개수를_먹지_않는다)); return; }
        AssertNoDbConfShadow();

        const int keep = 3;
        using var fx = GateFixture.Create(passwordOverride: null);
        fx.SetRetention(keep);

        // 준비 — 만든 순서 = 이전 성공 3개(오래된 순) → 옛 0B 3개. 파일 이름은 초 단위라 실제 회차와 겹치지 않는 날짜로.
        var baseTime = DateTime.Now.AddDays(-10);
        var previous = new List<string>();
        var legacyEmpty = new List<string>();
        for (var i = 1; i <= keep; i++)
        {
            var f = Path.Combine(fx.Folder, $"hitpan_backup_20000101_00000{i}.sql");
            File.WriteAllText(f, $"-- gate previous successful backup {i}\nCREATE TABLE gate_prev_{i} (id INT);\n");
            File.SetCreationTime(f, baseTime.AddHours(i));
            previous.Add(f);
            await Task.Delay(20);
        }
        for (var i = 1; i <= keep; i++)
        {
            var f = Path.Combine(fx.Folder, $"hitpan_backup_20000102_00000{i}.sql");
            File.WriteAllBytes(f, Array.Empty<byte>());
            File.SetCreationTime(f, baseTime.AddDays(1).AddHours(i));
            legacyEmpty.Add(f);
            await Task.Delay(20);
        }
        var seeded = previous.Concat(legacyEmpty).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var v = new List<string>();
        fx.SetConfigPassword("gate-wrong-" + Guid.NewGuid().ToString("N")[..12]);
        for (var i = 1; i <= keep; i++)
        {
            await Task.Delay(1100);   // 백업 파일 이름이 초 단위 — 회차마다 다른 이름
            var failed = await RunBoundedAsync(
                () => fx.Service.RunBackupAsync(fx.TenantId, "gate", CancellationToken.None), TimeSpan.FromSeconds(30));
            if (!failed.Finished) { fx.KillNewDumps(); await SettleAsync(failed.Task); v.Add($"실패 회차 {i}: 30초 안에 안 끝났다"); }
            else if (failed.Thrown is not null) v.Add($"실패 회차 {i}: 예외로 빠졌다 {failed.Thrown.GetType().Name}");
            else if (failed.Response is { Success: true }) v.Add($"실패 회차 {i}: 틀린 비번인데 Success=true");
        }

        fx.SetConfigPassword(null);
        await Task.Delay(1100);
        var ok = await RunBoundedAsync(
            () => fx.Service.RunBackupAsync(fx.TenantId, "gate", CancellationToken.None), TimeSpan.FromSeconds(60));
        if (!ok.Finished) { fx.KillNewDumps(); await SettleAsync(ok.Task); v.Add("성공 회차: 60초 안에 안 끝났다"); }
        if (ok.Thrown is not null) v.Add($"성공 회차: 예외로 빠졌다 {ok.Thrown.GetType().Name}: {ok.Thrown.Message}");
        if (ok.Response is { Success: false } bad) v.Add($"성공 회차가 실패했다: {bad.Error}");

        var files = Directory.GetFiles(fx.Folder, "hitpan_backup_*.sql");
        var leftoverEmpty = files.Where(f => !seeded.Contains(f) && new FileInfo(f).Length == 0).Select(Path.GetFileName).ToList();
        if (leftoverEmpty.Count > 0) v.Add($"C-16 ① 이번 실패가 남긴 0바이트 파일 {leftoverEmpty.Count}개: {string.Join(",", leftoverEmpty)}");
        for (var i = 1; i < keep; i++)
            if (!File.Exists(previous[i])) v.Add($"C-16 ② 지워지면 안 되는 이전 성공 백업이 지워졌다: {Path.GetFileName(previous[i])}");
        if (File.Exists(previous[0]))
            v.Add($"대조: 보관 {keep} 인데 가장 오래된 성공 백업이 남았다(보관정책이 돌지 않았다): {Path.GetFileName(previous[0])}");
        var removedLegacy = legacyEmpty.Where(f => !File.Exists(f)).Select(Path.GetFileName).ToList();
        if (removedLegacy.Count > 0) v.Add($"C-16 ② 옛 0바이트 파일을 보관정책이 지웠다(세지도 지우지도 않아야 한다): {string.Join(",", removedLegacy)}");
        var newSuccess = files.Where(f => !seeded.Contains(f) && new FileInfo(f).Length > 0).ToList();
        if (newSuccess.Count != 1) v.Add($"이번 성공 백업 파일이 1개가 아니다: {newSuccess.Count}개");
        fx.CheckNoSecretLeak(v, ok.Response, fx.LatestHistory().Error);

        AssertNoViolations("G-BK7", v, ok);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK4 — 기본 백업 폴더는 절대 경로 · Windows 폴더 밖 · 제한 권한 (DB 없음 · 입력 주입)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK4 — 문서 폴더 빈 값 / 상대 경로 / Windows 폴더 하위 / 저장된 상대 경로 → 결과가 절대 경로 · Windows 폴더 밖.
    /// 대조: 쓸 수 있는 저장 경로는 그대로 돌려준다.
    /// C-8(Windows): 코드가 만드는 기본 폴더는 상속을 끊고 SYSTEM·Administrators·실행 계정만 모든 권한 ·
    /// 이미 있는 폴더에 그 셋 밖의 쓰기 권한자가 있으면 이유와 함께 실패 · 셋만 있으면 통과(대조).
    /// C-12(Windows · 병렬이슈29 · C-12 정정): Users 읽기 줄 거부 · 잘 알려진 그룹(LOCAL) 줄은 권한 종류와 무관하게 거부 ·
    /// 개별 관리자 사용자 줄 허용 · Backup 교차점 거부 · 상위 HitPan 교차점 거부 · 교차점 너머로 연 파일의 실제 위치는 기대 위치와 다르다(대조: 곧바로 연 파일은 같다).
    /// <para>무력화: 기본 폴더 결정을 <c>Path.Combine(문서폴더, "HitpanBackup")</c> 로 되돌리면 빨간불.
    /// 재분석 지점 검사(<c>ThrowIfReparsePointOnPathWindows</c>)를 건너뛰게 하면 ⓕ·ⓖ 빨간불.</para>
    /// </summary>
    [Fact]
    public void G_BK4_기본_백업_폴더는_절대경로이고_Windows_폴더_밖이다()
    {
        var method = typeof(BackupService).GetMethod("ResolveBackupFolderCore",
            BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, null);
        if (method is null)
            Assert.Fail("G-BK4: 기본 폴더 결정 함수 ResolveBackupFolderCore(stored, documents, windows, programData) 가 없다 — "
                      + "현행은 GetSettingsAsync 안에서 Path.Combine(MyDocuments, \"HitpanBackup\") 를 바로 쓰므로 "
                      + "문서 폴더가 빈 값일 때 상대 경로 \"HitpanBackup\" 이 저장되는 것을 막는 자리가 없다.");

        var root = Path.Combine(Path.GetTempPath(), "hitpan_bkgate_g4_" + Guid.NewGuid().ToString("N")[..8]);
        var win = Path.Combine(root, "Windows");
        var programData = Path.Combine(root, "ProgramData");
        var docs = Path.Combine(root, "Users", "me", "Documents");
        var okStored = Path.Combine(root, "D", "MyBackup");

        string Call(string? stored, string? documents) =>
            (string)method!.Invoke(null, new object?[] { stored, documents, win, programData })!;

        var v = new List<string>();
        void Must(string label, string? stored, string? documents)
        {
            var got = Call(stored, documents);
            if (!Path.IsPathFullyQualified(got)) v.Add($"{label}: 절대 경로가 아니다 → \"{got}\"");
            else if (IsUnder(got, win)) v.Add($"{label}: Windows 폴더 안이다 → \"{got}\"");
        }

        try
        {
            Must("문서 폴더 빈 값", null, "");
            Must("문서 폴더 null", null, null);
            Must("문서 폴더 상대 경로", null, "Documents");
            Must("문서 폴더가 Windows 하위", null, Path.Combine(win, "System32", "config", "systemprofile", "Documents"));
            Must("저장된 상대 경로(K3)", "HitpanBackup", "");
            Must("저장된 Windows 하위 경로(K3)", Path.Combine(win, "System32", "HitpanBackup"), "");

            // 대조 — 멀쩡한 값은 건드리지 않는다
            var keep = Call(okStored, docs);
            if (keep != okStored) v.Add($"대조: 쓸 수 있는 저장 경로를 바꿨다 \"{okStored}\" → \"{keep}\"");
            var fromDocs = Call(null, docs);
            if (fromDocs != Path.Combine(docs, "HitpanBackup")) v.Add($"대조: 문서 폴더가 멀쩡한데 기본값이 문서 폴더가 아니다 → \"{fromDocs}\"");

            // C-8 — 기본 폴더 권한
            var secure = typeof(BackupService).GetMethod("EnsureRestrictedBackupFolder",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (secure is null) v.Add("C-8: 기본 폴더를 제한 권한으로 만드는 함수 EnsureRestrictedBackupFolder(path) 가 없다");
            else if (OperatingSystem.IsWindows()) CheckRestrictedFolderWindows(secure, root, v);
            else Console.Error.WriteLine("[BackupCredentialGate] G-BK4 C-8 권한 · C-12 ⓓ~ⓖ(넓은 그룹·잘 알려진 그룹·개별 사용자 줄·교차점) · C-12 ②(연 파일 실제 위치) 단언은 Windows 전용 — 이 OS 에서는 재지 않았다.");
        }
        finally { TryDeleteDir(root); }

        if (v.Count > 0) Assert.Fail("G-BK4 위반:\n  · " + string.Join("\n  · ", v));
    }

    [SupportedOSPlatform("windows")]
    private static void CheckRestrictedFolderWindows(MethodInfo secure, string root, List<string> v)
    {
        var allowed = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        if (WindowsIdentity.GetCurrent().User is { } me) allowed.Add(me);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        // ⓐ 없던 폴더 — 만들면서 상속 끊고 셋만 모든 권한
        var fresh = Path.Combine(root, "pd", "HitPan", "Backup");
        var (ok1, err1) = InvokeSecure(secure, fresh);
        if (!ok1) v.Add($"C-8 ⓐ 새 기본 폴더를 못 만들었다: {err1}");
        else
        {
            var acl = new DirectoryInfo(fresh).GetAccessControl();
            if (!acl.AreAccessRulesProtected) v.Add("C-8 ⓐ 새 기본 폴더가 상위 권한을 상속한다");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains((SecurityIdentifier)rule.IdentityReference))
                    v.Add($"C-8 ⓐ 새 기본 폴더에 셋 밖 계정 권한이 있다: {rule.IdentityReference.Value} {rule.FileSystemRights}");
            }
            foreach (var sid in allowed)
            {
                var full = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                    .Any(r => r.AccessControlType == AccessControlType.Allow && sid.Equals(r.IdentityReference)
                              && (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
                if (!full) v.Add($"C-8 ⓐ 새 기본 폴더에 {sid.Value} 모든 권한이 없다");
            }

            // ⓒ 대조 — 셋만 있는 이미 있는 폴더는 통과
            var (ok3, err3) = InvokeSecure(secure, fresh);
            if (!ok3) v.Add($"C-8 ⓒ 대조: 권한이 바른 기존 폴더를 거부했다: {err3}");
        }

        // ⓑ 이미 있는 폴더에 Users 쓰기 권한 — 덤프를 쓰지 않고 이유와 함께 실패
        var hostile = Path.Combine(root, "pd2", "HitPan", "Backup");
        Directory.CreateDirectory(hostile);
        var di = new DirectoryInfo(hostile);
        var ds = di.GetAccessControl();
        ds.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        di.SetAccessControl(ds);
        var (ok2, err2) = InvokeSecure(secure, hostile);
        if (ok2) v.Add("C-8 ⓑ Users 쓰기 권한이 있는 기존 폴더를 받아들였다");
        else if (err2 is null || !err2.Contains("권한", StringComparison.Ordinal)) v.Add($"C-8 ⓑ 거부 이유에 권한 문구가 없다: {err2}");

        // ── C-12 (병렬이슈29 · 작업지시서 §11-3 C-12 · C-12 정정) ─────────────────────────
        // ⓓ Users 읽기 줄 — 코드가 보호 권한으로 만든 기본 폴더에 넓은 그룹의 읽기 줄이 붙으면 거부
        var readable = Path.Combine(root, "pd3", "HitPan", "Backup");
        var (okR0, errR0) = InvokeSecure(secure, readable);
        if (!okR0) v.Add($"C-12 ⓓ 준비: 기본 폴더를 못 만들었다: {errR0}");
        else
        {
            AddAllowRule(readable, users, FileSystemRights.ReadAndExecute);
            var (okR, errR) = InvokeSecure(secure, readable);
            if (okR) v.Add("C-12 ⓓ Users 읽기 권한 줄이 붙은 기본 폴더를 받아들였다");
            else if (errR is null || !errR.Contains("권한", StringComparison.Ordinal)) v.Add($"C-12 ⓓ 거부 이유에 권한 문구가 없다: {errR}");
        }

        // ⓓ' 잘 알려진 그룹(LOCAL · S-1-2-0) 의 폴더 탐색 줄 — 권한 줄 주체 허용 목록이라 권한 종류와 무관하게 거부
        var localGroup = Path.Combine(root, "pd3b", "HitPan", "Backup");
        var (okL0, errL0) = InvokeSecure(secure, localGroup);
        if (!okL0) v.Add($"C-12 ⓓ' 준비: 기본 폴더를 못 만들었다: {errL0}");
        else
        {
            AddAllowRule(localGroup, new SecurityIdentifier(WellKnownSidType.LocalSid, null), FileSystemRights.Traverse);
            var (okL, _) = InvokeSecure(secure, localGroup);
            if (okL) v.Add("C-12 ⓓ' LOCAL 그룹 줄(폴더 탐색만)이 붙은 기본 폴더를 받아들였다 — 권한 줄 주체 허용 목록이 아니다");
        }

        // ⓔ 개별 관리자 사용자 줄(이 PC 의 기본 Administrator 계정 · RID 500) — 허용 (탐색기 「계속」 이 붙이는 줄과 같은 모양)
        var named = Path.Combine(root, "pd4", "HitPan", "Backup");
        var (okN0, errN0) = InvokeSecure(secure, named);
        var accountDomain = WindowsIdentity.GetCurrent().User?.AccountDomainSid;
        if (!okN0) v.Add($"C-12 ⓔ 준비: 기본 폴더를 못 만들었다: {errN0}");
        else if (accountDomain is null) v.Add("C-12 ⓔ 준비: 시험 계정의 계정 도메인 SID 가 없어 개별 사용자 SID 를 정하지 못했다 — 이 단언은 재지 못했다");
        else
        {
            var adminUser = new SecurityIdentifier(WellKnownSidType.AccountAdministratorSid, accountDomain);
            if (allowed.Contains(adminUser)) v.Add("C-12 ⓔ 준비: 시험 계정이 기본 Administrator 자신이라 대조가 되지 않는다 — 이 단언은 재지 못했다");
            else
            {
                AddAllowRule(named, adminUser, FileSystemRights.Modify);
                var (okN, errN) = InvokeSecure(secure, named);
                if (!okN) v.Add($"C-12 ⓔ 개별 사용자(관리자 계정) 권한 줄이 붙은 기본 폴더를 거부했다: {errN}");
            }
        }

        // ⓕ Backup 자체가 교차점 — 대상은 권한이 바른 폴더(대조로 먼저 통과 확인 · 교차점만 다르다)
        var target = Path.Combine(root, "jt", "Target");
        var junctionBackup = Path.Combine(root, "pd5", "HitPan", "Backup");
        var targetHitPan = Path.Combine(root, "jt2", "HitPan");
        var junctionHitPan = Path.Combine(root, "pd6", "HitPan");
        try
        {
            var (okT, errT) = InvokeSecure(secure, target);
            if (!okT) v.Add($"C-12 ⓕ 대조: 권한이 바른 대상 폴더를 거부했다: {errT}");
            var junctionOk = MakeJunction(junctionBackup, target, v, "ⓕ");
            if (junctionOk)
            {
                var (okJ, errJ) = InvokeSecure(secure, junctionBackup);
                if (okJ) v.Add("C-12 ⓕ Backup 이 교차점인데 받아들였다");
                else if (errJ is null || !errJ.Contains("연결", StringComparison.Ordinal)) v.Add($"C-12 ⓕ 거부 이유에 연결 문구가 없다: {errJ}");
            }

            // ⓖ 상위 HitPan 이 교차점 — 그 안 Backup 은 권한이 바른 폴더
            var (okT2, errT2) = InvokeSecure(secure, Path.Combine(targetHitPan, "Backup"));
            if (!okT2) v.Add($"C-12 ⓖ 대조: 권한이 바른 대상 폴더를 거부했다: {errT2}");
            if (MakeJunction(junctionHitPan, targetHitPan, v, "ⓖ"))
            {
                var (okP, errP) = InvokeSecure(secure, Path.Combine(junctionHitPan, "Backup"));
                if (okP) v.Add("C-12 ⓖ 상위 HitPan 이 교차점인데 받아들였다");
                else if (errP is null || !errP.Contains("연결", StringComparison.Ordinal)) v.Add($"C-12 ⓖ 거부 이유에 연결 문구가 없다: {errP}");
            }

            // ② 연 파일 핸들의 실제 위치 — 곧바로 연 파일은 기대 위치와 같다(대조) · 교차점 너머로 연 파일은 다르다
            var handleCheck = typeof(BackupService).GetMethod("IsHandleAtExpectedPathWindows", BindingFlags.NonPublic | BindingFlags.Static);
            if (handleCheck is null) v.Add("C-12 ②: 연 파일의 실제 위치를 확인하는 함수 IsHandleAtExpectedPathWindows 가 없다");
            else if (okT)
            {
                if (!HandleAtExpectedPath(handleCheck, Path.Combine(target, "direct.sql")))
                    v.Add("C-12 ② 대조: 곧바로 연 파일의 실제 위치를 기대 위치와 다르다고 했다");
                if (junctionOk && HandleAtExpectedPath(handleCheck, Path.Combine(junctionBackup, "via-junction.sql")))
                    v.Add("C-12 ② 교차점 너머로 연 파일의 실제 위치를 기대 위치와 같다고 했다");
            }
        }
        finally
        {
            // 교차점은 링크만 지운다 — 폴더 통째 삭제는 교차점에서 접근 거부로 멈춰 임시 폴더가 남았다(자체 실측).
            RemoveJunction(junctionBackup);
            RemoveJunction(junctionHitPan);
        }

        static (bool Ok, string? Error) InvokeSecure(MethodInfo m, string path)
        {
            try { m.Invoke(null, new object?[] { path }); return (true, null); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                return (false, $"{tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
            }
        }
    }

    /// <summary>C-12 시험 준비 — 폴더에 허용 권한 줄 하나를 붙인다(게이트 임시 폴더에만).</summary>
    [SupportedOSPlatform("windows")]
    private static void AddAllowRule(string dir, SecurityIdentifier sid, FileSystemRights rights)
    {
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(sid, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    /// <summary>
    /// C-12 시험 준비 — 관리자 권한 없이 만들 수 있는 디렉터리 교차점(<c>mklink /J</c>). 게이트 임시 폴더 안에서만 만든다.
    /// 못 만들면 위반으로 남긴다(조용히 건너뛰지 않는다).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool MakeJunction(string link, string target, List<string> v, string label)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) { v.Add($"C-12 {label} 준비: 교차점을 만들 cmd 를 띄우지 못했다 — 이 단언은 재지 못했다"); return false; }
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            p.Kill(entireProcessTree: true);
            v.Add($"C-12 {label} 준비: 교차점 만들기가 15초 안에 끝나지 않았다 — 이 단언은 재지 못했다");
            return false;
        }
        var info = new DirectoryInfo(link);
        if (p.ExitCode != 0 || !info.Exists || (info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            v.Add($"C-12 {label} 준비: 교차점을 만들지 못했다 (exit={p.ExitCode}) — 이 단언은 재지 못했다");
            return false;
        }
        return true;
    }

    /// <summary>
    /// C-12 시험 정리 — 교차점 링크만 지운다(대상 폴더는 건드리지 않는다).
    /// 폴더 통째 재귀 삭제는 교차점에서 접근 거부로 멈춰 부모 폴더가 남는다(자체 실측 — 게이트 6회 모두 임시 폴더 잔존).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void RemoveJunction(string link)
    {
        try
        {
            var info = new DirectoryInfo(link);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(link, recursive: false);
        }
        catch (IOException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 교차점 정리 실패 {link}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 교차점 정리 실패 {link}: {ex.Message}"); }
    }

    /// <summary>C-12 ② 시험 — 파일을 새로 열고 그 핸들로 실제 위치 확인 함수를 부른다.</summary>
    [SupportedOSPlatform("windows")]
    private static bool HandleAtExpectedPath(MethodInfo check, string file)
    {
        using var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var args = new object?[] { fs.SafeFileHandle, file, null };
        return (bool)check.Invoke(null, args)!;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-BK5 — PATH 의 mysqldump 를 고르지 않는다 (DB 없음)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-BK5 — PATH 에 가짜 <c>mysqldump</c> 만 두면 그것을 고르지 않는다(MariaDB 설치 폴더를 고르거나 이유와 함께 실패).
    /// PATH 에 가짜 <c>mysqldump</c>(앞) + <c>mariadb-dump</c>(뒤)를 두면 <c>mysqldump</c> 가 아닌 MariaDB 쪽을 고른다.
    /// <para>
    /// 재는 함수 = <b>덤프 경로가 실제로 쓰는 결정 함수</b>. 수정 후 <c>ResolveDumpBinary()</c>,
    /// 수정 전은 <c>RunMysqldumpAsync</c>(:334) 가 부르던 <c>ResolveMariadbBinary("mysqldump.exe", "mariadb-dump.exe")</c> 그대로.
    /// (복원 쪽 <c>ResolveMariadbBinary</c> 는 C-11 보류로 그대로 둔다 — 덤프 경로가 새 함수를 쓰는지는 G-BK1 의 실행파일 로그로 잰다.)
    /// </para>
    /// <para>무력화: PATH 를 먼저 · 후보 순서대로 찾게 되돌리면 빨간불(Windows).</para>
    /// </summary>
    [Fact]
    public void G_BK5_PATH_의_mysqldump_를_고르지_않는다()
    {
        var dumpResolver = typeof(BackupService).GetMethod("ResolveDumpBinary",
            BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
        var legacy = typeof(BackupService).GetMethod("ResolveMariadbBinary",
            BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string[]) }, null);
        Assert.True(dumpResolver is not null || legacy is not null,
            "G-BK5: 덤프 실행파일 결정 함수(ResolveDumpBinary / ResolveMariadbBinary)가 없다 — 이름이 바뀌었으면 이 게이트를 같이 본다.");
        object?[]? legacyArgs = dumpResolver is null ? new object?[] { new[] { "mysqldump.exe", "mariadb-dump.exe" } } : null;
        var target = dumpResolver ?? legacy!;

        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        var root = Path.Combine(Path.GetTempPath(), "hitpan_bkgate_g5_" + Guid.NewGuid().ToString("N")[..8]);
        var dirMysql = Path.Combine(root, "mysql84", "bin");
        var dirMaria = Path.Combine(root, "mariadb", "bin");
        Directory.CreateDirectory(dirMysql);
        Directory.CreateDirectory(dirMaria);
        var fakeMysqldump = Path.Combine(dirMysql, "mysqldump" + ext);
        var fakeMariadbDump = Path.Combine(dirMaria, "mariadb-dump" + ext);
        File.WriteAllText(fakeMysqldump, "fake");
        File.WriteAllText(fakeMariadbDump, "fake");

        var savedPath = Environment.GetEnvironmentVariable("PATH");
        var v = new List<string>();
        try
        {
            // ① PATH = 가짜 mysqldump 만
            Environment.SetEnvironmentVariable("PATH", dirMysql);
            var (got1, err1) = Invoke(target, legacyArgs);
            if (got1 is not null)
            {
                if (SamePath(got1, fakeMysqldump)) v.Add($"① PATH 의 가짜 mysqldump 를 골랐다 → {got1}");
                else if (!IsMariaDbChoice(got1)) v.Add($"① MariaDB 가 아닌 것을 골랐다 → {got1}");
            }
            else if (string.IsNullOrEmpty(err1)) v.Add("① 못 찾았는데 이유가 없다");

            // ② PATH = 가짜 mysqldump(앞) + 가짜 mariadb-dump(뒤)
            Environment.SetEnvironmentVariable("PATH", dirMysql + Path.PathSeparator + dirMaria);
            var (got2, err2) = Invoke(target, legacyArgs);
            if (got2 is null) v.Add($"② PATH 에 mariadb-dump 가 있는데 못 골랐다: {err2}");
            else if (SamePath(got2, fakeMysqldump)) v.Add($"② mariadb-dump 가 있는데 PATH 의 mysqldump 를 골랐다 → {got2}");
            else if (!IsMariaDbChoice(got2)) v.Add($"② MariaDB 가 아닌 것을 골랐다 → {got2}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            TryDeleteDir(root);
        }

        if (v.Count > 0) Assert.Fail($"G-BK5 위반 (잰 함수 {target.Name}):\n  · " + string.Join("\n  · ", v));

        static (string? Path, string? Error) Invoke(MethodInfo m, object?[]? args)
        {
            try { return ((string?)m.Invoke(null, args), null); }
            catch (TargetInvocationException tie) when (tie.InnerException is InvalidOperationException ioe)
            {
                return (null, ioe.Message);
            }
        }

        static bool IsMariaDbChoice(string path) =>
            Path.GetFileName(path).StartsWith("mariadb", StringComparison.OrdinalIgnoreCase)
            || path.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  보조
    // ════════════════════════════════════════════════════════════════════════

    private sealed record Outcome(Task<RunBackupResponse> Task, bool Finished, TimeSpan Elapsed, RunBackupResponse? Response, Exception? Thrown);

    private static Task<Outcome> RunBoundedAsync(Func<Task<RunBackupResponse>> start, TimeSpan deadline) =>
        AwaitBoundedAsync(Task.Run(start), deadline);

    private static async Task<Outcome> AwaitBoundedAsync(Task<RunBackupResponse> task, TimeSpan deadline)
    {
        var sw = Stopwatch.StartNew();
        var winner = await Task.WhenAny(task, Task.Delay(deadline));
        if (winner != task) return new Outcome(task, false, sw.Elapsed, null, null);
        try { return new Outcome(task, true, sw.Elapsed, await task, null); }
        catch (Exception ex) { return new Outcome(task, true, sw.Elapsed, null, ex); }
    }

    /// <summary>제한시간을 넘긴 작업이 덤프 종료 뒤 스스로 끝나기를 짧게 기다린다(기록 판독 전 정리).</summary>
    private static async Task SettleAsync(Task task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
        if (winner != task)
            Console.Error.WriteLine("[BackupCredentialGate] 덤프를 끝낸 뒤에도 백업 작업이 15초 안에 안 끝났다.");
        else if (task.IsFaulted)
            Console.Error.WriteLine($"[BackupCredentialGate] 정리 중 백업 작업 예외: {task.Exception?.GetBaseException().Message}");
    }

    private static void AssertNoViolations(string gate, List<string> v, Outcome o)
    {
        if (v.Count == 0) return;
        Assert.Fail($"{gate} 위반 (경과 {o.Elapsed.TotalSeconds:F1}s):\n  · " + string.Join("\n  · ", v));
    }

    /// <summary>
    /// 설정 원본이 환경변수가 아니라 db.conf 에서 읽히면 게이트가 다른 값을 잰다 — 시작 때 막는다.
    /// (TenantConfigReader 후보: HITPAN_DB_CONF → 실행파일 폴더 · .. · ../..)
    /// </summary>
    private static void AssertNoDbConfShadow()
    {
        Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HITPAN_DB_CONF")),
            "HITPAN_DB_CONF 가 설정돼 있다 — 게이트가 환경변수가 아닌 파일 설정을 잰다.");
        var dirs = new[] { Path.GetDirectoryName(Environment.ProcessPath), AppContext.BaseDirectory }
            .Where(d => !string.IsNullOrEmpty(d)).Select(d => d!);
        foreach (var d in dirs)
        foreach (var rel in new[] { "db.conf", Path.Combine("..", "db.conf"), Path.Combine("..", "..", "db.conf") })
        {
            var full = Path.GetFullPath(Path.Combine(d, rel));
            Assert.False(File.Exists(full), $"{full} 가 있다 — 게이트가 환경변수가 아닌 db.conf 를 잰다.");
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var sep = Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path).TrimEnd(sep) + sep;
        var r = Path.GetFullPath(root).TrimEnd(sep) + sep;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 임시 폴더 정리 실패 {dir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 임시 폴더 정리 실패 {dir}: {ex.Message}"); }
    }

    // ── DB 접속 (DbGateEnvironment 관례) ─────────────────────────────────────

    private static string TestDb => Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";
    private static string DbHost => Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
    private static string? DbPort => Environment.GetEnvironmentVariable("HITPAN_DB_PORT");
    private static string DbUser => Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
    private static string DbPass => Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 (작14 W1)
        // 헌법 #39 — 로컬은 포트를 명시한 격리 인스턴스에서만. 기본값(운영 3306)으로 붙지 않는다.
        if (string.IsNullOrWhiteSpace(DbPort)) return false;
        try { using var c = OpenAdmin(); return true; }
        catch (MySqlException) { return false; }
    }

    private static void Skipped(string gate) => DbGateEnvironment.SkipOrFail(gate);

    /// <summary>게이트 자신이 판독·정리에 쓰는 연결(서비스 연결과 별개).</summary>
    private static MySqlConnection OpenAdmin()
    {
        var c = new MySqlConnection(
            $"Server={DbHost};Port={DbPort ?? "3306"};Database={TestDb};User={DbUser};Password={DbPass};"
          + "AllowUserVariables=true;GuidFormat=None;Connection Timeout=5;");
        c.Open();
        return c;
    }

    /// <summary>서비스에 주는 연결 — InfrastructureExtensions.cs:55 와 같은 조립 · 연 뒤에 넘긴다.</summary>
    private static MySqlConnection OpenLikeProduction()
    {
        var c = new MySqlConnection(
            $"Server={DbHost};Port={DbPort ?? "3306"};Database={TestDb};User={DbUser};Password={DbPass};"
          + "DefaultCommandTimeout=90;AllowLoadLocalInfile=true;AllowUserVariables=true;GuidFormat=None;");
        c.Open();
        return c;
    }

    // ── 로그 수집 (실행파일 로그 · 비밀번호 유출 판독) ─────────────────────────

    private sealed class CapturingLogger : ILogger<BackupService>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue($"{logLevel}: {formatter(state, exception)} {exception?.Message}");
    }

    // ── 시험 한 건의 자리 ──────────────────────────────────────────────────

    private sealed class GateFixture : IDisposable
    {
        private readonly Dictionary<string, string?> _savedEnv = new();
        private readonly HashSet<int> _preexisting;
        private readonly MySqlConnection _serviceConn;
        private readonly MySqlConnection _admin;

        public string TenantId { get; }
        public string Folder { get; }
        public BackupService Service { get; }
        public CapturingLogger Logger { get; } = new();

        private GateFixture(string? passwordOverride)
        {
            _preexisting = ProcessTree.AllPids();
            TenantId = "GATE-BK-" + Guid.NewGuid().ToString("N")[..8];
            Folder = Path.Combine(Path.GetTempPath(), "hitpan_bkgate_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Folder);

            // 설정 원본(TenantConfigReader)에 줄 값 — 운영의 db.conf 키와 같은 이름.
            foreach (var k in ConfigKeys) _savedEnv[k] = Environment.GetEnvironmentVariable(k);
            Environment.SetEnvironmentVariable("DB_HOST", DbHost);
            Environment.SetEnvironmentVariable("DB_PORT", DbPort ?? "3306");
            Environment.SetEnvironmentVariable("DB_NAME", TestDb);
            Environment.SetEnvironmentVariable("DB_USER", DbUser);
            Environment.SetEnvironmentVariable("DB_PASSWORD", passwordOverride ?? DbPass);

            _admin = OpenAdmin();
            _admin.Execute(
                "INSERT INTO backup_settings (tenant_id, primary_path, mirror_path, schedule_mode, retention_count) "
              + "VALUES (@T, @P, NULL, 'manual', 30)", new { T = TenantId, P = Folder });

            _serviceConn = OpenLikeProduction();
            Service = new BackupService(_serviceConn, Logger);
        }

        public static GateFixture Create(string? passwordOverride) => new(passwordOverride);

        /// <summary>G-BK7 — 이 게이트 행의 보관 개수.</summary>
        public void SetRetention(int keep) =>
            _admin.Execute("UPDATE backup_settings SET retention_count = @K WHERE tenant_id = @T", new { K = keep, T = TenantId });

        /// <summary>G-BK7 — 설정 원본(환경변수 DB_PASSWORD)만 바꾼다. null = 게이트 비번으로 되돌림. 값은 출력하지 않는다(Dispose 가 원래 값 복원).</summary>
        public void SetConfigPassword(string? password) =>
            Environment.SetEnvironmentVariable("DB_PASSWORD", password ?? DbPass);

        /// <summary>복원 안전 확인(회사명)을 통과할 이 게이트 전용 회사 행.</summary>
        public string CreateCompany()
        {
            var suffix = TenantId[^8..];
            var name = "게이트복원" + suffix;
            _admin.Execute(
                "INSERT INTO local_company (tenant_id, tenant_code, company_name) VALUES (@T, @C, @N)",
                new { T = TenantId, C = "GBK" + suffix, N = name });
            return name;
        }

        public long CountRestoreHistory() =>
            _admin.ExecuteScalar<long>("SELECT COUNT(*) FROM restore_history WHERE tenant_id = @T", new { T = TenantId });

        public List<string> BackupFiles() =>
            Directory.Exists(Folder) ? Directory.GetFiles(Folder, "hitpan_*.sql").ToList() : new List<string>();

        public (string? Status, string? Error) LatestHistory() =>
            _admin.QueryFirstOrDefault<(string?, string?)>(
                "SELECT status, error_message FROM backup_history WHERE tenant_id = @T ORDER BY started_at DESC LIMIT 1",
                new { T = TenantId });

        /// <summary>비밀번호 값이 로그·반환 이유·이력에 섞였나 (값 자체는 출력하지 않는다).</summary>
        public void CheckNoSecretLeak(List<string> v, RunBackupResponse? r, string? historyError)
        {
            var secret = DbPass;
            if (secret.Length < 4) return;
            if (Logger.Lines.Any(l => l.Contains(secret, StringComparison.Ordinal))) v.Add("로그에 비밀번호 값이 섞였다(값 출력 안 함)");
            if (r?.Error?.Contains(secret, StringComparison.Ordinal) == true) v.Add("반환 이유에 비밀번호 값이 섞였다(값 출력 안 함)");
            if (historyError?.Contains(secret, StringComparison.Ordinal) == true) v.Add("이력 error_message 에 비밀번호 값이 섞였다(값 출력 안 함)");
        }

        /// <summary>이 시험 프로세스의 자손 중 시험 시작 뒤 생긴 덤프.</summary>
        public List<(int Pid, string Name)> SurvivingDumps() => SurvivingProcesses(DumpNames);

        /// <summary>이 시험 프로세스의 자손 중 시험 시작 뒤 생긴, 이름이 맞는 프로세스.</summary>
        public List<(int Pid, string Name)> SurvivingProcesses(string[] names) =>
            ProcessTree.Descendants(Environment.ProcessId)
                .Where(p => !_preexisting.Contains(p.Pid) && names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

        /// <summary>시험이 띄운 덤프·가져넣기 클라이언트만 정리한다(자손 · 시험 뒤 생성 · 이름 일치).</summary>
        public void KillNewDumps()
        {
            foreach (var (pid, name) in SurvivingProcesses(ClientNames))
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (!string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                    Console.Error.WriteLine($"[BackupCredentialGate] 시험이 띄운 덤프 정리: {name}#{pid}");
                }
                catch (ArgumentException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 이미 끝난 덤프 {pid}: {ex.Message}"); }
                catch (InvalidOperationException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 이미 끝난 덤프 {pid}: {ex.Message}"); }
                catch (System.ComponentModel.Win32Exception ex) { Console.Error.WriteLine($"[BackupCredentialGate] 덤프 종료 실패 {pid}: {ex.Message}"); }
            }
        }

        /// <summary>게이트 전용 표 — 이름이 앞쪽(a_…)이라 덤프가 곧 이 표에 닿는다.</summary>
        public string CreateGateTable()
        {
            var name = "a_gate_bk_" + Guid.NewGuid().ToString("N")[..8];
            _admin.Execute($"CREATE TABLE `{name}` (id INT NOT NULL PRIMARY KEY) ENGINE=InnoDB");
            _admin.Execute($"INSERT INTO `{name}` (id) VALUES (1)");
            return name;
        }

        public bool DumpWaitingOnLock(string table) =>
            _admin.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM information_schema.PROCESSLIST "
              + "WHERE ID <> CONNECTION_ID() AND INSTR(IFNULL(INFO, ''), @N) > 0 AND STATE LIKE '%metadata lock%'",
                new { N = table }) > 0;

        /// <summary>잠금에 걸린 채 남은 서버 쪽 스레드(이 표를 읽던 것만)를 끊고 잠금 해제 · 표 삭제.</summary>
        public void ReleaseGateTable(MySqlConnection lockConn, string table)
        {
            var ids = _admin.Query<long>(
                "SELECT ID FROM information_schema.PROCESSLIST WHERE ID <> CONNECTION_ID() AND ID <> @L AND INSTR(IFNULL(INFO, ''), @N) > 0",
                new { N = table, L = lockConn.ServerThread }).ToList();
            foreach (var id in ids)
            {
                try { _admin.Execute($"KILL {id}"); }
                catch (MySqlException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 서버 스레드 {id} 정리 실패: {ex.Message}"); }
            }
            lockConn.Execute("UNLOCK TABLES");
            _admin.Execute("SET SESSION lock_wait_timeout = 10");
            _admin.Execute($"DROP TABLE IF EXISTS `{table}`");
        }

        public void Dispose()
        {
            KillNewDumps();
            foreach (var (k, val) in _savedEnv) Environment.SetEnvironmentVariable(k, val);
            try
            {
                _admin.Execute("DELETE FROM backup_history WHERE tenant_id = @T", new { T = TenantId });
                _admin.Execute("DELETE FROM backup_settings WHERE tenant_id = @T", new { T = TenantId });
                _admin.Execute("DELETE FROM restore_history WHERE tenant_id = @T", new { T = TenantId });
                _admin.Execute("DELETE FROM local_company WHERE tenant_id = @T", new { T = TenantId });
            }
            catch (MySqlException ex) { Console.Error.WriteLine($"[BackupCredentialGate] 시험 행 정리 실패 {TenantId}: {ex.Message}"); }
            _serviceConn.Dispose();
            _admin.Dispose();
            TryDeleteDir(Folder);
        }
    }

    // ── 프로세스 트리 (부모 PID 로 자손만 고른다 — 남의 덤프는 건드리지 않는다) ─────────

    private static class ProcessTree
    {
        public static HashSet<int> AllPids() => Snapshot().Select(e => e.Pid).ToHashSet();

        public static List<(int Pid, string Name)> Descendants(int rootPid)
        {
            var all = Snapshot();
            var result = new List<(int, string)>();
            var frontier = new Queue<int>();
            frontier.Enqueue(rootPid);
            var seen = new HashSet<int> { rootPid };
            while (frontier.Count > 0)
            {
                var parent = frontier.Dequeue();
                foreach (var e in all.Where(e => e.ParentPid == parent && e.Pid != parent))
                {
                    if (!seen.Add(e.Pid)) continue;
                    result.Add((e.Pid, e.Name));
                    frontier.Enqueue(e.Pid);
                }
            }
            return result;
        }

        private static List<(int Pid, int ParentPid, string Name)> Snapshot() =>
            OperatingSystem.IsWindows() ? SnapshotWindows() : SnapshotProc();

        private static List<(int, int, string)> SnapshotProc()
        {
            var list = new List<(int, int, string)>();
            var vanished = 0;
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
                string stat;
                try { stat = File.ReadAllText(Path.Combine(dir, "stat")); }
                catch (IOException) { vanished++; continue; }                 // 읽는 사이 끝난 프로세스
                catch (UnauthorizedAccessException) { vanished++; continue; }
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                if (open < 0 || close < open || close + 2 >= stat.Length) continue;
                var fields = stat[(close + 2)..].Split(' ');
                if (fields.Length < 2 || !int.TryParse(fields[1], out var ppid)) continue;
                list.Add((pid, ppid, stat[(open + 1)..close]));
            }
            _ = vanished;
            return list;
        }

        private static List<(int, int, string)> SnapshotWindows()
        {
            var list = new List<(int, int, string)>();
            var snap = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (!Process32FirstW(snap, ref e)) return list;
                do
                {
                    list.Add(((int)e.th32ProcessID, (int)e.th32ParentProcessID, Path.GetFileNameWithoutExtension(e.szExeFile)));
                } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return list;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
