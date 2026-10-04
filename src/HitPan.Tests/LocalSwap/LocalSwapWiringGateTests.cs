using System.Text;
using System.Text.RegularExpressions;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 봉합 갈래 M(연결) — 설계 §14-3 G-V1 · G-U7 · G-U5d.
/// </summary>
/// <remarks>
/// <para>🟢 실제 <see cref="InstalledVersionLedger"/>·<see cref="LocalSwapLauncher"/>·<see cref="ManualUpdateService"/>·<see cref="LocalRollbackService"/> 를 부른다(글자 검사는 G-V1 의 DI 한 줄뿐 — 등록 줄이 없으면 기동 때 아무도 안 부른다).
/// 설치 폴더는 임시 폴더 · 예약 작업은 메모리(<see cref="FakeSchtasks"/>) · 피드·받기·백업은 대역 · 실제 schtasks·서비스·<c>C:\Program Files\HitPan</c> 0.</para>
/// <para>🔴 대조(개발명세서 M §4): G-V1 = <c>Program.cs</c> 등록 줄 뺌 · G-U7 = 두 서비스의 <c>CheckReady</c> 호출 뺌 · G-U5d = <c>ManualUpdateService.Start</c> 의 <c>TryReserve</c> 뺌 — 각각 FAIL 확인.</para>
/// </remarks>
public sealed class LocalSwapWiringGateTests : IDisposable
{
    private const string Current = "1.3.48";
    private const string Newer = "1.3.49";
    private const string Older = "1.3.47";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-m-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapWiring] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-V1 판 이력 기록(05ⓑ)
    // ══════════════════════════════════════════════════════════════

    private FakeSwapEnvironment Env(string name)
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, name));
        env.CurrentVersion = Current;
        env.UtcNow = new DateTime(2026, 9, 30, 13, 0, 0, DateTimeKind.Utc);
        return env;
    }

    private static string LedgerPath(FakeSwapEnvironment env) =>
        Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName, LocalSwapLauncher.VersionsSeenFileName);

    private static void SeedLedger(FakeSwapEnvironment env, params string[] versions)
    {
        var dir = Path.GetDirectoryName(LedgerPath(env))!;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        foreach (var v in versions) sb.Append(v).Append("|2026-09-29T00:00:00.0000000Z\r\n");
        File.WriteAllText(LedgerPath(env), sb.ToString(), new UTF8Encoding(false));
    }

    [Fact(DisplayName = "G-V1 🔴 판 이력 — 끝 줄 Y(1.3.47) 면 지금 판 X(1.3.48) 한 줄이 덧붙고, 읽는 쪽이 Y 를 직전 판으로 읽는다")]
    public void V1_appends_current_when_last_line_differs()
    {
        var env = Env("v1a");
        SeedLedger(env, "1.3.46", Older);
        var log = new ListLogger<InstalledVersionLedger>();
        var ledger = new InstalledVersionLedger(env, new HookedFolderGuard(), log);

        Assert.True(ledger.Record());

        var lines = File.ReadAllLines(LedgerPath(env));
        Assert.Equal(3, lines.Length);
        Assert.Equal("1.3.46|2026-09-29T00:00:00.0000000Z", lines[0]);
        Assert.Equal(Older + "|2026-09-29T00:00:00.0000000Z", lines[1]);
        Assert.Equal(Current + "|2026-09-30T13:00:00.0000000Z", lines[2]); // 계약 §2 형식 {M.m.b}|{UTC o}
        var bytes = File.ReadAllBytes(LedgerPath(env));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM 이 붙었다(계약 §2 · BOM 없음)");
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        // 읽는 쪽(L) 과 한 벌 — 지금 판이 마지막 줄 ⇒ 바로 위 다른 판 = 직전 설치 판
        Assert.True(RollbackMaterialFinder.TryReadPreviousVersion(LedgerPath(env), new Version(1, 3, 48), out var prev));
        Assert.Equal(new Version(1, 3, 47), prev);

        // 파일이 없을 때도 첫 줄을 만든다(봉합 판부터 쌓인다)
        var fresh = Env("v1a-new");
        Assert.True(new InstalledVersionLedger(fresh, new HookedFolderGuard(), log).Record());
        Assert.Equal(new[] { Current + "|2026-09-30T13:00:00.0000000Z" }, File.ReadAllLines(LedgerPath(fresh)));
    }

    [Fact(DisplayName = "G-V1 🔴 판 이력 — 끝 줄이 이미 지금 판 X 면 그대로(재시작마다 줄이 늘지 않는다) · 끊긴 끝 줄 뒤엔 새 줄로")]
    public void V1_same_version_is_not_written_again()
    {
        var env = Env("v1b");
        SeedLedger(env, Older, Current);
        var before = File.ReadAllBytes(LedgerPath(env));
        var ledger = new InstalledVersionLedger(env, new HookedFolderGuard(), new ListLogger<InstalledVersionLedger>());

        Assert.False(ledger.Record());
        Assert.False(ledger.Record());
        Assert.Equal(before, File.ReadAllBytes(LedgerPath(env)));

        // 끝에 모양이 틀린 줄이 있어도 「읽히는 마지막 줄」로 판단한다(읽는 쪽과 같은 규칙)
        File.AppendAllText(LedgerPath(env), "garbage\r\n", new UTF8Encoding(false));
        Assert.False(ledger.Record());

        // 줄바꿈 없이 끊긴 끝 줄(쓰다 끊김) 뒤 — 새 판은 새 줄로 시작해 둘 다 읽힌다
        var cut = Env("v1b-cut");
        SeedLedger(cut, "1.3.46");
        File.AppendAllText(LedgerPath(cut), Older + "|2026-09-29T0", new UTF8Encoding(false));
        Assert.True(new InstalledVersionLedger(cut, new HookedFolderGuard(), new ListLogger<InstalledVersionLedger>()).Record());
        Assert.Equal(Current + "|2026-09-30T13:00:00.0000000Z", File.ReadAllLines(LedgerPath(cut))[^1]);
    }

    [Fact(DisplayName = "G-V1 🔴 판 이력 — 폴더 문지기 실패면 안 쓰고 경고만 · 기동(StartAsync)은 무엇이 나도 던지지 않는다(S-2)")]
    public async Task V1_guard_failure_writes_nothing_and_never_blocks_startup()
    {
        var env = Env("v1c");
        var guard = new HookedFolderGuard { Before = _ => throw new InvalidOperationException("gate: unsafe folder") };
        var log = new ListLogger<InstalledVersionLedger>();
        var ledger = new InstalledVersionLedger(env, guard, log);

        Assert.False(ledger.Record());
        Assert.False(File.Exists(LedgerPath(env)));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException);

        // 예상 못 한 예외(문지기 계약 밖)도 기동으로 번지지 않는다 — 경고 한 줄
        var odd = new InstalledVersionLedger(env, new HookedFolderGuard { Before = _ => throw new NotSupportedException("gate: odd") }, log);
        await odd.StartAsync(CancellationToken.None);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Exception is NotSupportedException);
        Assert.False(File.Exists(LedgerPath(env)));

        // 설치 루트 없음(개발 실행) — 안 쓰고 조용히
        var dev = new FakeSwapEnvironment { AppRoot = null, CurrentVersion = Current };
        Assert.False(new InstalledVersionLedger(dev, new HookedFolderGuard(), log).Record());
    }

    [Fact(DisplayName = "G-V1 🔴 DI — API Program.cs 에 기록기가 기동 서비스로 한 번 등록돼 있고, 그 등록으로 뜨면 한 줄이 적힌다")]
    public async Task V1_registered_as_hosted_service_in_program()
    {
        var program = File.ReadAllLines(Path.Combine(RepoRoot(), "src", "HitPan.API", "Program.cs"));
        var code = program.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
        var reg = new Regex(@"builder\.Services\.AddHostedService<\s*(?:HitPan\.API\.Services\.LocalSwap\.)?InstalledVersionLedger\s*>\s*\(\s*\)\s*;",
            RegexOptions.CultureInvariant);
        Assert.True(code.Count(l => reg.IsMatch(l)) == 1,
            "Program.cs 에 InstalledVersionLedger 기동 등록(AddHostedService) 이 정확히 한 줄 있어야 한다 — 없으면 판 이력이 영영 안 쌓여 05ⓑ 가 늘 「직전 판 모름」");
        // 기록기가 받는 두 의존도 같은 Program.cs 에 등록돼 있어야 기동 때 풀린다
        Assert.Contains(code, l => l.Contains("AddSingleton<HitPan.API.Services.LocalSwap.ILocalSwapEnvironment,", StringComparison.Ordinal));
        Assert.Contains(code, l => l.Contains("AddSingleton<HitPan.API.Services.LocalSwap.ISwapFolderGuard,", StringComparison.Ordinal));

        // 같은 모양의 등록으로 호스트 서비스를 풀어 기동하면 실제로 한 줄이 적힌다(등록 = 동작)
        var env = Env("v1d");
        SeedLedger(env, Older);
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<ILocalSwapEnvironment>(env);
        sc.AddSingleton<ISwapFolderGuard>(new HookedFolderGuard());
        sc.AddHostedService<InstalledVersionLedger>();
        using var sp = sc.BuildServiceProvider();
        foreach (var h in sp.GetServices<IHostedService>()) await h.StartAsync(CancellationToken.None);
        Assert.Equal(Current + "|2026-09-30T13:00:00.0000000Z", File.ReadAllLines(LedgerPath(env))[^1]);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "HitPan.API", "Program.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("레포 루트를 못 찾았다");
    }

    // ══════════════════════════════════════════════════════════════
    // G-U7 사전 판정(07) · G-U5d 예약(F-4)
    // ══════════════════════════════════════════════════════════════

    /// <summary>재료 ① prev(1.3.47 · 지금 판 1.3.48 이 밀어냄) — 되돌리기 판정이 번호를 줄 수 있는 모양.</summary>
    private static void SeedPrev(FakeSwapEnvironment env)
    {
        var prev = Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName, "prev");
        foreach (var p in new[] { "api", "web", "watchdog" }) Directory.CreateDirectory(Path.Combine(prev, p));
        File.WriteAllText(Path.Combine(prev, "sha256.txt"), "x");
        File.WriteAllText(Path.Combine(prev, "version.txt"), Older);
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), Current);
    }

    private sealed class Rig
    {
        public required FakeSwapEnvironment Env { get; init; }
        public required HookedFolderGuard Guard { get; init; }
        public required FakeSchtasks Tasks { get; init; }
        public required CountingLauncher Launcher { get; init; }
        public required NewerFeed Feed { get; init; }
        public required ZipFetcher Fetch { get; init; }
        public required CountingBackup Backup { get; init; }
        public required ManualUpdateService Manual { get; init; }
        public required LocalRollbackService Rollback { get; init; }
    }

    // 20260930작1 봉합2 B2 — renew = 한 단계 안 예약 갱신 주기(설계 15-1 10ⓑ). null = 운영 기본(5분) · 0 = 끔.
    private Rig Build(string name, TimeSpan? renew = null)
    {
        var env = Env(name);
        SeedPrev(env);
        var guard = new HookedFolderGuard();
        var tasks = new FakeSchtasks();
        var launcher = new CountingLauncher(new LocalSwapLauncher(env, tasks, guard, NullLogger<LocalSwapLauncher>.Instance));
        var feed = new NewerFeed();
        var fetch = new ZipFetcher();
        var backup = new CountingBackup();
        var sc = new ServiceCollection();
        sc.AddScoped<IBackupService>(_ => backup);
        var scopes = sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var folders = new ManualFolders(env.AppRoot!);
        var usage = new ManualUsageLog(folders, NullLogger<ManualUsageLog>.Instance, (_, _, _) => { });
        var manual = new ManualUpdateService(feed, fetch, new NoAutoLock(), scopes, folders, usage, launcher,
            new ManualUpdateEnvironment(() => true, () => Current), NullLogger<ManualUpdateService>.Instance,
            reservationRenewInterval: renew);
        var rollback = new LocalRollbackService(launcher, env, NullLogger<LocalRollbackService>.Instance);
        return new Rig
        {
            Env = env, Guard = guard, Tasks = tasks, Launcher = launcher, Feed = feed, Fetch = fetch,
            Backup = backup, Manual = manual, Rollback = rollback,
        };
    }

    [Fact(DisplayName = "G-U7 🔴 슬롯 없음 — 수동 업데이트 [예] 즉시 거부 · 피드·받기·백업 0 · 되돌리기 번호 발급 0 (대조: 슬롯 있으면 번호가 나온다)")]
    public async Task U7_slot_missing_refuses_before_download_and_backup()
    {
        var r = Build("u7-slot");
        r.Env.Slot = null;

        var start = r.Manual.Start("T-1", "u1", "menu");
        await r.Manual.LastRun;
        Assert.False(start.Accepted, "슬롯이 없는데 [예] 를 받아들였다(받기·백업 뒤에야 런처가 거부한다)");
        Assert.Equal(SwapReasons.RequestInvalid, start.Reason);
        Assert.Equal(0, r.Feed.Calls);
        Assert.Equal(0, r.Fetch.Downloads);
        Assert.Equal(0, r.Backup.Runs);
        Assert.Equal(0, r.Launcher.Launches);

        var status = r.Rollback.GetStatus("u1");
        Assert.False(status.CanRollback);
        Assert.Null(status.Ticket);
        Assert.Equal(SwapReasons.RequestInvalid, status.Reason);

        // 확인(읽기만) 도 「가능」 거짓 0 — 피드를 묻지 않고 사유를 보인다
        var check = await r.Manual.CheckAsync(CancellationToken.None);
        Assert.False(check.UpdateAvailable);
        Assert.Equal(SwapReasons.RequestInvalid, check.Reason);
        Assert.Equal(0, r.Feed.Calls);

        // 대조군 — 한 칸(슬롯)만 되돌리면 같은 재료로 번호가 나온다(거부가 사전 판정 때문임을 확인)
        r.Env.Slot = 1;
        var ok = r.Rollback.GetStatus("u1");
        Assert.True(ok.CanRollback, ok.Reason);
        Assert.NotNull(ok.Ticket);
    }

    [Fact(DisplayName = "G-U7 🔴 작업 폴더 문지기 실패 — 수동 업데이트 [예] 즉시 folder_unsafe · 받기·백업 0 · 되돌리기 번호 발급 0")]
    public async Task U7_unsafe_work_folder_refuses_before_download_and_backup()
    {
        var r = Build("u7-guard");
        r.Guard.Before = p => throw new InvalidOperationException("gate: unsafe " + p);

        var start = r.Manual.Start("T-1", "u1", "menu");
        await r.Manual.LastRun;
        Assert.False(start.Accepted, "작업 폴더가 안전하지 않은데 [예] 를 받아들였다");
        Assert.Equal(SwapReasons.FolderUnsafe, start.Reason);
        Assert.Equal(0, r.Fetch.Downloads);
        Assert.Equal(0, r.Backup.Runs);
        Assert.Equal(0, r.Launcher.Launches);

        var status = r.Rollback.GetStatus("u1");
        Assert.False(status.CanRollback);
        Assert.Null(status.Ticket);
        Assert.Equal(SwapReasons.FolderUnsafe, status.Reason);

        // 대조군 — 문지기가 통과하면 [예] 를 받아들이고 받기·백업·넘기기까지 간다
        r.Guard.Before = null;
        var ok = r.Manual.Start("T-1", "u1", "menu");
        Assert.True(ok.Accepted, ok.Reason);
        await r.Manual.LastRun;
        Assert.Equal(1, r.Backup.Runs);
        Assert.Equal(1, r.Launcher.Launches);
    }

    [Fact(DisplayName = "G-U5d 🔴 수동 업데이트가 받기에서 멈춰 있을 때 되돌리기 [예] → swap_in_progress · Launch 0 (대조: 끝난 뒤엔 걸린다)")]
    public async Task U5d_reservation_blocks_rollback_during_download()
    {
        var r = Build("u5d");
        r.Fetch.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var early = r.Rollback.GetStatus("u1"); // 수동 업데이트 전에 받아 둔 확인 번호
        Assert.True(early.CanRollback, early.Reason);

        var start = r.Manual.Start("T-1", "u1", "menu");
        Assert.True(start.Accepted, start.Reason);
        await r.Fetch.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)); // 받기 한가운데

        var rb = r.Rollback.Start("u1", early.Ticket);
        Assert.False(rb.Started, "수동 업데이트 받기 중인데 되돌리기가 걸렸다(F-4)");
        Assert.Equal(SwapReasons.SwapInProgress, rb.Reason);
        Assert.Equal(0, r.Launcher.Launches);
        Assert.Equal(0, r.Tasks.CountStartingWith("/Create"));
        // 화면 상태 조회도 진행 중(번호 발급 0)
        var during = r.Rollback.GetStatus("u1");
        Assert.Equal(SwapReasons.SwapInProgress, during.Reason);
        Assert.Null(during.Ticket);

        // 받기 실패로 끝 — 예약이 풀린다
        r.Fetch.Hold.SetResult();
        await r.Manual.LastRun;
        Assert.Equal(ManualUpdateStages.Refused, r.Manual.GetStatus("T-1")!.Stage);

        // 대조군 — 예약이 풀리면 같은 되돌리기가 걸린다(거부가 예약 때문임을 확인)
        var again = r.Rollback.GetStatus("u1");
        Assert.True(again.CanRollback, again.Reason);
        var go = r.Rollback.Start("u1", again.Ticket);
        Assert.True(go.Started, go.Reason);
        Assert.Equal(1, r.Launcher.Launches);
    }

    [Fact(DisplayName = "G-U5d 🔴 끝나지 않은 교체(swap_interrupted) — 수동 업데이트 확인이 「가능」을 내지 않는다 · [예] 도 거부")]
    public async Task Interrupted_swap_is_not_reported_as_available()
    {
        var r = Build("interrupted");
        var app = r.Env.AppRoot!;
        Directory.CreateDirectory(Path.Combine(app, "api.rbk"));
        var req = new SwapRequest
        {
            Ticket = LocalSwapLauncher.NewTicket(),
            Mode = SwapModes.Update,
            State = SwapStates.Broken,
            From = Current,
            To = Newer,
            Material = new SwapMaterial { Kind = SwapMaterialKinds.ManualZip, Path = "x" },
            RequestedAt = r.Env.UtcNow.AddHours(-2),
            UpdatedAt = r.Env.UtcNow.AddHours(-2),
        };
        File.WriteAllText(Path.Combine(app, LocalSwapLauncher.WorkFolderName, LocalSwapLauncher.RequestFileName), req.ToJson());
        Assert.Equal(SwapReasons.SwapInterrupted, r.Launcher.CheckBusy());

        var check = await r.Manual.CheckAsync(CancellationToken.None);
        Assert.False(check.UpdateAvailable, "끝나지 않은 교체인데 수동 업데이트가 「가능」이라 했다");
        Assert.Equal(SwapReasons.SwapInterrupted, check.Reason);
        Assert.Equal(0, r.Feed.Calls);

        var start = r.Manual.Start("T-1", "u1", "menu");
        await r.Manual.LastRun;
        Assert.False(start.Accepted);
        Assert.Equal(SwapReasons.SwapInterrupted, start.Reason);
        Assert.Equal(0, r.Backup.Runs);
    }

    // ══════════════════════════════════════════════════════════════
    // 20260930작1 봉합2 B2 — 설계 §15-3 G-U5e · G-U5h · G-U5f(10 예약 갱신) · G-OLD2 서비스 부분(N-2) · 워치독 판 출처(PM 추가)
    // 🔴 대조(개발명세서 B2 §4): G-U5e = 단계 갱신 뺌 · G-U5h = 주기 갱신 뺌 · G-U5f = 갱신 반환값 무시 · G-OLD2 = CheckAsync 새 줄 뺌
    //    · 워치독 판 = dll/exe 순서 뒤집음 — 각각 FAIL 확인.
    // 🔴 되돌리기 확인 번호는 10분짜리(LocalRollbackService.TicketLifetime)라 +40분 뒤엔 미리 받아 둔 번호가 만료된다 ⇒
    //    「되돌리기가 끼어들 수 있나」를 그 시각의 GetStatus(번호 발급) → Start 로 잰다(예약이 살아 있으면 번호 0 · Launch 0).
    // ══════════════════════════════════════════════════════════════

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Fact(DisplayName = "G-U5e 🔴 단계마다 예약 갱신 — 받기 +20분 → 백업 한가운데 +20분(주기 갱신 끔) → 되돌리기 번호 0 · Launch 0 (대조: 단계 갱신 빼면 만료돼 되돌리기가 걸린다)")]
    public async Task U5e_stage_renewal_keeps_reservation_across_stages()
    {
        var r = Build("u5e", renew: TimeSpan.Zero);
        r.Fetch.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        r.Fetch.SucceedAfterHold = true;
        r.Backup.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = r.Manual.Start("T-1", "u1", "menu");
        Assert.True(start.Accepted, start.Reason);
        await r.Fetch.Entered.Task.WaitAsync(Wait); // 받기 한가운데(시작 +0)

        r.Env.UtcNow = r.Env.UtcNow.AddMinutes(20); // 받기가 20분 걸렸다
        r.Fetch.Hold.SetResult();
        await r.Backup.Entered.Task.WaitAsync(Wait); // 백업 한가운데 — 검증·백업 단계로 옮길 때 +20분에 갱신됐어야 한다
        r.Env.UtcNow = r.Env.UtcNow.AddMinutes(20); // 시작 +40분 · 마지막 단계 갱신 +20분

        var st = r.Rollback.GetStatus("u1");
        var rb = r.Rollback.Start("u1", st.Ticket);
        Assert.Equal(0, r.Launcher.Launches);
        Assert.Equal(0, r.Tasks.CountStartingWith("/Create"));
        Assert.False(rb.Started, "받기+백업이 30분을 넘자 예약이 만료돼 되돌리기가 백업 도중 끼어들었다(10)");
        Assert.Equal(SwapReasons.SwapInProgress, st.Reason);
        Assert.Null(st.Ticket);

        // 백업이 끝나면 넘기기까지 간다(자기 예약에 막히지 않는다)
        r.Backup.Hold.SetResult();
        await r.Manual.LastRun;
        Assert.Equal(ManualUpdateStages.HandedOff, r.Manual.GetStatus("T-1")!.Stage);
        Assert.Equal(1, r.Launcher.Launches);
    }

    [Fact(DisplayName = "G-U5h 🔴 한 단계 안 주기 갱신(10ms) — 받기 한가운데 +20분 → 갱신 → +20분 → 되돌리기 번호 0 · Launch 0 (대조: 주기 갱신 빼면 걸린다)")]
    public async Task U5h_periodic_renewal_keeps_reservation_within_one_stage()
    {
        var r = Build("u5h", renew: TimeSpan.FromMilliseconds(10));
        r.Fetch.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = r.Manual.Start("T-1", "u1", "menu");
        Assert.True(start.Accepted, start.Reason);
        await r.Fetch.Entered.Task.WaitAsync(Wait); // 받기 한가운데 — 이 한 단계가 40분 걸린다

        r.Env.UtcNow = r.Env.UtcNow.AddMinutes(20);
        // 시계를 옮긴 뒤 주기 갱신이 두 번 돌 때까지(설계 「200ms 대기」 · 느린 CI 를 위해 최대 5초). 못 돌아도 여기서는 단언하지 않는다 —
        // 대조(주기 갱신 뺀 사본)는 아래 되돌리기 단언에서 FAIL 해야 한다.
        var before = r.Launcher.Reserves;
        var until = DateTime.UtcNow.AddSeconds(5);
        while (r.Launcher.Reserves < before + 2 && DateTime.UtcNow < until) await Task.Delay(20);
        await Task.Delay(200);
        r.Env.UtcNow = r.Env.UtcNow.AddMinutes(20); // 시작 +40분 · 단계 갱신은 +0분뿐

        var st = r.Rollback.GetStatus("u1");
        var rb = r.Rollback.Start("u1", st.Ticket);
        Assert.Equal(0, r.Launcher.Launches);
        Assert.False(rb.Started, "받기 한 단계가 30분을 넘자 예약이 만료돼 되돌리기가 끼어들었다(10ⓑ)");
        Assert.Equal(SwapReasons.SwapInProgress, st.Reason);
        Assert.Null(st.Ticket);

        r.Fetch.Hold.SetResult(); // 받기 실패로 끝 — 예약이 풀리고 주기 갱신도 멈춘다
        await r.Manual.LastRun;
        Assert.Equal(ManualUpdateStages.Refused, r.Manual.GetStatus("T-1")!.Stage);
        var reservesAtEnd = r.Launcher.Reserves;
        await Task.Delay(100);
        Assert.Equal(reservesAtEnd, r.Launcher.Reserves); // 끝난 뒤 주기 갱신 0(다시 쥐지 않는다)
        var again = r.Rollback.GetStatus("u1");
        Assert.True(again.CanRollback, again.Reason);
    }

    [Fact(DisplayName = "G-U5f 🔴 갱신 실패 — +31분 뒤 남이 예약 → 다음 단계 갱신 false → swap_in_progress 로 끝 · 백업 0 · Launch 0 (대조: 반환값 무시하면 백업·넘기기)")]
    public async Task U5f_failed_renewal_ends_without_backup_or_handoff()
    {
        var r = Build("u5f", renew: TimeSpan.Zero);
        r.Fetch.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        r.Fetch.SucceedAfterHold = true;

        var start = r.Manual.Start("T-1", "u1", "menu");
        Assert.True(start.Accepted, start.Reason);
        await r.Fetch.Entered.Task.WaitAsync(Wait);

        r.Env.UtcNow = r.Env.UtcNow.AddMinutes(31); // 만료(30분 갱신 없음 = 주인 없음 · 규칙 그대로)
        Assert.True(r.Launcher.TryReserve("other:gate"), "만료 뒤엔 남이 쥘 수 있어야 한다(만료 규칙 그대로)");

        r.Fetch.Hold.SetResult(); // 받기 성공 → 검증 단계로 옮기며 갱신 → false
        await r.Manual.LastRun;
        var s = r.Manual.GetStatus("T-1")!;
        Assert.Equal(ManualUpdateStages.Refused, s.Stage);
        Assert.Equal(ManualUpdateReasons.SwapInProgress, s.Reason);
        Assert.Equal(0, r.Backup.Runs);
        Assert.Equal(0, r.Launcher.Launches);
        Assert.Equal(0, r.Tasks.CountStartingWith("/Create"));
        // 남의 예약은 그대로(우리 끝이 남의 예약을 풀지 않는다)
        Assert.False(r.Launcher.TryReserve("third:gate"));
    }

    [Fact(DisplayName = "G-OLD2 🔴 서비스 — web.old 단독 ⇒ 수동 업데이트 확인 「가능」 아님(update_cleanup_pending · 피드 0) · [예] 받기 0·백업 0 · 되돌리기 번호 0 · .old 무접촉")]
    public async Task GOLD2_old_blocks_manual_update_and_rollback_ticket()
    {
        var r = Build("old2");
        var old = Path.Combine(r.Env.AppRoot!, "web.old");
        Directory.CreateDirectory(old);
        var oldFile = Path.Combine(old, "HitPan.Web.dll");
        File.WriteAllText(oldFile, "old");

        var check = await r.Manual.CheckAsync(CancellationToken.None);
        Assert.False(check.UpdateAvailable, "워치독 .old 가 남아 있는데 수동 업데이트가 「가능」이라 했다(N-2)");
        Assert.Equal(SwapReasons.UpdateCleanupPending, check.Reason);
        Assert.False(check.Busy, "정리 대기는 「진행 중」이 아니다");
        Assert.Equal(0, r.Feed.Calls);

        var start = r.Manual.Start("T-1", "u1", "menu");
        await r.Manual.LastRun;
        Assert.False(start.Accepted);
        Assert.Equal(SwapReasons.UpdateCleanupPending, start.Reason);
        Assert.Equal(0, r.Fetch.Downloads);
        Assert.Equal(0, r.Backup.Runs);
        Assert.Equal(0, r.Launcher.Launches);

        var status = r.Rollback.GetStatus("u1");
        Assert.False(status.CanRollback);
        Assert.Null(status.Ticket);
        Assert.Equal(SwapReasons.UpdateCleanupPending, status.Reason);

        Assert.True(File.Exists(oldFile), ".old 를 건드렸다(보기만 해야 한다)");

        // 대조군 — (시험이) .old 를 치우면 같은 재료로 번호가 나온다(거부가 .old 때문임을 확인)
        Directory.Delete(old, recursive: true);
        var ok = r.Rollback.GetStatus("u1");
        Assert.True(ok.CanRollback, ok.Reason);
    }

    [Fact(DisplayName = "G-WV 🔴 워치독 판 출처 — HitPan.Watchdog.dll 판 ≠ exe 판이면 dll 판(게시 파이프라인과 한 벌) · dll 없을 때만 exe · 둘 다 없으면 null")]
    public void WatchdogVersion_prefers_dll_over_exe()
    {
        var dir = Path.Combine(_root, "wv", "watchdog");
        Directory.CreateDirectory(dir);
        var dllSrc = typeof(Assert).Assembly.Location;   // xunit.assert — 판 2.x
        var exeSrc = typeof(object).Assembly.Location;   // System.Private.CoreLib — 판 8.x
        static string V(string p)
        {
            var f = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
            return $"{f.FileMajorPart}.{f.FileMinorPart}.{f.FileBuildPart}";
        }
        Assert.NotEqual(V(dllSrc), V(exeSrc)); // 전제 — 두 판이 달라야 어느 쪽을 읽었는지 가른다

        var dll = Path.Combine(dir, "HitPan.Watchdog.dll");
        var exe = Path.Combine(dir, "HitPan.Watchdog.exe");
        File.Copy(dllSrc, dll);
        File.Copy(exeSrc, exe);
        Assert.Equal(V(dllSrc), LocalSwapEnvironment.ReadWatchdogVersion(dir, NullLogger.Instance));

        File.Delete(dll);
        Assert.Equal(V(exeSrc), LocalSwapEnvironment.ReadWatchdogVersion(dir, NullLogger.Instance));

        File.Delete(exe);
        Assert.Null(LocalSwapEnvironment.ReadWatchdogVersion(dir, NullLogger.Instance));
    }

    // ── 대역 ──

    private sealed class CountingLauncher(ILocalSwapLauncher inner) : ILocalSwapLauncher
    {
        private int _launches;
        private int _reserves; // 봉합2 B2 — 예약(갱신 포함) 호출 수. 세기를 먼저 하고 안쪽을 부른다(센 뒤의 호출은 그 뒤 시계를 읽는다).
        public int Launches => Volatile.Read(ref _launches);
        public int Reserves => Volatile.Read(ref _reserves);
        public string? CheckBusy() => inner.CheckBusy();
        public string? CheckBusy(string? owner) => inner.CheckBusy(owner);
        public SwapRequest? ReadLast() => inner.ReadLast();
        public string? CheckReady() => inner.CheckReady();

        public bool TryReserve(string owner)
        {
            Interlocked.Increment(ref _reserves);
            return inner.TryReserve(owner);
        }
        public void Release(string owner) => inner.Release(owner);

        public SwapLaunchResult Launch(SwapLaunchInput input)
        {
            Interlocked.Increment(ref _launches);
            return inner.Launch(input);
        }
    }

    private sealed class NewerFeed : IUpdateFeed
    {
        public int Calls;

        public Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new FeedCheckResult(FeedCheckStatus.Newer,
                new FeedPackage(Newer, "stable", "https://example.invalid/hitpan-1.3.49.zip", new string('a', 64), 10, null), null));
        }
    }

    /// <summary>받기 대역 — <see cref="Hold"/> 가 있으면 받기 한가운데서 멈췄다가, 풀리면 받기 실패로 끝난다.</summary>
    private sealed class ZipFetcher : IPackageFetcher
    {
        public int Downloads;
        public TaskCompletionSource? Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>봉합2 B2 — true 면 <see cref="Hold"/> 가 풀린 뒤 받기 성공으로 끝난다(기본 false = 종전대로 실패).</summary>
        public bool SucceedAfterHold { get; set; }

        public bool HasEnoughSpace(long packageSizeBytes, string targetDir) => true;

        public async Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            Interlocked.Increment(ref Downloads);
            Entered.TrySetResult();
            if (Hold is not null)
            {
                await Hold.Task.ConfigureAwait(false);
                if (!SucceedAfterHold) throw new IOException("gate: download stopped");
            }
            var path = Path.Combine(targetDir, $"hitpan-{package.Version}.zip");
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 }, ct).ConfigureAwait(false);
            return path;
        }

        public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class NoAutoLock : IAutoUpdateLockProbe
    {
        public bool IsAutoUpdateInProgress() => false;
    }

    private sealed class CountingBackup : IBackupService
    {
        public int Runs;
        /// <summary>봉합2 B2 — 있으면 백업 한가운데서 멈췄다가 풀리면 성공으로 끝난다.</summary>
        public TaskCompletionSource? Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default)
        {
            Interlocked.Increment(ref Runs);
            Entered.TrySetResult();
            if (Hold is not null) await Hold.Task.ConfigureAwait(false);
            return new RunBackupResponse { Success = true };
        }

        public Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>경고 로그를 잡아 두는 기록기(「경고만 남긴다」를 동작으로 잰다).</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries
        {
            get { lock (_entries) return _entries.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }
}
