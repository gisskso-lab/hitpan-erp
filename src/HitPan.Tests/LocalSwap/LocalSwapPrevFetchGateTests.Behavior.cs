using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N4c — 「세 번째 길」 G-NP1~G-NP9 동작 시험(설계 §19-2 · 19-6 · N2 개발명세서 §8 공개 표면).
/// </summary>
/// <remarks>
/// <para>🔴 NCP 실접속 0 — 받기는 실제 어댑터(<see cref="WatchdogUpdateCoreAdapter"/> 기존 두 인자 생성자)를 쓰되
/// <see cref="IHttpClientFactory"/> 는 요청을 손에 쥐는 처리기(<see cref="LocalSwapManifestKeepGateTests.RecordingHandler"/>)만 준다.
/// 밑단 소켓 처리기가 없어 처리기 밖으로 나갈 길이 코드상 없다. 받는 주소도 <c>.invalid</c>(풀리지 않는 이름).</para>
/// <para>저장본 창구(<see cref="IPreviousPackageFeed"/>)는 가짜 — 서명 판정은 N1 쪽 G-NK 가 맡는다(19-3 계약: 저장본 없음·서명 불량·판 다름 = null).</para>
/// <para>DB 무접촉 · 시험 키·db.conf 무접촉 · 모든 도움 이름은 <c>Np</c> 로 시작(같은 partial 의 다른 파일과 이름 겹침 방지).</para>
/// </remarks>
public sealed partial class LocalSwapPrevFetchGateTests
{
    // ── 도움 ─────────────────────────────────────────────────────────────

    private const string NpCurrent = "1.3.51";
    private const string NpP = "1.3.50";
    private const string NpUser = "np-user-7781";
    private const string NpUrl = "https://updates.invalid/files/hitpan-1.3.50.zip";

    private static byte[] NpZip(string tag)
    {
        using var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = za.CreateEntry("api/version.txt");
            using var w = new StreamWriter(e.Open());
            w.Write(tag);
        }
        return ms.ToArray();
    }

    private static string NpSha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static FeedPackage NpPackage(string version, byte[] bytes, bool upperSha = false) =>
        new(version, "Normal", NpUrl, upperSha ? NpSha(bytes).ToUpperInvariant() : NpSha(bytes), bytes.Length, null);

    /// <summary>같은 길이 · 다른 sha(가운데 한 바이트 뒤집기).</summary>
    private static byte[] NpFlip(byte[] b)
    {
        var c = (byte[])b.Clone();
        c[c.Length / 2] ^= 0xFF;
        return c;
    }

    private static HttpResponseMessage NpOk(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class NpFeed : IPreviousPackageFeed
    {
        public Dictionary<string, FeedPackage?> Packages { get; } = new(StringComparer.Ordinal);

        /// <summary>참이면 묻는 판 그대로 「있다」고 답한다 — T2·T3 만 막는지 재려고(저장본이 있어도 T2·T3 가 막아야 한다).</summary>
        public bool AnyVersion { get; set; }

        public int Calls;

        public FeedPackage? LoadVerified(string version)
        {
            Interlocked.Increment(ref Calls);
            if (AnyVersion && Packages.Values.FirstOrDefault(p => p is not null) is { } any) return any with { Version = version };
            return Packages.TryGetValue(version, out var p) ? p : null;
        }
    }

    private sealed class NpLock : IAutoUpdateLockProbe
    {
        private int _n;
        public Func<int, bool>? Answer { get; set; }
        public bool IsAutoUpdateInProgress() => Answer?.Invoke(Interlocked.Increment(ref _n)) ?? false;
    }

    /// <summary>예약은 한 주인만 · 바쁨은 <see cref="Busy"/> 또는 남의 예약 · Launch 는 입력을 적고 정해진 결과.</summary>
    private sealed class NpLauncher : ILocalSwapLauncher
    {
        private readonly object _lock = new();
        public string? Holder { get; private set; }
        public string? Busy { get; set; }
        public List<SwapLaunchInput> Launches { get; } = new();
        public Func<SwapLaunchInput, SwapLaunchResult> OnLaunch { get; set; } = i => new SwapLaunchResult(true, SwapReasons.Ok, i.Ticket);

        public string? CheckReady() => null;
        public string? CheckBusy() => CheckBusy(null);

        public string? CheckBusy(string? owner)
        {
            lock (_lock)
                return Busy ?? (Holder is not null && Holder != owner ? SwapReasons.SwapInProgress : null);
        }

        public SwapRequest? ReadLast() => null;

        public SwapLaunchResult Launch(SwapLaunchInput input)
        {
            lock (_lock) Launches.Add(input);
            return OnLaunch(input);
        }

        public bool TryReserve(string owner)
        {
            lock (_lock)
            {
                if (Holder is not null && Holder != owner) return false;
                Holder = owner;
                return true;
            }
        }

        public void Release(string owner)
        {
            lock (_lock)
                if (Holder == owner) Holder = null;
        }
    }

    /// <summary>실제 어댑터를 감싼다 — 받기·해시는 원본 그대로 · 부를 때마다 받기 단계를 적는다 · 공간 답만 바꿀 수 있다.</summary>
    private sealed class NpFetcher(IPackageFetcher inner, Action note) : IPackageFetcher
    {
        public int SpaceCalls;
        public int Downloads;
        public Func<int, bool>? Space { get; set; }

        public bool HasEnoughSpace(long packageSizeBytes, string targetDir)
        {
            var n = Interlocked.Increment(ref SpaceCalls);
            return Space?.Invoke(n) ?? inner.HasEnoughSpace(packageSizeBytes, targetDir);
        }

        public Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            Interlocked.Increment(ref Downloads);
            note();
            return inner.DownloadAsync(package, targetDir, ct);
        }

        public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct)
        {
            note();
            return inner.VerifySha256Async(filePath, expectedSha256, ct);
        }
    }

    private sealed class NpRig : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hp-np-" + Guid.NewGuid().ToString("N"));
        public FakeSwapEnvironment Env { get; }
        public LocalSwapManifestKeepGateTests.RecordingFactory Http { get; } = new();
        public NpFeed Feed { get; } = new();
        public NpLock Lock { get; } = new();
        public NpLauncher Launcher { get; } = new();
        public NpFetcher Fetcher { get; }
        public LocalRollbackService Service { get; }
        public byte[] Zip { get; } = NpZip(NpP);
        public List<string> Stages { get; } = new();

        public NpRig()
        {
            Env = FakeSwapEnvironment.Under(Root);
            Env.CurrentVersion = NpCurrent;
            Fetcher = new NpFetcher(new WatchdogUpdateCoreAdapter(Http, NullLoggerFactory.Instance), Note);
            Service = new LocalRollbackService(Launcher, Env, NullLogger<LocalRollbackService>.Instance, Feed, Fetcher, Lock,
                lifetime: null, reservationRenewInterval: TimeSpan.Zero);
            Launcher.OnLaunch = i =>
            {
                Note();
                return new SwapLaunchResult(true, SwapReasons.Ok, i.Ticket);
            };
            Ledger(NpP, NpCurrent);
            Feed.Packages[NpP] = NpPackage(NpP, Zip, upperSha: true);
            var zip = Zip;
            Http.Handler.Respond = (req, _) => Task.FromResult(req.RequestUri!.AbsoluteUri == NpUrl ? NpOk(zip) : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        public string Work => Path.Combine(Env.AppRoot!, LocalSwapLauncher.WorkFolderName);
        public string Staging => new ManualFolders(Env.AppRoot!).StagingDir;
        public string Target => Path.Combine(Staging, "hitpan-" + NpP + ".zip");
        public int Requests { get { lock (Http.Handler.Requests) return Http.Handler.Requests.Count; } }

        public string[] StagingZips() =>
            Directory.Exists(Staging) ? Directory.GetFiles(Staging, "hitpan-*.zip").Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray() : Array.Empty<string>();

        public void Ledger(params string[] versions)
        {
            Directory.CreateDirectory(Work);
            File.WriteAllLines(Path.Combine(Work, LocalSwapLauncher.VersionsSeenFileName),
                versions.Select((v, i) => v + "|2026-09-" + (10 + i).ToString("00") + "T00:00:00Z"));
        }

        public void PutStaging(byte[] bytes)
        {
            Directory.CreateDirectory(Staging);
            File.WriteAllBytes(Target, bytes);
        }

        /// <summary>받기 진행 칸을 적는다 — 받는 중엔 예약이 잡혀 있어 상태 확인은 바쁨으로 끝나고 정리도 돌지 않는다(읽기만).</summary>
        public void Note()
        {
            var s = Service.GetStatus("np-observer").Fetch?.Stage;
            lock (Stages)
                if (s is not null && (Stages.Count == 0 || Stages[^1] != s)) Stages.Add(s);
        }

        /// <summary>상태 확인(열림 확인) → [예] → 받기 일 끝까지 기다림. 끝난 뒤 상태 확인은 부르지 않는다(정리가 돌면 「받은 파일 0」을 가린다).</summary>
        public async Task<SwapLaunchResult> StartAndWaitAsync()
        {
            var st = Service.GetStatus(NpUser);
            Assert.True(st.CanRollback && st.MaterialKind == SwapMaterialKinds.ManualZip, "전제 — 세 번째 길이 서야 한다: " + st.Reason + " / " + st.MaterialKind);
            var r = Service.Start(NpUser, st.Ticket);
            await Service.LastFetchRun;
            return r;
        }

        /// <summary>받기 일 끝의 진행 칸(정리는 끝 단언 뒤에만).</summary>
        public LocalRollbackFetchStatus? FetchNow() => Service.GetStatus("np-observer").Fetch;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("[G-NP] 임시 폴더 정리 실패(시험 결과 무관): " + Root + " — " + ex.Message);
            }
        }
    }

    private static void NpAssertNoOutbound(NpRig rig) =>
        Assert.All(rig.Http.Handler.Requests, r => Assert.Equal("updates.invalid", r.RequestUri!.Host));

    // ── G-NP1 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "N4c G-NP1 🚨 ①② 없음 + T2~T6 참 → CanRollback · 종류 manual_zip · 대상 P · 상태 확인 때 가짜 서버 요청 0 · 받기 0")]
    public void NP1_third_path_opens_without_request()
    {
        using var rig = new NpRig();
        var st = rig.Service.GetStatus(NpUser);
        Assert.True(st.CanRollback, "열려야 한다: " + st.Reason);
        Assert.Equal(SwapReasons.Ok, st.Reason);
        Assert.Equal(SwapMaterialKinds.ManualZip, st.MaterialKind);
        Assert.Equal(NpP, st.TargetVersion);
        Assert.NotNull(st.Ticket);
        Assert.Equal(0, rig.Requests);
        Assert.Equal(0, rig.Fetcher.Downloads);
        Assert.Empty(rig.Launcher.Launches);
        Assert.Empty(rig.StagingZips());
        Assert.True(rig.Feed.Calls >= 1, "저장본을 읽어야 한다(T5)");
    }

    // ── G-NP2 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "N4c G-NP2 🚨 하나씩 빼기 → 지금과 같은 사유 · 번호 0 · 요청 0 · 받기 0")]
    [InlineData("T2_묵은prev", SwapReasons.NoPreviousVersion)]
    [InlineData("T3_이력모름", SwapReasons.NoPreviousVersion)]
    [InlineData("T3_이력끝이지금판아님", SwapReasons.NoPreviousVersion)]
    [InlineData("T4_P가지금판이상", SwapReasons.NoPreviousVersion)]
    [InlineData("연쇄차단", SwapReasons.RollbackChainBlocked)]
    [InlineData("T5_저장본null(없음·서명불량 — 계약상 같은 null)", SwapReasons.NoPreviousVersion)]
    [InlineData("T5_판다름", SwapReasons.NoPreviousVersion)]
    [InlineData("T5_sha형식불량", SwapReasons.NoPreviousVersion)]
    [InlineData("T5_주소없음", SwapReasons.NoPreviousVersion)]
    [InlineData("T6_공간", SwapReasons.DiskLow)]
    public void NP2_each_condition_blocks(string which, string reason)
    {
        using var rig = new NpRig();
        var good = rig.Feed.Packages[NpP]!;
        switch (which)
        {
            case "T2_묵은prev":
                rig.Feed.AnyVersion = true;
                Directory.CreateDirectory(Path.Combine(rig.Work, "prev"));
                File.WriteAllText(Path.Combine(rig.Work, "prev", "replaced-by.txt"), "1.3.49");
                break;
            case "T3_이력모름":
                rig.Feed.AnyVersion = true;
                File.Delete(Path.Combine(rig.Work, LocalSwapLauncher.VersionsSeenFileName));
                break;
            case "T3_이력끝이지금판아님":
                rig.Feed.AnyVersion = true;
                rig.Ledger("1.3.49", NpP);
                break;
            case "T4_P가지금판이상":
                rig.Ledger("1.3.52", NpCurrent);
                rig.Feed.Packages["1.3.52"] = good with { Version = "1.3.52" };
                break;
            case "연쇄차단":
                File.WriteAllText(Path.Combine(rig.Work, RollbackMaterialFinder.ChainMarkFileName), NpCurrent + "|2026-10-01T00:00:00Z");
                break;
            case "T5_저장본null(없음·서명불량 — 계약상 같은 null)":
                rig.Feed.Packages.Clear();
                break;
            case "T5_판다름":
                rig.Feed.Packages[NpP] = good with { Version = "1.3.49" };
                break;
            case "T5_sha형식불량":
                rig.Feed.Packages[NpP] = good with { Sha256 = new string('g', 64) };
                break;
            case "T5_주소없음":
                rig.Feed.Packages[NpP] = good with { DownloadUrl = " " };
                break;
            case "T6_공간":
                rig.Fetcher.Space = _ => false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(which), which, "모르는 경우");
        }

        var st = rig.Service.GetStatus(NpUser);
        Assert.False(st.CanRollback, which + " — 열리면 안 된다(종류 " + st.MaterialKind + " · 대상 " + st.TargetVersion + ")");
        Assert.Equal(reason, st.Reason);
        Assert.Null(st.Ticket);
        Assert.Equal(0, rig.Requests);
        Assert.Equal(0, rig.Fetcher.Downloads);
        Assert.Empty(rig.Launcher.Launches);
    }

    // ── G-NP3 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "N4c G-NP3 🚨 ①/② 있음 + 저장본도 있음 → 종류 prev/staging_zip · Launch 도 그 재료 · 가짜 서버 요청 0 · 받기 0(#30)")]
    [InlineData(SwapMaterialKinds.Prev)]
    [InlineData(SwapMaterialKinds.StagingZip)]
    public async Task NP3_local_material_first(string kind)
    {
        using var rig = new NpRig();
        if (kind == SwapMaterialKinds.Prev)
        {
            var prev = Path.Combine(rig.Work, "prev");
            foreach (var part in new[] { "api", "web", "watchdog" }) Directory.CreateDirectory(Path.Combine(prev, part));
            File.WriteAllText(Path.Combine(prev, "sha256.txt"), "x");
            File.WriteAllText(Path.Combine(prev, "version.txt"), NpP);
            File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), NpCurrent);
        }
        else
        {
            File.WriteAllBytes(Path.Combine(rig.Env.WatchdogStagingDir, "hitpan-" + NpCurrent + ".zip"), NpZip(NpCurrent));
            File.WriteAllBytes(Path.Combine(rig.Env.WatchdogStagingDir, "hitpan-" + NpP + ".zip"), rig.Zip);
        }

        var st = rig.Service.GetStatus(NpUser);
        Assert.True(st.CanRollback, st.Reason);
        Assert.Equal(kind, st.MaterialKind);
        Assert.Equal(NpP, st.TargetVersion);

        var r = rig.Service.Start(NpUser, st.Ticket);
        await rig.Service.LastFetchRun;
        Assert.True(r.Started, r.Reason);
        var launch = Assert.Single(rig.Launcher.Launches);
        Assert.Equal(kind, launch.Material.Kind);
        Assert.Equal(0, rig.Requests);
        Assert.Equal(0, rig.Fetcher.Downloads);
        Assert.Null(rig.Launcher.Holder);
    }

    // ── G-NP4 · G-NP6 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "N4c G-NP4 🚨 받기 성공 → Launch 1 · manual_zip · 경로 manual\\staging\\hitpan-{P}.zip · sha256 소문자 · 단계 downloading→verifying→handing_off→handed_off · 예약 풀림")]
    public async Task NP4_fetch_success()
    {
        using var rig = new NpRig();
        var r = await rig.StartAndWaitAsync();
        Assert.True(r.Started, r.Reason);
        Assert.Equal(SwapReasons.Ok, r.Reason);

        var launch = Assert.Single(rig.Launcher.Launches);
        Assert.Equal(SwapModes.Rollback, launch.Mode);
        Assert.Equal(SwapMaterialKinds.ManualZip, launch.Material.Kind);
        Assert.Equal(Path.GetFullPath(rig.Target), Path.GetFullPath(launch.Material.Path), ignoreCase: true);
        Assert.Equal(NpSha(rig.Zip), launch.Material.Sha256);   // 저장본은 대문자 — 넘길 때 소문자
        Assert.Equal(NpCurrent, launch.From);
        Assert.Equal(NpP, launch.To);
        Assert.Equal(NpUser, launch.RequestedBy);
        Assert.Equal("rollback:" + launch.Ticket, launch.Owner);
        Assert.True(File.Exists(rig.Target));
        Assert.Equal(NpSha(rig.Zip), NpSha(File.ReadAllBytes(rig.Target)));
        Assert.Null(rig.Launcher.Holder);
        Assert.Equal(1, rig.Requests);

        rig.Note();
        Assert.Equal(new[] { ManualUpdateStages.Downloading, ManualUpdateStages.Verifying, ManualUpdateStages.HandingOff, ManualUpdateStages.HandedOff }, rig.Stages);
        Assert.Null(rig.FetchNow()!.Reason);
    }

    [Fact(DisplayName = "N4c G-NP6 🚨 요청 모양(#18/#22) — GET 1 · 주소 = 저장본 downloadUrl 그대로 · 쿼리 0 · 헤더 0 · 본문 0 · 사용자·번호·tenant 글자 0 · 처리기 밖 0")]
    public async Task NP6_request_shape()
    {
        using var rig = new NpRig();
        var r = await rig.StartAndWaitAsync();
        Assert.True(r.Started, r.Reason);

        var req = Assert.Single(rig.Http.Handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal(NpUrl, req.RequestUri!.AbsoluteUri);
        Assert.Equal("", req.RequestUri.Query);
        Assert.Empty(req.Headers);
        Assert.Null(req.Content);
        var whole = req.RequestUri.AbsoluteUri;
        Assert.DoesNotContain(NpUser, whole, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(r.Ticket!, whole, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", whole, StringComparison.OrdinalIgnoreCase);
        NpAssertNoOutbound(rig);
    }

    // ── G-NP5 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "N4c G-NP5 🚨 받기 실패 → 사유 19-3 · Launch 0(런처 거부만 1) · 받은 파일 0 · 예약 풀림 · 단계 refused")]
    [InlineData("404", SwapReasons.NoPreviousVersion)]
    [InlineData("끊김", ManualUpdateReasons.DownloadFailed)]
    [InlineData("sha다름", SwapReasons.HashMismatch)]
    [InlineData("길이다름", SwapReasons.HashMismatch)]
    [InlineData("공간", SwapReasons.DiskLow)]
    [InlineData("자동업데이트표식", SwapReasons.UpdateInProgress)]
    [InlineData("런처거부", "np_launcher_refused")]
    public async Task NP5_fetch_failure(string which, string reason)
    {
        using var rig = new NpRig();
        switch (which)
        {
            case "404":
                rig.Http.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                break;
            case "끊김":
                rig.Http.Handler.Respond = (_, _) => throw new HttpRequestException("np: 연결 끊김");
                break;
            case "sha다름":
                var flipped = NpFlip(rig.Zip);
                rig.Http.Handler.Respond = (_, _) => Task.FromResult(NpOk(flipped));
                break;
            case "길이다름":
                var longer = rig.Zip.Concat(new byte[] { 1, 2, 3 }).ToArray();
                rig.Http.Handler.Respond = (_, _) => Task.FromResult(NpOk(longer));
                break;
            case "공간":
                rig.Fetcher.Space = n => n <= 2;   // 상태 확인 T6(1) · [예] 의 다시 판정 T6(2)은 통과 · 받기 ④(3) 에서 모자람
                break;
            case "자동업데이트표식":
                rig.Lock.Answer = _ => true;
                break;
            case "런처거부":
                rig.Launcher.OnLaunch = _ => SwapLaunchResult.Refuse("np_launcher_refused");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(which), which, "모르는 경우");
        }

        var r = await rig.StartAndWaitAsync();
        Assert.True(r.Started, "받기가 첫 단계라 [예] 는 ok — 실패는 Fetch 로 보인다: " + r.Reason);

        // 끝 상태 확인(정리) 전에 — 받기 일이 스스로 지웠어야 한다
        Assert.Empty(rig.StagingZips());
        Assert.Equal(which == "런처거부" ? 1 : 0, rig.Launcher.Launches.Count);
        Assert.Null(rig.Launcher.Holder);
        NpAssertNoOutbound(rig);

        var f = rig.FetchNow();
        Assert.NotNull(f);
        Assert.Equal(ManualUpdateStages.Refused, f!.Stage);
        Assert.Equal(reason, f.Reason);
        Assert.Equal(NpP, f.To);
    }

    // ── G-NP7 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "N4c G-NP7 🚨 남은 파일 정리 — v ≤ 지금 판만 지움 · 높은 판 그대로 · 교체 열림이면 전부 그대로")]
    [InlineData(false)]
    [InlineData(true)]
    public void NP7_leftover_cleanup(bool busy)
    {
        using var rig = new NpRig();
        rig.Feed.Packages.Clear();   // 세 번째 길 판정이 안 서게(정리 제외 대상 0 — G-NP8 이 따로 잰다)
        Directory.CreateDirectory(rig.Staging);
        foreach (var v in new[] { "1.3.49", NpCurrent, "1.3.52" })
            File.WriteAllBytes(Path.Combine(rig.Staging, "hitpan-" + v + ".zip"), new byte[] { 1 });
        if (busy) rig.Launcher.Busy = SwapReasons.SwapInProgress;

        rig.Service.GetStatus(NpUser);

        Assert.Equal(busy ? new[] { "hitpan-1.3.49.zip", "hitpan-1.3.51.zip", "hitpan-1.3.52.zip" } : new[] { "hitpan-1.3.52.zip" }, rig.StagingZips());
        Assert.Equal(0, rig.Requests);
    }

    // ── G-NP8 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "N4c G-NP8 🚨 넣어 둔 맞는 파일 → 바깥 요청 0 · Launch 1 · 경로·sha 같음 · 단계 downloading 없이 verifying→handing_off→handed_off · 그 전 상태 확인에도 파일 그대로")]
    public async Task NP8_placed_good_file()
    {
        using var rig = new NpRig();
        rig.PutStaging(rig.Zip);

        rig.Service.GetStatus(NpUser);
        rig.Service.GetStatus(NpUser);
        Assert.True(File.Exists(rig.Target), "상태 확인(정리)이 넣어 둔 P 파일을 지웠다(19-6 보정)");

        var r = await rig.StartAndWaitAsync();
        Assert.True(r.Started, r.Reason);
        Assert.Equal(0, rig.Requests);
        Assert.Equal(0, rig.Fetcher.Downloads);
        var launch = Assert.Single(rig.Launcher.Launches);
        Assert.Equal(SwapMaterialKinds.ManualZip, launch.Material.Kind);
        Assert.Equal(Path.GetFullPath(rig.Target), Path.GetFullPath(launch.Material.Path), ignoreCase: true);
        Assert.Equal(NpSha(rig.Zip), launch.Material.Sha256);
        Assert.Equal(NpSha(rig.Zip), NpSha(File.ReadAllBytes(rig.Target)));
        Assert.Null(rig.Launcher.Holder);

        rig.Note();
        Assert.DoesNotContain(ManualUpdateStages.Downloading, rig.Stages);
        Assert.Equal(new[] { ManualUpdateStages.Verifying, ManualUpdateStages.HandingOff, ManualUpdateStages.HandedOff }, rig.Stages);
    }

    // ── G-NP9 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "N4c G-NP9 🚨 넣어 둔 안 맞는 파일 → 안 씀 · 지워짐 · 서버 끊김이면 download_failed·Launch 0 / 서버가 맞는 zip 이면 받은 것으로 Launch")]
    [InlineData("sha다름", false)]
    [InlineData("sha다름", true)]
    [InlineData("길이다름", false)]
    [InlineData("길이다름", true)]
    public async Task NP9_placed_bad_file(string which, bool serverGood)
    {
        using var rig = new NpRig();
        var placed = which == "sha다름" ? NpFlip(rig.Zip) : rig.Zip.Concat(new byte[] { 9 }).ToArray();
        rig.PutStaging(placed);
        if (!serverGood)
            rig.Http.Handler.Respond = (_, _) => throw new HttpRequestException("np: 연결 끊김");

        var r = await rig.StartAndWaitAsync();
        Assert.True(r.Started, r.Reason);

        if (serverGood)
        {
            var launch = Assert.Single(rig.Launcher.Launches);
            Assert.Equal(1, rig.Requests);
            Assert.Equal(NpSha(rig.Zip), launch.Material.Sha256);
            Assert.Equal(NpSha(rig.Zip), NpSha(File.ReadAllBytes(rig.Target)));   // 넣어 둔 것이 아니라 받은 것
        }
        else
        {
            Assert.Empty(rig.Launcher.Launches);
            Assert.Empty(rig.StagingZips());   // 넣어 둔 안 맞는 파일 지워짐
            var f = rig.FetchNow();
            Assert.Equal(ManualUpdateStages.Refused, f!.Stage);
            Assert.Equal(ManualUpdateReasons.DownloadFailed, f.Reason);
        }
        Assert.Null(rig.Launcher.Holder);
        NpAssertNoOutbound(rig);
    }
}
