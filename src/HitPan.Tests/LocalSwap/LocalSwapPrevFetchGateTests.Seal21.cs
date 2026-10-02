using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N5 — 병렬이슈 21 보강 게이트 G-NP10 · G-NW4(설계 §19-7 · 작업지시서 18-8 X-9).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 판 이력에 빈칸이 있으면(기동 기록 실패) 세 번째 길이 「직전 판」 대신 <b>두 판 뒤</b>를 받아 되돌린다(이슈 21).
/// R-21b(저장본을 남길 때 판 == 지금 판이면 기존 기록기로 빈칸 메우기) · R-21c(일꾼도 「이력 모름 = 거부」)가 막는다.</para>
/// <para>🟢 서명 대신 <see cref="IPreviousPackageFeed"/> 가짜(저장본 글자를 그대로 읽는다 — 서명 재확인은 어댑터 몫이고 이 게이트의 축이 아니다)
/// · 받기는 가짜 서버(<see cref="IPackageFetcher"/> — 요청 주소만 손에 쥐고, 실제 받는 함수처럼 <c>{받는 폴더}\hitpan-{판}.zip</c> 에 쓴다) — NCP 실접속 0.</para>
/// <para>G-NW4 를 이 클래스에 둔 이유: 일꾼 시험 클래스(<c>LocalSwapPrevFetchWorkerGateTests</c>)는 partial 이 아니고 다른 갈래(N4b)가 그 파일을
/// 고치는 중이라 열지 않았다 — 같은 GATES 줄의 이 클래스(세 번째 길)에 붙였다(개발명세서 N5 §6).</para>
/// </remarks>
public sealed partial class LocalSwapPrevFetchGateTests
{
    // ── 공용 대역(N5 · 이름 앞 S21 — 다른 갈래 partial 과 이름 겹침 0) ──

    /// <summary>저장본 글자를 그대로 읽는 창구(서명 재확인 대역). 저장본 = <see cref="S21Manifest"/> 모양.</summary>
    internal sealed class S21KeeperFeed : IPreviousPackageFeed
    {
        private readonly SignedManifestKeeper _keeper;
        public S21KeeperFeed(SignedManifestKeeper keeper) => _keeper = keeper;

        public FeedPackage? LoadVerified(string version)
        {
            var text = _keeper.Read(version);
            if (text is null) return null;
            var o = JsonNode.Parse(text)!.AsObject();
            return new FeedPackage((string)o["version"]!, "Normal", (string)o["downloadUrl"]!, (string)o["sha256"]!, (long)o["sizeBytes"]!, null);
        }
    }

    /// <summary>가짜 파일 서버 — 요청 주소를 손에 쥐고 <see cref="Payload"/> 를 받는 폴더에 쓴다(실제 UpdateClient 와 같은 이름 규칙).</summary>
    internal sealed class S21Server : IPackageFetcher
    {
        public byte[] Payload { get; init; } = Array.Empty<byte>();
        public List<string> Urls { get; } = new();

        public bool HasEnoughSpace(long packageSizeBytes, string targetDir) => true;

        public async Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
        {
            lock (Urls) Urls.Add(package.DownloadUrl);
            Directory.CreateDirectory(targetDir);
            var path = Path.Combine(targetDir, "hitpan-" + package.Version + ".zip");
            await File.WriteAllBytesAsync(path, Payload, ct);
            return path;
        }

        public async Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            return string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class S21NoLock : IAutoUpdateLockProbe
    {
        public bool IsAutoUpdateInProgress() => false;
    }

    /// <summary>시험 한 판 — 설치 폴더 · 저장본 · 가짜 서버 · 세 번째 길이 선 서비스.</summary>
    internal sealed class S21Bench : IDisposable
    {
        public string Root { get; }
        public FakeSwapEnvironment Env { get; }
        public HookedFolderGuard Guard { get; } = new();
        public SignedManifestKeeper Keeper { get; }
        public S21Server Server { get; }
        public LocalSwapLauncher Launcher { get; }
        public LocalRollbackService Service { get; }

        /// <param name="fill">true = 운영 생성자(R-21b 켜짐) · false = 기존 3인자 생성자(R-21b 꺼짐 = 대조군).</param>
        public S21Bench(string current, bool fill)
        {
            Root = Path.Combine(Path.GetTempPath(), "hp-n5-" + Guid.NewGuid().ToString("N"));
            Env = FakeSwapEnvironment.Under(Root);
            Env.CurrentVersion = current;
            Keeper = fill
                ? new SignedManifestKeeper(Env, Guard, NullLogger<SignedManifestKeeper>.Instance, NullLoggerFactory.Instance)
                : new SignedManifestKeeper(Env, Guard, NullLogger<SignedManifestKeeper>.Instance);
            var zip = Path.Combine(Root, "payload", "p.zip");
            LocalSwapWorkerRig.MakeZip(zip, "0.0.0");
            Server = new S21Server { Payload = File.ReadAllBytes(zip) };
            Launcher = new LocalSwapLauncher(Env, new FakeSchtasks(), Guard, NullLogger<LocalSwapLauncher>.Instance);
            Service = new LocalRollbackService(Launcher, Env, NullLogger<LocalRollbackService>.Instance,
                new S21KeeperFeed(Keeper), Server, new S21NoLock(), null, TimeSpan.Zero);
        }

        public string Work => Path.Combine(Env.AppRoot!, LocalSwapLauncher.WorkFolderName);
        public string Ledger => Path.Combine(Work, LocalSwapLauncher.VersionsSeenFileName);

        /// <summary>저장본 글자 — 주소 = <c>https://feed.invalid/hitpan-{v}.zip</c> · sha·크기 = 가짜 서버가 줄 파일.</summary>
        public string S21Manifest(string v) => JsonSerializer.Serialize(new
        {
            version = v,
            downloadUrl = "https://feed.invalid/hitpan-" + v + ".zip",
            sha256 = Convert.ToHexString(SHA256.HashData(Server.Payload)).ToLowerInvariant(),
            sizeBytes = (long)Server.Payload.Length,
        });

        /// <summary>판 이력을 이 판들로 새로 쓴다(<c>M.m.b|UTC</c> · \r\n).</summary>
        public void WriteLedger(params string[] versions)
        {
            Directory.CreateDirectory(Work);
            var t = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(Ledger, string.Concat(versions.Select((v, i) => v + "|" + t.AddDays(i).ToString("o") + "\r\n")));
        }

        /// <summary>읽히는 줄의 판(읽는 쪽과 같은 규칙 — 두 칸 · 판 모양).</summary>
        public string[] LedgerVersions() => File.Exists(Ledger)
            ? File.ReadAllLines(Ledger).Select(l => l.Trim().Split('|')).Where(c => c.Length == 2 && RollbackMaterialFinder.TryParse(c[0], out _))
                .Select(c => c[0]).ToArray()
            : Array.Empty<string>();

        /// <summary>기동 한 번(기존 기록기 · 이 판의 문지기로).</summary>
        public Task Boot(string version, ISwapFolderGuard guard)
        {
            Env.CurrentVersion = version;
            return new InstalledVersionLedger(Env, guard, NullLogger<InstalledVersionLedger>.Instance).StartAsync(CancellationToken.None);
        }

        /// <summary>상태 → [예] → 받기 일 끝까지. 반환 = (상태, 시작 결과, 요청서 · 없으면 null).</summary>
        public async Task<(LocalRollbackStatus Status, SwapLaunchResult? Start, SwapRequest? Request)> RollbackThroughFetch()
        {
            var status = Service.GetStatus("gate-user");
            if (!status.CanRollback) return (status, null, null);
            var start = Service.Start("gate-user", status.Ticket);
            await Service.LastFetchRun.WaitAsync(TimeSpan.FromSeconds(30));
            return (status, start, Launcher.ReadLast());
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException ex) { Console.Error.WriteLine("[N5] 임시 폴더 정리 실패(시험 결과 무관): " + Root + " — " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Console.Error.WriteLine("[N5] 임시 폴더 정리 실패(시험 결과 무관): " + Root + " — " + ex.Message); }
        }
    }

    /// <summary>기동 기록이 실패한 판 — 기록기 문지기가 던진다(병렬이슈 01 과 같은 거부 모양).</summary>
    private sealed class S21RefusingGuard : ISwapFolderGuard
    {
        public void EnsureSafe(string path) => throw new InvalidOperationException("시험 — 작업 폴더 거부(기동 기록 실패 주입)");
    }

    /// <summary>
    /// 이력 50 → 51 기동(기록 실패) → 51 에서 저장본 남김 → 52 기동 → 되돌리기. 반환 = (받기 요청 주소들, 요청서, 이력).
    /// </summary>
    private static async Task<(string[] Urls, SwapRequest? Request, string[] Ledger, string Reason)> S21GapThenTwoUp(bool fill)
    {
        using var b = new S21Bench("1.3.50", fill);
        b.WriteLedger("1.3.50");
        Assert.True(b.Keeper.Keep("1.3.50", b.S21Manifest("1.3.50")), "50 저장본 남기기 실패");

        await b.Boot("1.3.51", new S21RefusingGuard());
        Assert.Equal(new[] { "1.3.50" }, b.LedgerVersions());          // 전제: 51 기동 기록이 실제로 실패했다(빈칸)
        Assert.True(b.Keeper.Keep("1.3.51", b.S21Manifest("1.3.51")), "51 저장본 남기기 실패");

        await b.Boot("1.3.52", b.Guard);
        var (status, start, request) = await b.RollbackThroughFetch();
        string[] urls;
        lock (b.Server.Urls) urls = b.Server.Urls.ToArray();
        return (urls, request, b.LedgerVersions(), status.Reason + "/" + start?.Reason);
    }

    // ── G-NP10 — 메우기 뒤 두 판 뒤 안 감 ──

    [Fact(DisplayName = "N5 G-NP10 🚨 이력 50 → 51 기동 기록 실패 → 51 에서 저장본 남김(메우기) → 52 → 되돌리기 P = 51 · 받기 주소 = 51 저장본 downloadUrl")]
    public async Task Np10_fill_then_previous_is_one_back()
    {
        var (urls, request, ledger, reason) = await S21GapThenTwoUp(fill: true);
        Assert.Equal(new[] { "1.3.50", "1.3.51", "1.3.52" }, ledger);
        var url = Assert.Single(urls);
        Assert.Equal("https://feed.invalid/hitpan-1.3.51.zip", url);
        Assert.True(request is not null, "요청서가 안 적혔다 — " + reason);
        Assert.Equal("1.3.51", request!.To);
        Assert.Equal(SwapMaterialKinds.ManualZip, request.Material?.Kind);
        Assert.EndsWith("hitpan-1.3.51.zip", request.Material?.Path ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "N5 G-NP10 대조군 🔴 R-21b 끈 저장본 담당(기존 3인자 생성자) → 같은 길에서 P = 50(두 판 뒤 · 게이트가 FAIL 을 낸다)")]
    public async Task Np10_control_no_fill_goes_two_back()
    {
        var (urls, request, ledger, reason) = await S21GapThenTwoUp(fill: false);
        Assert.Equal(new[] { "1.3.50", "1.3.52" }, ledger);
        var url = Assert.Single(urls);
        Assert.Equal("https://feed.invalid/hitpan-1.3.50.zip", url);
        Assert.True(request is not null, "대조군인데 요청서가 안 적혔다 — " + reason);
        Assert.Equal("1.3.50", request!.To);
    }

    // ── G-NW4 — 일꾼 이력 모름 거부(R-21c) ──

    /// <summary>R-21c 머리(<c>Initialize-Material</c> · staging_zip 재확인 뒤) — 대조 사본이 찾는 글자(기대값 아님).</summary>
    public const string S21WorkerHead = "if ($mat.kind -eq 'manual_zip' -and $Mode -eq 'rollback') {";

    private static string S21Sub(string original, string line, string with)
    {
        var n = 0;
        for (var i = original.IndexOf(line, StringComparison.Ordinal); i >= 0; i = original.IndexOf(line, i + line.Length, StringComparison.Ordinal)) n++;
        Assert.True(n == 1, "R-21c 머리 줄이 " + n + "번 있다(1번이어야 한다) — 대조군을 같이 고쳐라");
        return original.Replace(line, with, StringComparison.Ordinal);
    }

    // 🔴 같은 줄에 본문(`{` 뒤)이 이어지므로 `#` 줄 주석 대신 `<# … #>` 블록 주석(10/2 실제 사고).
    public static string S21ControlNoR21c() =>
        S21Sub(LocalSwapWorkerRig.OriginalScript(), S21WorkerHead, "<# (control) R-21c off #> if ($false) {");

    /// <summary>되돌리기 1.3.48 → 1.3.47 · 재료 = <c>{app}\manual\staging\hitpan-1.3.47.zip</c>(경로·이름·sha 모양 맞음 = p1·p2 통과).</summary>
    private static LocalSwapWorkerRig S21ManualZipRig()
    {
        var rig = LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow);
        var zip = Path.Combine(rig.ManualStaging, "hitpan-" + LocalSwapWorkerRig.To + ".zip");
        LocalSwapWorkerRig.MakeZip(zip, LocalSwapWorkerRig.To);
        rig.SetRequestField("material", new JsonObject
        {
            ["kind"] = SwapMaterialKinds.ManualZip,
            ["path"] = zip,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),
        });
        return rig;
    }

    private static void S21WriteSeen(LocalSwapWorkerRig rig, params string[] versions)
    {
        var t = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(rig.SeenPath, string.Concat(versions.Select((v, i) => v + "|" + t.AddDays(i).ToString("o") + "\r\n")));
    }

    private static string S21Why(LocalSwapWorkerRig rig) => rig.Log() + "\n--- calls ---\n" + rig.Calls();

    [Theory(DisplayName = "N5 G-NW4 🚨 되돌리기 + manual_zip · ① 이력 없음 ② 마지막 ≠ from ③ 바로 위 ≠ to → refused·material_invalid · 프로그램 폴더·stage 무변화")]
    [InlineData("none")]
    [InlineData("last")]
    [InlineData("above")]
    public void Nw4_history_unknown_refused(string which)
    {
        using var rig = S21ManualZipRig();
        switch (which)
        {
            case "none": break;                                                        // ① 이력 파일 없음
            case "last": S21WriteSeen(rig, "1.3.46", "1.3.47"); break;                 // ② 마지막 1.3.47 ≠ from 1.3.48
            case "above": S21WriteSeen(rig, "1.3.46", "1.3.48"); break;                // ③ 바로 위 1.3.46 ≠ to 1.3.47
            default: throw new ArgumentException(which);
        }
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.MaterialInvalid,
            which + ": " + final.State + "/" + final.Reason + " / " + S21Why(rig));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "watchdog"));
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Path.Combine(rig.App, p + ".rbk")), which + ": " + p + ".rbk 가 생겼다 / " + S21Why(rig));
        Assert.False(Directory.Exists(Path.Combine(rig.Work, "stage", LocalSwapWorkerRig.To)), which + ": stage 에 풀었다 / " + S21Why(rig));
        // ①② 는 R-21c 만 막는다(기존 봉합 05 재확인은 이력 없음·마지막 ≠ 지금이면 통과시킨다 — 1.3.48 비상 경로).
        // ③ 은 기존 봉합 05(`Get-StagingZipRefusal` · 같은 규칙)가 먼저 막는다 — R-21c 는 그 뒤 두 번째 벽(실측 10/2 · 개발명세서 N5 §5).
        if (which != "above")
            Assert.True(rig.CountLog("S1 fetched zip refused") >= 1, which + ": R-21c 가 아닌 다른 줄이 거부했다 / " + S21Why(rig));
        else
            Assert.True(rig.CountLog("S1 staging zip refused") + rig.CountLog("S1 fetched zip refused") >= 1, which + ": 이력 규칙이 아닌 다른 줄이 거부했다 / " + S21Why(rig));
    }

    [Fact(DisplayName = "N5 G-NW4 🚨 되돌리기 + manual_zip · 이력 마지막 = from · 바로 위 = to → R-21c 통과 · S1 재료 준비")]
    public void Nw4_history_matches_passes()
    {
        using var rig = S21ManualZipRig();
        S21WriteSeen(rig, "1.3.46", "1.3.47", "1.3.48", "1.3.48");
        Assert.Equal(0, rig.Run());
        Assert.Equal(0, rig.CountLog("S1 fetched zip refused"));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "이력이 맞는데 S1 재료 준비까지 안 갔다 / " + rig.Final().State + "/" + rig.Final().Reason + " / " + S21Why(rig));
    }

    [Fact(DisplayName = "N5 G-NW4 🚨 staging_zip 같은 조건(이력 없음) → 지금대로 S1 재료 준비(1.3.48 비상 경로 보존 · R-21c 무관)")]
    public void Nw4_staging_zip_without_history_unchanged()
    {
        using var rig = LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow);
        Assert.False(File.Exists(rig.SeenPath));
        Assert.Equal(0, rig.Run());
        Assert.Equal(0, rig.CountLog("S1 fetched zip refused"));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "staging_zip 비상 경로가 S1 에서 막혔다 / " + rig.Final().State + "/" + rig.Final().Reason + " / " + S21Why(rig));
    }

    [Theory(DisplayName = "N5 G-NW4 대조군 🔴 R-21c 블록을 if ($false) 로 끈 사본 → ① 이력 없음 ② 마지막 ≠ from 인데 풀기까지 간다(게이트가 FAIL 을 낸다)")]
    [InlineData("none")]
    [InlineData("last")]
    public void Nw4_control_r21c_off_extracts(string which)
    {
        var script = S21ControlNoR21c();
        using var rig = S21ManualZipRig();
        if (which == "last") S21WriteSeen(rig, "1.3.46", "1.3.47");
        Assert.Equal(0, rig.Run(script));
        Assert.Equal(0, rig.CountLog("S1 fetched zip refused"));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "대조군인데 S1 재료 준비까지 안 갔다 — R-21c 아닌 다른 줄이 막고 있다 / " + rig.Final().State + "/" + rig.Final().Reason + " / " + S21Why(rig));
    }
}
