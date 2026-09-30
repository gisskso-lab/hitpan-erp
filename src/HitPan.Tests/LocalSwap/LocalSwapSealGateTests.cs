using System.IO.Compression;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 봉합 차수 갈래 L — 설계 §14-3 게이트 G-P3(재료 ② 인접) · G-R1(남은 작업) + 런처 새 멤버(07·F-4 런처 쪽).
/// </summary>
/// <remarks>
/// 🟢 실제 <see cref="RollbackMaterialFinder"/>·<see cref="LocalSwapLauncher"/>·<see cref="LocalRollbackService"/> 를 부른다(글자 검사 아님).
/// 설치 폴더 = 임시 폴더 · 예약 작업 = 메모리 대역(<see cref="FakeSchtasks"/>) · 실제 schtasks·서비스·<c>C:\Program Files\HitPan</c> 0.
/// 🔴 대조군(봉합 뺀 사본에서 FAIL)은 개발명세서 <c>docs/개발/erp/20260930작1_봉합_개발명세서_L.md</c> 에 기록.
/// </remarks>
public sealed class LocalSwapSealGateTests : IDisposable
{
    private const string N = "1.3.47";
    private const string N1 = "1.3.48";
    private const string N2 = "1.3.49";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-seal-l-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapSeal] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    private static LocalSwapLauncher Launcher(FakeSwapEnvironment env, ISchtasksRunner sc) =>
        new(env, sc, new HookedFolderGuard(), NullLogger<LocalSwapLauncher>.Instance);

    private static string Work(FakeSwapEnvironment env) => Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName);

    private static void Zip(FakeSwapEnvironment env, string version)
    {
        var path = Path.Combine(env.WatchdogStagingDir, $"hitpan-{version}.zip");
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        var e = z.CreateEntry("api/x.txt");
        using var w = new StreamWriter(e.Open());
        w.Write(version);
    }

    private static void Prev(FakeSwapEnvironment env, string version, string replacedBy)
    {
        var prev = Path.Combine(Work(env), "prev");
        foreach (var p in new[] { "api", "web", "watchdog" })
        {
            Directory.CreateDirectory(Path.Combine(prev, p));
            File.WriteAllText(Path.Combine(prev, p, "x.bin"), p);
        }
        File.WriteAllText(Path.Combine(prev, "version.txt"), version);
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), replacedBy);
        File.WriteAllText(Path.Combine(prev, "sha256.txt"), "");
    }

    private static void History(FakeSwapEnvironment env, params string[] versions)
    {
        Directory.CreateDirectory(Work(env));
        var t = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var lines = versions.Select((v, i) => v + "|" + t.AddDays(i).ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllLines(Path.Combine(Work(env), LocalSwapLauncher.VersionsSeenFileName), lines);
    }

    // ══════════════════════════════════════════════════════════════
    // G-P3 재료 ② 인접 — 설계 §14-3 표 시드 4개
    // ══════════════════════════════════════════════════════════════

    private FakeSwapEnvironment Seed(int seed)
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "p3-" + seed));
        env.CurrentVersion = N2;
        switch (seed)
        {
            case 1: // prev N by N+1 · zip N,N+2 · 지금 N+2 — 그 사이 한 판 더 갔다(묵은 prev)
                Prev(env, N, N1);
                Zip(env, N); Zip(env, N2);
                break;
            case 2: // 이력 N,N+1,N+2 · zip N,N+2 — N+1 은 설치 EXE·수동으로 들어와 zip 이 없다
                History(env, N, N1, N2);
                Zip(env, N); Zip(env, N2);
                break;
            case 3: // 이력 N+1,N+2 · zip N+1,N+2 — 바로 앞 판 zip
                History(env, N1, N2);
                Zip(env, N1); Zip(env, N2);
                break;
            case 4: // 이력 없음 · prev 없음 · zip N+1,N+2 — 봉합 전과 같음(1.3.48 비상 경로)
                Zip(env, N1); Zip(env, N2);
                break;
        }
        return env;
    }

    [Theory(DisplayName = "G-P3 🔴 재료② 인접 — (1) 묵은 prev (2) 이력상 직전 판 zip 없음 = 거부 no_previous_version · 작업 등록 0 / (3)(4) 허용")]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void GP3_staging_zip_must_be_the_immediate_previous(int seed, bool allowed)
    {
        var env = Seed(seed);

        var material = RollbackMaterialFinder.Find(env.AppRoot!, env.WatchdogStagingDir, env.CurrentVersion, out var reason);

        var sc = new FakeSchtasks();
        var svc = new LocalRollbackService(Launcher(env, sc), env, NullLogger<LocalRollbackService>.Instance);
        var status = svc.GetStatus("gate-user");
        var start = svc.Start("gate-user", status.Ticket);

        if (allowed)
        {
            Assert.NotNull(material);
            Assert.Equal(SwapReasons.Ok, reason);
            Assert.Equal(SwapMaterialKinds.StagingZip, material!.Kind);
            Assert.Equal(N1, material.Version);
            Assert.True(status.CanRollback, status.Reason);
            Assert.True(start.Started, start.Reason);
            Assert.Equal(1, sc.CountStartingWith("/Create"));
        }
        else
        {
            Assert.Null(material);
            Assert.Equal(SwapReasons.NoPreviousVersion, reason);
            Assert.False(status.CanRollback);
            Assert.Equal(SwapReasons.NoPreviousVersion, status.Reason);
            Assert.False(start.Started);
            Assert.Equal(0, sc.CountStartingWith("/Create"));
            Assert.False(File.Exists(Path.Combine(Work(env), LocalSwapLauncher.RequestFileName)));
        }
    }

    [Fact(DisplayName = "G-P3 판 이력 읽기 — 마지막 줄 = 지금 판일 때만 · 모양 틀린 줄 건너뜀 · 같은 판 줄은 넘어 올라감 · 한 줄뿐 = 모름")]
    public void GP3_ledger_reading_rules()
    {
        var dir = Path.Combine(_root, "ledger");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, LocalSwapLauncher.VersionsSeenFileName);
        var cur = new Version(1, 3, 49);

        File.WriteAllLines(path, new[] { "1.3.47|2026-09-01T00:00:00Z", "garbage", "1.3.48|x|y", "1.3.49|2026-09-02T00:00:00Z", "1.3.49|2026-09-03T00:00:00Z" });
        Assert.True(RollbackMaterialFinder.TryReadPreviousVersion(path, cur, out var p));
        Assert.Equal(new Version(1, 3, 47), p);

        File.WriteAllLines(path, new[] { "1.3.48|2026-09-01T00:00:00Z", "1.3.50|2026-09-02T00:00:00Z" });
        Assert.False(RollbackMaterialFinder.TryReadPreviousVersion(path, cur, out _)); // 마지막 ≠ 지금 판

        File.WriteAllLines(path, new[] { "1.3.49|2026-09-01T00:00:00Z" });
        Assert.False(RollbackMaterialFinder.TryReadPreviousVersion(path, cur, out _)); // 한 줄뿐

        Assert.False(RollbackMaterialFinder.TryReadPreviousVersion(Path.Combine(dir, "none.txt"), cur, out _)); // 파일 없음
    }

    // ══════════════════════════════════════════════════════════════
    // G-R1 남은 작업 — 설계 §14-3
    // ══════════════════════════════════════════════════════════════

    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static void OpenRequest(FakeSwapEnvironment env, string state, int minutesAgo)
    {
        Directory.CreateDirectory(Work(env));
        var r = new SwapRequest
        {
            Ticket = LocalSwapLauncher.NewTicket(), Mode = SwapModes.Rollback, State = state, From = N2, To = N1,
            RequestedAt = T0.AddMinutes(-minutesAgo - 1), UpdatedAt = T0.AddMinutes(-minutesAgo), AppRoot = env.AppRoot!, Slot = 1,
            Step = state == SwapStates.Running ? "S4" : null,
        };
        File.WriteAllText(Path.Combine(Work(env), LocalSwapLauncher.RequestFileName), r.ToJson());
    }

    private static int Deletes(FakeSchtasks sc) =>
        sc.Calls.Count(c => c.StartsWith("/Delete /TN \"" + LocalSwapLauncher.TaskName + "\" /F", StringComparison.Ordinal));

    [Fact(DisplayName = "G-R1 🔴 남은 작업 — requested 31분 = 작업 지움 · refused/worker_not_started · 바쁨 아님 (대조: 29분 = swap_in_progress · 지우기 0)")]
    public void GR1_requested_stale_task_is_cleared()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-req31"));
        env.UtcNow = T0;
        OpenRequest(env, SwapStates.Requested, 31);
        var sc = new FakeSchtasks();
        sc.Tasks.Add(LocalSwapLauncher.TaskName);
        var launcher = Launcher(env, sc);

        Assert.Null(launcher.CheckBusy());
        Assert.Equal(1, Deletes(sc));
        Assert.DoesNotContain(LocalSwapLauncher.TaskName, sc.Tasks);
        var last = launcher.ReadLast()!;
        Assert.Equal(SwapStates.Refused, last.State);
        Assert.Equal(SwapReasons.WorkerNotStarted, last.Reason);
        Assert.Equal(T0.AddMinutes(-31), last.LastTouchedUtc); // 끝 상태를 적어도 쿨다운을 새로 걸지 않는다(계약 §4 봉합)
        Assert.Null(launcher.CheckBusy()); // 다시 물어도 그대로(쿨다운 0 · refused)

        // 대조 — 29분이면 아직 살아 있는 요청: 바쁨 · 지우기 0 · 요청서 그대로
        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-req29"));
        env2.UtcNow = T0;
        OpenRequest(env2, SwapStates.Requested, 29);
        var sc2 = new FakeSchtasks();
        sc2.Tasks.Add(LocalSwapLauncher.TaskName);
        var l2 = Launcher(env2, sc2);
        Assert.Equal(SwapReasons.SwapInProgress, l2.CheckBusy());
        Assert.Equal(0, Deletes(sc2));
        Assert.Equal(SwapStates.Requested, l2.ReadLast()!.State);
    }

    [Fact(DisplayName = "G-R1 🔴 남은 작업 — running 31분 + api.rbk = broken/worker_interrupted · 사유 swap_interrupted (「진행 중」 아님)")]
    public void GR1_running_stale_with_rbk_is_interrupted()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-run31"));
        env.UtcNow = T0;
        OpenRequest(env, SwapStates.Running, 31);
        Directory.CreateDirectory(Path.Combine(env.AppRoot!, "api.rbk"));
        var sc = new FakeSchtasks();
        sc.Tasks.Add(LocalSwapLauncher.TaskName);
        var launcher = Launcher(env, sc);

        Assert.Equal(SwapReasons.SwapInterrupted, launcher.CheckBusy());
        Assert.Equal(1, Deletes(sc));
        var last = launcher.ReadLast()!;
        Assert.Equal(SwapStates.Broken, last.State);
        Assert.Equal(SwapReasons.WorkerInterrupted, last.Reason);
        Assert.Equal("S4", last.Step);
        Assert.Equal(SwapReasons.SwapInterrupted, launcher.CheckBusy()); // 다시 물어도 같은 답(쿨다운으로 바뀌지 않는다)

        // 교체는 막힌다 — 작업 등록 0
        var r = launcher.Launch(new SwapLaunchInput(SwapModes.Rollback, N2, N1,
            new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = "x" }, "gate-user", SwapEntries.Menu, null, null));
        Assert.False(r.Started);
        Assert.Equal(SwapReasons.SwapInterrupted, r.Reason);
        Assert.Equal(0, sc.CountStartingWith("/Create"));

        // 대조 — .rbk 가 남았어도 마지막 요청이 broken 이 아니면 종전대로 update_in_progress
        //   ⬛ 20260930작1 봉합2(설계 15-3 기대값 변경 ② · PM 결재 T-4): 그 기대값이 N-1 거짓 「진행 중」 자체였다.
        //   이제 15-0 모양대로 — web.rbk 있는데 살아 있는 web 없음(M2) · 워치독 판 null(M3) ⇒ swap_interrupted · update_in_progress 아님.
        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-rbk-only"));
        env2.UtcNow = T0;
        Directory.CreateDirectory(Path.Combine(env2.AppRoot!, "web.rbk"));
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env2, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "G-R1 남은 작업 — swap.lock 이 30분 안이면 작업이 있어도 바쁨 · 지우기 0 / 요청서 없이 작업만 묵었으면 지우고 통과")]
    public void GR1_fresh_lock_keeps_task_and_orphan_task_is_cleared()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-lock"));
        env.UtcNow = T0;
        Directory.CreateDirectory(Work(env));
        File.WriteAllText(Path.Combine(Work(env), LocalSwapLauncher.LockFileName),
            LocalSwapLauncher.NewTicket() + "|" + T0.AddMinutes(-5).ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        var sc = new FakeSchtasks();
        sc.Tasks.Add(LocalSwapLauncher.TaskName);
        Assert.Equal(SwapReasons.SwapInProgress, Launcher(env, sc).CheckBusy());
        Assert.Equal(0, Deletes(sc));

        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-orphan"));
        env2.UtcNow = T0;
        var sc2 = new FakeSchtasks();
        sc2.Tasks.Add(LocalSwapLauncher.TaskName);
        Assert.Null(Launcher(env2, sc2).CheckBusy());
        Assert.Equal(1, Deletes(sc2));
    }

    // ══════════════════════════════════════════════════════════════
    // 07·F-4 런처 쪽 — CheckReady(사전 판정 한 벌) · TryReserve/Release(프로세스 안 예약) · CheckBusy(owner)
    //   (서비스 연결 게이트 G-U7·G-U5d 는 갈래 M)
    // ══════════════════════════════════════════════════════════════

    private static SwapLaunchInput Input(string? owner = null) => new(
        SwapModes.Rollback, N2, N1, new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = "x" },
        "gate-user", SwapEntries.Menu, null, null, owner);

    [Fact(DisplayName = "07 CheckReady — 슬롯 없음 request_invalid · 폴더 문지기 실패 folder_unsafe · 원본 없음 script_missing · 되면 null · Launch 도 같은 답(작업 등록 0)")]
    public void CheckReady_is_the_static_gate_of_launch()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "ready"));
        var sc = new FakeSchtasks();
        var guard = new HookedFolderGuard();
        var launcher = new LocalSwapLauncher(env, sc, guard, NullLogger<LocalSwapLauncher>.Instance);

        Assert.Null(launcher.CheckReady());

        env.Slot = null;
        Assert.Equal(SwapReasons.RequestInvalid, launcher.CheckReady());
        Assert.Equal(SwapReasons.RequestInvalid, launcher.Launch(Input()).Reason);
        env.Slot = 1;

        guard.Before = _ => throw new InvalidOperationException("gate: unsafe");
        Assert.Equal(SwapReasons.FolderUnsafe, launcher.CheckReady());
        Assert.Equal(SwapReasons.FolderUnsafe, launcher.Launch(Input()).Reason);
        guard.Before = null;

        var script = env.ScriptSourcePath;
        env.ScriptSourcePath = script + ".missing";
        Assert.Equal(SwapReasons.ScriptMissing, launcher.CheckReady());
        env.ScriptSourcePath = script;

        env.IsWindows = false;
        Assert.Equal(SwapReasons.NotWindows, launcher.CheckReady());
        env.IsWindows = true;

        Assert.Equal(0, sc.CountStartingWith("/Create"));
        Assert.Equal(0, sc.CountStartingWith("/Query")); // 정적 판정은 바쁨(작업 조회)을 보지 않는다
    }

    [Fact(DisplayName = "F-4 🔴 예약 — 남의 예약 = swap_in_progress · Launch 0 / 자기 예약(Owner) = 시작 · 해제는 주인만 · 30분 갱신 없으면 풀림")]
    public void Reservation_blocks_others_but_not_its_owner()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "reserve"));
        env.UtcNow = T0;
        var sc = new FakeSchtasks();
        var launcher = Launcher(env, sc);

        Assert.Null(launcher.CheckBusy());
        Assert.True(launcher.TryReserve("manual-update"));
        Assert.True(launcher.TryReserve("manual-update")); // 같은 주인 = 다시 잡기(시각 갱신)
        Assert.False(launcher.TryReserve("rollback"));
        Assert.False(launcher.TryReserve(" "));

        Assert.Equal(SwapReasons.SwapInProgress, launcher.CheckBusy());
        Assert.Equal(SwapReasons.SwapInProgress, launcher.CheckBusy("rollback"));
        Assert.Null(launcher.CheckBusy("manual-update"));

        // 남(예약 없는 되돌리기)은 막힌다 — 작업 등록 0
        var other = launcher.Launch(Input());
        Assert.False(other.Started);
        Assert.Equal(SwapReasons.SwapInProgress, other.Reason);
        Assert.Equal(0, sc.CountStartingWith("/Create"));

        // 남의 해제는 무시
        launcher.Release("rollback");
        Assert.Equal(SwapReasons.SwapInProgress, launcher.CheckBusy());

        // 주인은 자기 예약에 막히지 않는다
        var mine = launcher.Launch(Input("manual-update"));
        Assert.True(mine.Started, mine.Reason);
        Assert.Equal(1, sc.CountStartingWith("/Create"));

        launcher.Release("manual-update");
        Assert.True(launcher.TryReserve("rollback"));
        launcher.Release("rollback");

        // 30분 갱신이 없으면 풀린 것으로 본다(해제 누락이 영구 바쁨으로 번지지 않게)
        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "reserve-expire"));
        env2.UtcNow = T0;
        var l2 = Launcher(env2, new FakeSchtasks());
        Assert.True(l2.TryReserve("manual-update"));
        env2.UtcNow = T0.AddMinutes(29);
        Assert.Equal(SwapReasons.SwapInProgress, l2.CheckBusy());
        env2.UtcNow = T0.AddMinutes(31);
        Assert.Null(l2.CheckBusy());
        Assert.True(l2.TryReserve("rollback"));
    }
}