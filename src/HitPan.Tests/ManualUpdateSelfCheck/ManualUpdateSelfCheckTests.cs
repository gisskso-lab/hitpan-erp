using System.Reflection;
using HitPan.API.Controllers;
using HitPan.API.Services.ManualUpdate;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.ManualUpdateSelfCheck;

/// <summary>
/// 2026-09-30 작1 갈래 U — 구현자 자기 확인(대역만 · 실제 피드·예약작업·서비스 조작 0).
/// 정식 게이트 G-U1~U6 은 갈래 G 몫(tests ManualUpdate/*) — 이 파일은 그와 겹치지 않는 이름으로 둔 구현자 확인이다.
/// 각 판정은 음성대조군(규칙을 어긴 입력이면 판정이 뒤집힌다)을 함께 잰다.
/// </summary>
public sealed class ManualUpdateSelfCheckTests : IDisposable
{
    private const string Current = "1.3.48";
    private readonly string _appRoot = Path.Combine(Path.GetTempPath(), "hp-mu-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_appRoot)) Directory.Delete(_appRoot, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[ManualUpdateSelfCheck] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    // ── 대역 ───────────────────────────────────────────────
    private sealed class FakeFeed : IUpdateFeed
    {
        public FeedCheckResult Result { get; set; } = new(FeedCheckStatus.NotNewer, null, null);
        public int Calls { get; private set; }
        public Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeFetcher : IPackageFetcher
    {
        public bool HashOk { get; set; } = true;
        public int Downloads { get; private set; }
        public bool HasEnoughSpace(long packageSizeBytes, string targetDir) => true;
        public async Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            Downloads++;
            Directory.CreateDirectory(targetDir);
            var path = Path.Combine(targetDir, $"hitpan-{package.Version}.zip");
            await File.WriteAllTextAsync(path, "stub", ct);
            return path;
        }
        public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct) => Task.FromResult(HashOk);
    }

    private sealed class FakeLock : IAutoUpdateLockProbe
    {
        public bool Held { get; set; }
        public bool IsAutoUpdateInProgress() => Held;
    }

    private sealed class FakeBackup : IBackupService
    {
        public bool Ok { get; set; } = true;
        public int Runs { get; private set; }
        public Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default)
        {
            Runs++;
            return Task.FromResult(new RunBackupResponse { Success = Ok, Error = Ok ? null : "stub-fail" });
        }
        public Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed record Rig(ManualUpdateService Service, FakeFeed Feed, FakeFetcher Fetcher, FakeLock Lock, FakeBackup Backup,
        ManualFolders Folders, List<int> Events, HitPan.Tests.LocalSwap.FakeSchtasks Schtasks);

    private Rig Build()
    {
        var feed = new FakeFeed();
        var fetcher = new FakeFetcher();
        var autoLock = new FakeLock();
        var backup = new FakeBackup();
        var sc = new ServiceCollection();
        sc.AddScoped<IBackupService>(_ => backup);
        var scopes = sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        // 20260930작1 I-API — 런처가 이어졌다: 실제 LocalSwapLauncher + 대역(작업 등록은 메모리 · 설치 폴더는 임시).
        var swapEnv = HitPan.Tests.LocalSwap.FakeSwapEnvironment.Under(_appRoot);
        swapEnv.CurrentVersion = Current;
        var schtasks = new HitPan.Tests.LocalSwap.FakeSchtasks();
        var launcher = new HitPan.API.Services.LocalSwap.LocalSwapLauncher(swapEnv, schtasks,
            new HitPan.Tests.LocalSwap.HookedFolderGuard(), NullLogger<HitPan.API.Services.LocalSwap.LocalSwapLauncher>.Instance);
        var folders = new ManualFolders(swapEnv.AppRoot!);
        var events = new List<int>();
        var usage = new ManualUsageLog(folders, NullLogger<ManualUsageLog>.Instance, (id, _, _) => events.Add(id));
        var env = new ManualUpdateEnvironment(() => true, () => Current);
        var svc = new ManualUpdateService(feed, fetcher, autoLock, scopes, folders, usage, launcher, env, NullLogger<ManualUpdateService>.Instance);
        return new Rig(svc, feed, fetcher, autoLock, backup, folders, events, schtasks);
    }

    private static FeedCheckResult Newer(string version) =>
        new(FeedCheckStatus.Newer, new FeedPackage(version, "Normal", "https://example.invalid/p.zip", new string('a', 64), 10, null), null);

    private static async Task<ManualUpdateJobStatus> RunToEnd(Rig r)
    {
        var outcome = r.Service.Start("T-1", "user-1", "menu");
        Assert.True(outcome.Accepted, $"시작 거부: {outcome.Reason}");
        await r.Service.LastRun;
        return r.Service.GetStatus("T-1")!;
    }

    private int UsageLines(Rig r) => File.Exists(r.Folders.UsageFile) ? File.ReadAllLines(r.Folders.UsageFile).Length : 0;

    // ── G-U2 서명·해시·다운그레이드 ─────────────────────────
    [Fact(DisplayName = "U자기확인 서명불량 피드 → 받기 0 · signature_invalid")]
    public async Task InvalidFeed_NoDownload()
    {
        var r = Build();
        r.Feed.Result = new FeedCheckResult(FeedCheckStatus.Invalid, null, null);
        var s = await RunToEnd(r);
        Assert.Equal(ManualUpdateReasons.SignatureInvalid, s.Reason);
        Assert.Equal(0, r.Fetcher.Downloads);
    }

    [Theory(DisplayName = "U자기확인 피드가 Newer 라 해도 판 ≤ 현재면 거부(다운그레이드 0) · 대조군 높은 판은 통과")]
    [InlineData("1.3.47", false)]
    [InlineData("1.3.48", false)]
    [InlineData("1.3.49", true)]
    public async Task Downgrade_Refused_ServiceRecheck(string feedVersion, bool shouldDownload)
    {
        var r = Build();
        r.Feed.Result = Newer(feedVersion);
        var s = await RunToEnd(r);
        Assert.Equal(shouldDownload ? 1 : 0, r.Fetcher.Downloads);
        if (!shouldDownload) Assert.Equal(ManualUpdateReasons.NoNewerVersion, s.Reason);
    }

    [Fact(DisplayName = "U자기확인 해시 불일치 → 백업·넘기기 0 · hash_mismatch · 받은 파일 삭제")]
    public async Task HashMismatch_NoBackupNoHandOff()
    {
        var r = Build();
        r.Feed.Result = Newer("1.3.49");
        r.Fetcher.HashOk = false;
        var s = await RunToEnd(r);
        Assert.Equal(ManualUpdateReasons.HashMismatch, s.Reason);
        Assert.Equal(0, r.Backup.Runs);
        Assert.False(File.Exists(Path.Combine(r.Folders.StagingDir, "hitpan-1.3.49.zip")));
    }

    // ── G-U4 백업 선행 ─────────────────────────────────────
    [Theory(DisplayName = "U자기확인 백업 실패 → 넘기기 단계 도달 0 · 대조군 백업 성공이면 넘기기 단계 도달")]
    [InlineData(false, ManualUpdateReasons.BackupFailed)]
    // ⬛ 초판 대조군 기대값 launcher_not_wired — I-API 가 런처를 이어 「넘김(handed_off · 사유 없음)」으로 바뀌었다.
    [InlineData(true, null)]
    public async Task BackupGate(bool backupOk, string? expectedReason)
    {
        var r = Build();
        r.Feed.Result = Newer("1.3.49");
        r.Backup.Ok = backupOk;
        var s = await RunToEnd(r);
        Assert.Equal(1, r.Backup.Runs);
        Assert.Equal(expectedReason, s.Reason);
    }

    // ── G-U5 워치독 잠금 ────────────────────────────────────
    [Fact(DisplayName = "U자기확인 워치독 UpdateLockFile 잡힘 → 시작 거부 · 피드 조회 0")]
    public void AutoLockHeld_Refused()
    {
        var r = Build();
        r.Lock.Held = true;
        var o = r.Service.Start("T-1", "user-1", "menu");
        Assert.False(o.Accepted);
        Assert.Equal(ManualUpdateReasons.UpdateInProgress, o.Reason);
        Assert.Equal(0, r.Feed.Calls);
    }

    // ── G-U6 기록 ──────────────────────────────────────────
    [Fact(DisplayName = "U자기확인 넘기기 전 종료 1회 = usage.jsonl 1줄 + 이벤트 1건(28065)")]
    public async Task UsageOneLineOneEvent()
    {
        var r = Build();
        r.Feed.Result = Newer("1.3.49");
        // I-API — 런처가 이어져 넘기기가 성공한다. 「넘기기 전 종료」를 만들려고 작업 등록을 실패시킨다(task_register_failed).
        r.Schtasks.CreateExit = 1;
        var s = await RunToEnd(r);
        Assert.Equal(HitPan.API.Services.LocalSwap.SwapReasons.TaskRegisterFailed, s.Reason);
        Assert.Equal(1, UsageLines(r));
        Assert.Equal(new[] { ManualUsageLog.EventIdRefusedBeforeHandOff }, r.Events);
        var line = File.ReadAllText(r.Folders.UsageFile);
        Assert.Contains("\"requested_by\":\"user-1\"", line);
        Assert.Contains("\"mode\":\"update\"", line);
    }

    // ── G-U1 문(특성 존재) ─────────────────────────────────
    [Fact(DisplayName = "U자기확인 컨트롤러 문 = TenantAdminOnly + MainPcOnly · 대조군 문 없는 형식은 false")]
    public void ControllerDoors()
    {
        static bool HasDoors(Type t) =>
            t.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == "TenantAdminOnly")
            && t.GetCustomAttribute<HitPan.API.Security.MainPcOnlyAttribute>() is not null;

        Assert.True(HasDoors(typeof(ManualUpdateController)));
        Assert.False(HasDoors(typeof(ManualUpdateSelfCheckTests)));
    }

    // ── G-U3·G-U6 글자 검사 ────────────────────────────────
    private static readonly string[] Forbidden =
    {
        "local_update_consents", "WatchdogConsentReader", "back.hitpan.kr", "Backoffice", "Process.Start",
        "INSERT INTO", "UPDATE ", "DELETE FROM", "update-consent", "sc.exe", "New-Service",
    };

    private static List<string> Hits(string text) =>
        Forbidden.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();

    [Fact(DisplayName = "U자기확인 수동 모듈 파일에 동의표·워치독판독기·본사·프로세스·SQL 쓰기 글자 0 · 대조군 주입 문자열은 잡힌다")]
    public void ModuleText_NoForbiddenWords()
    {
        var root = FindRepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src", "HitPan.API", "Services", "ManualUpdate"), "*.cs")
            .Append(Path.Combine(root, "src", "HitPan.API", "Controllers", "ManualUpdateController.cs"))
            .ToList();
        Assert.True(files.Count >= 7, $"파일 수 {files.Count}");

        foreach (var f in files)
        {
            // 주석 줄은 뺀다 — 설명문에 이름이 나오는 것은 호출이 아니다.
            var code = string.Join('\n', File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")));
            var hits = Hits(code);
            Assert.True(hits.Count == 0, $"{Path.GetFileName(f)}: {string.Join(",", hits)}");
        }

        Assert.NotEmpty(Hits("var c = new BackofficeClient(\"https://back.hitpan.kr\");"));
    }

    // ── 계약 §7 · I-WEB 발견 §5-1 — 수동 업데이트 확인도 마지막 교체 결과(last)를 싣는다 ──
    [Fact(DisplayName = "I-API I-WEB§5-1 넘긴 뒤 「최신 버전 확인」에 last(update · 1.3.48→1.3.49 · requested) · 대조군 교체 기록 없으면 last 없음")]
    public async Task Check_CarriesLast_AfterHandOff()
    {
        var r = Build();
        r.Feed.Result = Newer("1.3.49");

        var before = await r.Service.CheckAsync(CancellationToken.None);
        Assert.Null(before.Last);

        var s = await RunToEnd(r);
        Assert.Equal(ManualUpdateStages.HandedOff, s.Stage);

        var after = await r.Service.CheckAsync(CancellationToken.None);
        Assert.NotNull(after.Last);
        Assert.Equal(HitPan.API.Services.LocalSwap.SwapModes.Update, after.Last!.Mode);
        Assert.Equal(HitPan.API.Services.LocalSwap.SwapStates.Requested, after.Last.State);
        Assert.Equal(Current, after.Last.From);
        Assert.Equal("1.3.49", after.Last.To);
    }

    private static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.API"))) return d.FullName;
        throw new DirectoryNotFoundException("레포 뿌리를 못 찾았다");
    }
}
