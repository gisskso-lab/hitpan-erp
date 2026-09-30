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

    private Rig Build(string name)
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
            new ManualUpdateEnvironment(() => true, () => Current), NullLogger<ManualUpdateService>.Instance);
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

    // ── 대역 ──

    private sealed class CountingLauncher(ILocalSwapLauncher inner) : ILocalSwapLauncher
    {
        private int _launches;
        public int Launches => Volatile.Read(ref _launches);
        public string? CheckBusy() => inner.CheckBusy();
        public string? CheckBusy(string? owner) => inner.CheckBusy(owner);
        public SwapRequest? ReadLast() => inner.ReadLast();
        public string? CheckReady() => inner.CheckReady();
        public bool TryReserve(string owner) => inner.TryReserve(owner);
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

        public bool HasEnoughSpace(long packageSizeBytes, string targetDir) => true;

        public async Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            Interlocked.Increment(ref Downloads);
            Entered.TrySetResult();
            if (Hold is not null)
            {
                await Hold.Task.ConfigureAwait(false);
                throw new IOException("gate: download stopped");
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
