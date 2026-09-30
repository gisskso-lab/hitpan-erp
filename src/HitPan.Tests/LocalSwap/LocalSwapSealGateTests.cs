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
        var env2 = FakeSwapEnvironment.Under(Path.Combine(_root, "r1-rbk-only"));
        env2.UtcNow = T0;
        Directory.CreateDirectory(Path.Combine(env2.AppRoot!, "web.rbk"));
        Assert.Equal(SwapReasons.UpdateInProgress, Launcher(env2, new FakeSchtasks()).CheckBusy());
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
}
