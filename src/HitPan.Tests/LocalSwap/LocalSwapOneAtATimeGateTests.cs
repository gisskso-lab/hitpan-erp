using System.Globalization;
using System.Text.RegularExpressions;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 I-API 4 — 병렬이슈 03 「한 번에 하나」 게이트(G-U5b 동시 · G-U5c 묵은 요청 · <c>/F</c> 없음 · 확인 번호 10분 ·
/// 쿨다운 10분 · 워치독 잠금 15분).
/// </summary>
/// <remarks>
/// 🟢 실제 <see cref="LocalSwapLauncher"/>·<see cref="LocalRollbackService"/>·<see cref="ManualUpdateService"/>·<c>local-swap.ps1</c> 를 부른다(글자 검사 아님).
/// 예약 작업은 메모리·대역 폴더 · 설치 폴더는 임시 폴더 · 실제 schtasks·서비스·피드 0.
/// 🔴 각 게이트는 <b>음성대조군</b>을 안에 둔다 — 규칙을 뺀 장면에서 같은 판정이 FAIL(또는 반대 답)해야 한다.
/// </remarks>
public sealed class LocalSwapOneAtATimeGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-l1-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapOneAtATime] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    private static SwapLaunchInput Input() => new(
        SwapModes.Rollback, "1.3.48", "1.3.47",
        new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = "x" }, "gate-user", SwapEntries.Menu, null, null);

    private static LocalSwapLauncher Launcher(FakeSwapEnvironment env, ISchtasksRunner sc) =>
        new(env, sc, new HookedFolderGuard(), NullLogger<LocalSwapLauncher>.Instance);

    /// <summary>CheckBusy 의 마지막 조회 직후(= 바쁨 판정은 지났고 잠금은 아직) 끼어들 자리를 주는 대역.</summary>
    private sealed class RacingSchtasks : ISchtasksRunner
    {
        public FakeSchtasks Inner { get; } = new();
        public Action? AfterLastBusyQuery { get; set; }

        public int Run(string arguments)
        {
            var code = Inner.Run(arguments);
            if (arguments.StartsWith("/Query", StringComparison.Ordinal) && arguments.Contains("HitPanWatchdogSelfReplaceRecover", StringComparison.Ordinal))
            {
                var hook = AfterLastBusyQuery;
                AfterLastBusyQuery = null; // 한 번만
                hook?.Invoke();
            }
            return code;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-U5b 동시 요청 — 두 런처(= 두 프로세스 · 프로세스 안 lock 무력)가 바쁨 판정을 함께 지나도 시작은 하나
    // ══════════════════════════════════════════════════════════════

    private sealed record RaceOutcome(SwapLaunchResult Outer, SwapLaunchResult? Inner, int TasksRegistered, SwapRequest? Request);

    private RaceOutcome Race(bool removeLockAfterWinner)
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, removeLockAfterWinner ? "race-ctl" : "race"));
        var sc = new RacingSchtasks();
        var outer = Launcher(env, sc);
        var inner = Launcher(env, sc);
        SwapLaunchResult? innerResult = null;
        sc.AfterLastBusyQuery = () =>
        {
            innerResult = inner.Launch(Input());
            if (removeLockAfterWinner)
                File.Delete(Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName, LocalSwapLauncher.LockFileName));
        };
        var outerResult = outer.Launch(Input());
        var registered = sc.Inner.Tasks.Count(t => t == LocalSwapLauncher.TaskName);
        return new RaceOutcome(outerResult, innerResult, registered, outer.ReadLast());
    }

    private static List<string> JudgeRace(RaceOutcome r)
    {
        var problems = new List<string>();
        var started = new[] { r.Outer, r.Inner }.Where(x => x is { Started: true }).ToList();
        if (started.Count != 1) problems.Add($"시작 {started.Count}건(기대 1)");
        if (r.TasksRegistered != 1) problems.Add($"등록된 작업 {r.TasksRegistered}(기대 1)");
        var winner = started.FirstOrDefault()?.Ticket;
        if (r.Request is null || r.Request.Ticket != winner || r.Request.State != SwapStates.Requested)
            problems.Add($"요청서가 이긴 요청의 것이 아니다(요청서 번호={r.Request?.Ticket} 상태={r.Request?.State} · 이긴 번호={winner}) — 일꾼이 번호 불일치로 멈추고 작업이 남는다");
        return problems;
    }

    [Fact(DisplayName = "I-API4 G-U5b 🔴 동시 두 요청 — 바쁨 판정을 함께 지나도 시작 1 · 작업 1 · 요청서는 이긴 쪽 (대조군: 잠금 뺀 장면은 요청서를 덮는다)")]
    public void Concurrent_requests_start_exactly_one()
    {
        var ok = Race(removeLockAfterWinner: false);
        Assert.NotNull(ok.Inner);
        Assert.True(ok.Inner!.Started, $"먼저 들어간 요청이 시작 못 했다: {ok.Inner.Reason}");
        Assert.Equal(SwapReasons.SwapInProgress, ok.Outer.Reason);
        var problems = JudgeRace(ok);
        Assert.True(problems.Count == 0, string.Join(" / ", problems));

        // 🔴 음성대조군 — 배타 잠금(swap.lock CreateNew)이 없는 장면: 늦은 요청이 요청서를 덮고 등록에서야 막힌다 ⇒ 판정 FAIL.
        var ctl = Race(removeLockAfterWinner: true);
        Assert.NotEmpty(JudgeRace(ctl));
    }

    // ══════════════════════════════════════════════════════════════
    // /F 없음 — 같은 이름 작업이 있으면 덮지 않는다
    // ══════════════════════════════════════════════════════════════

    private static readonly Regex ForceFlag = new(@"(^|\s)/F(\s|$)", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "I-API4 🔴 schtasks /Create 에 /F 0 · 같은 이름 작업이 이미 있으면 등록 0 (대조군: /F 를 붙인 글자는 잡힌다)")]
    public void Create_has_no_force_and_existing_task_blocks()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "noforce"));
        var sc = new FakeSchtasks();
        var r = Launcher(env, sc).Launch(Input());
        Assert.True(r.Started, r.Reason);
        var create = sc.LastCreate();
        Assert.NotNull(create);
        Assert.Contains("/TN \"" + LocalSwapLauncher.TaskName + "\"", create!, StringComparison.Ordinal);
        Assert.False(ForceFlag.IsMatch(create), "작업 등록에 /F 가 있다 — 같은 이름 작업을 덮는다(병렬이슈 03 ②): " + create);
        Assert.True(ForceFlag.IsMatch(create.Replace("/Create ", "/Create /F ", StringComparison.Ordinal)), "대조군 — /F 판정식이 /F 를 못 잡는다");

        // 이미 등록된 같은 이름 작업(다른 요청)이 있으면 새로 걸지 않는다
        // 🔄 20260930작1 봉합 L(설계 §14-1 06ⓑ) — 「작업만 있으면 바쁨」은 봉합이 좁혔다: 작업이 있어도 열린 요청·swap.lock 이
        //    30분 안일 때만 바쁨(묵은 작업은 지운다 — G-R1 · LocalSwapSealGateTests). 그래서 이 장면에 「살아 있는 다른 요청」을 둔다.
        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "exists"));
        WriteLast(env2, SwapModes.Update, SwapStates.Running, env2.UtcNow.AddMinutes(-1));
        var sc2 = new FakeSchtasks();
        sc2.Tasks.Add(LocalSwapLauncher.TaskName);
        var r2 = Launcher(env2, sc2).Launch(Input());
        Assert.False(r2.Started);
        Assert.Equal(SwapReasons.SwapInProgress, r2.Reason);
        Assert.Equal(0, sc2.CountStartingWith("/Create"));

        // 🆕 봉합 L — 바쁨 판정을 지난 뒤(= 묵은 작업을 지운 뒤) 등록 직전에 남이 같은 이름 작업을 만들면 /F 가 없어 덮지 않는다
        var env3 = FakeSwapEnvironment.Under(Path.Combine(_root, "exists-race"));
        var sc3 = new FakeSchtasks();
        sc3.OnCreate = () => sc3.Tasks.Add(LocalSwapLauncher.TaskName);
        var r3 = Launcher(env3, sc3).Launch(Input());
        Assert.False(r3.Started);
        Assert.Equal(SwapReasons.TaskRegisterFailed, r3.Reason);
        Assert.Equal(1, sc3.CountStartingWith("/Create"));
        Assert.Equal(0, sc3.CountStartingWith("/Run"));
    }

    // ══════════════════════════════════════════════════════════════
    // 확인 번호 10분 (되돌리기)
    // ══════════════════════════════════════════════════════════════

    private static void Prev(FakeSwapEnvironment env)
    {
        var prev = Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName, "prev");
        foreach (var p in new[] { "api", "web", "watchdog" })
        {
            Directory.CreateDirectory(Path.Combine(prev, p));
            File.WriteAllText(Path.Combine(prev, p, "x.bin"), p);
        }
        File.WriteAllText(Path.Combine(prev, "version.txt"), "1.3.47");
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), "1.3.48");
        File.WriteAllText(Path.Combine(prev, "sha256.txt"), "");
    }

    [Fact(DisplayName = "I-API4 🔴 되돌리기 확인 번호 — 11분 지난 번호는 거부 · 작업 등록 0 (대조군: 9분은 시작)")]
    public void Rollback_ticket_expires_after_ten_minutes()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "ticket"));
        Prev(env);
        var t0 = new DateTime(2026, 9, 30, 1, 0, 0, DateTimeKind.Utc);
        env.UtcNow = t0;
        var sc = new FakeSchtasks();
        var svc = new LocalRollbackService(Launcher(env, sc), env, NullLogger<LocalRollbackService>.Instance);

        var status = svc.GetStatus("u1");
        Assert.True(status.CanRollback, status.Reason);
        Assert.Equal(10, status.TicketMinutes);
        env.UtcNow = t0.AddMinutes(11);
        var late = svc.Start("u1", status.Ticket);
        Assert.False(late.Started);
        Assert.Equal(SwapReasons.TicketInvalid, late.Reason);
        Assert.Equal(0, sc.CountStartingWith("/Create"));

        // 대조군 — 같은 서비스·같은 재료에서 9분 지난 번호는 시작한다(거부가 시간 때문임을 확인)
        env.UtcNow = t0.AddMinutes(20);
        var fresh = svc.GetStatus("u1");
        env.UtcNow = t0.AddMinutes(29);
        var ok = svc.Start("u1", fresh.Ticket);
        Assert.True(ok.Started, ok.Reason);
        Assert.Equal(1, sc.CountStartingWith("/Create"));
    }

    // ══════════════════════════════════════════════════════════════
    // 쿨다운 10분(두 모드 합쳐) · 워치독 UpdateLockFile 15분
    // ══════════════════════════════════════════════════════════════

    private static void WriteLast(FakeSwapEnvironment env, string mode, string state, DateTime updatedUtc)
    {
        var work = Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName);
        Directory.CreateDirectory(work);
        var r = new SwapRequest
        {
            Ticket = LocalSwapLauncher.NewTicket(), Mode = mode, State = state, From = "1.3.47", To = "1.3.48",
            RequestedAt = updatedUtc.AddMinutes(-3), UpdatedAt = updatedUtc, AppRoot = env.AppRoot!, Slot = 1,
        };
        File.WriteAllText(Path.Combine(work, LocalSwapLauncher.RequestFileName), r.ToJson());
    }

    [Theory(DisplayName = "I-API4 🔴 쿨다운 — 끝난 교체(두 모드) 뒤 10분 안 = cooldown · 11분 = 통과 (거부로 끝난 요청은 쿨다운 없음)")]
    [InlineData(SwapModes.Update)]
    [InlineData(SwapModes.Rollback)]
    public void Cooldown_ten_minutes_across_modes(string lastMode)
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "cool-" + lastMode));
        var now = new DateTime(2026, 9, 30, 2, 0, 0, DateTimeKind.Utc);
        env.UtcNow = now;
        var launcher = Launcher(env, new FakeSchtasks());

        foreach (var state in new[] { SwapStates.Success, SwapStates.Reverted, SwapStates.Broken })
        {
            WriteLast(env, lastMode, state, now.AddMinutes(-9));
            Assert.Equal(SwapReasons.Cooldown, launcher.CheckBusy());
            WriteLast(env, lastMode, state, now.AddMinutes(-11));
            Assert.Null(launcher.CheckBusy());
        }
        // 대조군 — 거부(refused)는 교체가 안 일어났으니 쿨다운을 걸지 않는다
        WriteLast(env, lastMode, SwapStates.Refused, now.AddMinutes(-1));
        Assert.Null(launcher.CheckBusy());
    }

    [Fact(DisplayName = "I-API4 🔴 워치독 update.lock — 14분 = update_in_progress · 16분 = 묵은 잠금(통과) · 미래 시각 = 무효")]
    public void Watchdog_lock_ttl_fifteen_minutes()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "wdlock"));
        var now = new DateTime(2026, 9, 30, 3, 0, 0, DateTimeKind.Utc);
        env.UtcNow = now;
        var launcher = Launcher(env, new FakeSchtasks());
        var lockPath = Path.Combine(env.AppRoot!, "update.lock");

        File.WriteAllText(lockPath, now.AddMinutes(-14).ToString("o", CultureInfo.InvariantCulture) + "|1.3.49");
        Assert.Equal(SwapReasons.UpdateInProgress, launcher.CheckBusy());
        File.WriteAllText(lockPath, now.AddMinutes(-16).ToString("o", CultureInfo.InvariantCulture) + "|1.3.49");
        Assert.Null(launcher.CheckBusy());
        File.WriteAllText(lockPath, now.AddMinutes(5).ToString("o", CultureInfo.InvariantCulture) + "|1.3.49");
        Assert.Null(launcher.CheckBusy());
        Assert.Equal(15, LocalSwapLauncher.WatchdogLockTtl.TotalMinutes);
    }

    // ══════════════════════════════════════════════════════════════
    // G-U5c 묵은 요청 — 일꾼 S0 가 요청서 발급 10분 넘으면 멈추기 전에 거부
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "I-API4 G-U5c 🔴 묵은 요청(11분) → 일꾼 S0 refused · 아무것도 안 멈춤 · 작업 스스로 삭제 (대조군: 방금 요청은 S0 를 지난다)")]
    public void Worker_refuses_stale_request_before_stopping_anything()
    {
        if (!OperatingSystem.IsWindows()) return; // 일꾼은 윈도 PowerShell 5.1 전용 — CI build 잡(windows-latest)에서 돈다

        using (var stale = LocalSwapWorkerRig.Rollback(DateTime.UtcNow.AddMinutes(-11)))
        {
            stale.Run();
            var f = stale.Final();
            Assert.True(f.State == SwapStates.Refused && f.Reason == SwapReasons.RequestInvalid && f.Step == "S0",
                $"묵은 요청이 S0 에서 안 멈췄다: {f.State}/{f.Reason}/{f.Step} · {stale.Log()}");
            Assert.DoesNotContain("/DISABLE", stale.Calls(), StringComparison.Ordinal);
            Assert.Empty(stale.LeftoverTasks());
        }

        // 대조군 — 방금 발급한 요청은 S0 를 지난다(S1 실패 주입으로 멈추기 전에 끝낸다)
        using var fresh = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        fresh.Run(failAt: "S1");
        var g = fresh.Final();
        Assert.True(g.Step == "S1" && g.Reason == SwapReasons.MaterialInvalid, $"대조군이 S0 를 못 지났다: {g.State}/{g.Reason}/{g.Step} · {fresh.Log()}");
    }

    // ══════════════════════════════════════════════════════════════
    // 수동 업데이트 — 진행 중 두 번째 [예] 는 받기·백업 전에 거부
    // ══════════════════════════════════════════════════════════════

    private sealed class BlockingFeed : IUpdateFeed
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public async Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Gate.Task.ConfigureAwait(false);
            return new FeedCheckResult(FeedCheckStatus.NotNewer, null, null);
        }
    }

    private sealed class NoFetch : IPackageFetcher
    {
        public int Downloads;
        public bool HasEnoughSpace(long packageSizeBytes, string targetDir) => true;
        public Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            Interlocked.Increment(ref Downloads);
            throw new InvalidOperationException("이 게이트에서는 받기가 일어나면 안 된다");
        }
        public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class NoLock : IAutoUpdateLockProbe
    {
        public bool IsAutoUpdateInProgress() => false;
    }

    private sealed class CountingBackup : IBackupService
    {
        public int Runs;
        public Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default)
        {
            Interlocked.Increment(ref Runs);
            return Task.FromResult(new RunBackupResponse { Success = true });
        }
        public Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact(DisplayName = "I-API4 G-U5b 🔴 수동 업데이트 진행 중 두 번째 [예] → swap_in_progress · 피드·받기·백업 0 (대조군: 끝난 뒤엔 받아들인다)")]
    public async Task Manual_update_second_start_is_refused_while_running()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "mu"));
        var feed = new BlockingFeed();
        var fetch = new NoFetch();
        var backup = new CountingBackup();
        var sc = new ServiceCollection();
        sc.AddScoped<IBackupService>(_ => backup);
        var scopes = sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var folders = new ManualFolders(env.AppRoot!);
        var usage = new ManualUsageLog(folders, NullLogger<ManualUsageLog>.Instance, (_, _, _) => { });
        var svc = new ManualUpdateService(feed, fetch, new NoLock(), scopes, folders, usage,
            Launcher(env, new FakeSchtasks()), new ManualUpdateEnvironment(() => true, () => "1.3.48"),
            NullLogger<ManualUpdateService>.Instance);

        var first = svc.Start("T-1", "u1", "menu");
        Assert.True(first.Accepted, first.Reason);
        var second = svc.Start("T-1", "u1", "menu");
        Assert.False(second.Accepted);
        Assert.Equal(ManualUpdateReasons.SwapInProgress, second.Reason);

        feed.Gate.SetResult();
        await svc.LastRun;
        Assert.Equal(1, feed.Calls);
        Assert.Equal(0, fetch.Downloads);
        Assert.Equal(0, backup.Runs);

        // 대조군 — 앞 작업이 끝나면 다시 받아들인다(거부가 「진행 중」 때문임을 확인)
        var third = svc.Start("T-1", "u1", "menu");
        Assert.True(third.Accepted, third.Reason);
        await svc.LastRun;
    }
}
