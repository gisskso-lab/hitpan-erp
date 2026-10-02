using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N6 — G-NF1 첫 회 NCP 확인표 받기(설계 §19-8 · 작업지시서 18-9 X-12).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 1.3.50 의 판 이력은 한 줄로 태어나 직전 판을 모른다 ⇒ 첫 회 [되돌리기]가 서지 않는다.
/// 첫 회만 지금 판 서명 manifest 의 받는 주소 이웃(<c>hitpan-{지금}.previous-manifest.json</c>)에서 직전 게시본 확인표를 받아
/// 기존 검증기로 본 뒤 기존 저장본 담당에 남기고, T3 은 「이력이 막 태어남 + 저장본 아래 정확히 1개」로 그 판을 쓴다.</para>
/// <para>🟢 NCP 실접속 0 — 모든 요청은 <see cref="RecordingHandler"/> 가 손에 쥔다. 서명 = N4b 시험 키(<see cref="N4bTestKey"/> · 병렬 끈 컬렉션 · db.conf finally 원상).
/// 받기는 N5 가짜 서버(<see cref="LocalSwapPrevFetchGateTests.S21Server"/>).</para>
/// <para>대조(Edit 도구로 제품을 바꿔 돌림 → 원복 · 개발명세서 N6 §4): ① <c>LocalRollbackService</c> T3 의 <c>&amp;&amp; !SignedManifestKeeper.TryFirstRunPrevious(…)</c> 뺌 →
/// 「ok」 에서 저장은 되나 <c>no_previous_version</c> ② <c>TryFetchFirstPrevious</c> 의 <c>Verify</c> 줄 뺌 → 「sig」 가 저장됨.</para>
/// </remarks>
public sealed partial class LocalSwapManifestKeepGateTests
{
    private const string N6Current = "1.3.50";
    private const string N6Prev = "1.3.49";
    private const string N6FeedZipUrl = "https://updates.hitpan.kr/packages/hitpan-1.3.50.zip";
    private const string N6PrevManifestUrl = "https://updates.hitpan.kr/packages/hitpan-1.3.50.previous-manifest.json";

    private sealed class N6NoLock : IAutoUpdateLockProbe
    {
        public bool IsAutoUpdateInProgress() => false;
    }

    /// <summary>시험 한 판 — 설치 폴더 · 운영 생성자 저장본 담당 · 실제 어댑터(피드·저장본 확인) · 가짜 받기 서버 · 되돌리기 서비스.</summary>
    private sealed class N6Bench : IDisposable
    {
        public string Root { get; }
        public FakeSwapEnvironment Env { get; }
        public HookedFolderGuard Guard { get; } = new();
        public SignedManifestKeeper Keeper { get; }
        public RecordingFactory Http { get; } = new();
        public WatchdogUpdateCoreAdapter Adapter { get; }
        public LocalSwapPrevFetchGateTests.S21Server Server { get; }
        public LocalSwapLauncher Launcher { get; }
        public LocalRollbackService Service { get; }

        public N6Bench()
        {
            Root = Path.Combine(Path.GetTempPath(), "hp-n6-" + Guid.NewGuid().ToString("N"));
            Env = FakeSwapEnvironment.Under(Root);
            Env.CurrentVersion = N6Current;
            Keeper = new SignedManifestKeeper(Env, Guard, NullLogger<SignedManifestKeeper>.Instance, NullLoggerFactory.Instance);
            Adapter = new WatchdogUpdateCoreAdapter(Http, NullLoggerFactory.Instance, Keeper);
            var zip = Path.Combine(Root, "payload", "p.zip");
            LocalSwapWorkerRig.MakeZip(zip, "0.0.0");
            Server = new LocalSwapPrevFetchGateTests.S21Server { Payload = File.ReadAllBytes(zip) };
            Launcher = new LocalSwapLauncher(Env, new FakeSchtasks(), Guard, NullLogger<LocalSwapLauncher>.Instance);
            Service = new LocalRollbackService(Launcher, Env, NullLogger<LocalRollbackService>.Instance,
                Adapter, Server, new N6NoLock(), null, TimeSpan.Zero);
        }

        public string Work => Path.Combine(Env.AppRoot!, LocalSwapLauncher.WorkFolderName);

        public void WriteLedger(params string[] versions)
        {
            Directory.CreateDirectory(Work);
            var t = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(Path.Combine(Work, LocalSwapLauncher.VersionsSeenFileName),
                string.Concat(versions.Select((v, i) => v + "|" + t.AddDays(i).ToString("o") + "\r\n")));
        }

        /// <summary>서명 전 manifest — sha·크기 = 가짜 서버가 줄 파일.</summary>
        public UpdateManifest Unsigned(string version, string downloadUrl) => Plain(version) with
        {
            DownloadUrl = downloadUrl,
            Sha256 = Convert.ToHexString(SHA256.HashData(Server.Payload)).ToLowerInvariant(),
            SizeBytes = Server.Payload.Length,
        };

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException ex) { Console.Error.WriteLine("[N6] 임시 폴더 정리 실패(시험 결과 무관): " + Root + " — " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Console.Error.WriteLine("[N6] 임시 폴더 정리 실패(시험 결과 무관): " + Root + " — " + ex.Message); }
        }
    }

    private static HttpResponseMessage N6Json(UpdateManifest m) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(m, FeedJson), Encoding.UTF8, "application/json") };

    /// <summary>서명 첫 글자 하나만 바꾼다(끝 글자는 덧채움 비트라 같은 바이트가 될 수 있다).</summary>
    private static UpdateManifest N6Tamper(UpdateManifest m) =>
        m with { Signature = (m.Signature![0] == 'A' ? "B" : "A") + m.Signature[1..] };

    [Theory(DisplayName = "N6 G-NF1 🚨 첫 회(이력 1.3.50 한 줄 · 저장본 아래 0) → 피드 확인이 …/packages/hitpan-1.3.50.previous-manifest.json 을 받아 1.3.49 저장 → 되돌리기 재료 manual_zip·hitpan-1.3.49.zip · ⓐ서명 ⓑ판≥지금 ⓔ404 = 저장 0·no_previous_version · ⓒ이력 두 줄 ⓓ주소 꼴 다름 = 추가 요청 0")]
    [InlineData("ok")]
    [InlineData("sig")]       // ⓐ 서명 한 글자 바꿈
    [InlineData("inside50")]  // ⓑ 안의 판 == 지금
    [InlineData("inside51")]  // ⓑ 안의 판 > 지금
    [InlineData("ledger2")]   // ⓒ 이력 1.3.49,1.3.50 두 줄
    [InlineData("url")]       // ⓓ downloadUrl 마지막 조각이 다름
    [InlineData("404")]       // ⓔ NCP 에 파일 없음
    public async Task Nf1_first_run_fetches_previous_manifest(string which)
    {
        using var b = new N6Bench();
        await N4bTestKey.WithKeyAsync(async () =>
        {
            b.WriteLedger(which == "ledger2" ? new[] { N6Prev, N6Current } : new[] { N6Current });
            var feed = N4bTestKey.Signed(b.Unsigned(N6Current,
                which == "url" ? "https://updates.hitpan.kr/packages/hitpan-1.3.50-full.zip" : N6FeedZipUrl));
            var prevVersion = which switch { "inside50" => N6Current, "inside51" => "1.3.51", _ => N6Prev };
            var prev = N4bTestKey.Signed(b.Unsigned(prevVersion, "https://updates.hitpan.kr/packages/hitpan-" + prevVersion + ".zip"));
            if (which == "sig") prev = N6Tamper(prev);
            b.Http.Handler.Respond = (req, _) => Task.FromResult(
                req.RequestUri!.AbsolutePath.EndsWith(".previous-manifest.json", StringComparison.Ordinal)
                    ? (which == "404" ? new HttpResponseMessage(HttpStatusCode.NotFound) : N6Json(prev))
                    : N6Json(feed));

            var r = await b.Adapter.CheckAsync(N6Current, CancellationToken.None);
            Assert.Equal(FeedCheckStatus.NotNewer, r.Status);                       // 확인 결과는 그대로(받기 무영향)
            HttpRequestMessage[] reqs;
            lock (b.Http.Handler.Requests) reqs = b.Http.Handler.Requests.ToArray();
            var status = b.Service.GetStatus("gate-user");

            if (which is "ledger2" or "url")
            {
                Assert.True(reqs.Length == 1, which + ": 추가 요청이 나갔다 — " + string.Join(", ", reqs.Select(q => q.RequestUri)));
                Assert.Equal(new[] { N6Current + ".json" }, Kept(b.Env));
                Assert.False(status.CanRollback, which + ": " + status.Reason);
                Assert.Equal(SwapReasons.NoPreviousVersion, status.Reason);
                return;
            }

            Assert.Equal(2, reqs.Length);
            var second = reqs[1];
            Assert.Equal(N6PrevManifestUrl, second.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, second.Method);                            // 요청 모양 = 피드 확인과 같다(#18/#22)
            Assert.True(string.IsNullOrEmpty(second.RequestUri.Query), "쿼리가 붙었다: " + second.RequestUri);
            Assert.Empty(second.Headers);

            if (which != "ok")
            {
                Assert.Equal(new[] { N6Current + ".json" }, Kept(b.Env));          // 남기지 않는다
                Assert.False(status.CanRollback, which + ": 되돌리기가 섰다");
                Assert.Equal(SwapReasons.NoPreviousVersion, status.Reason);        // 새 사유 0
                return;
            }

            Assert.Equal(new[] { N6Prev + ".json", N6Current + ".json" }, Kept(b.Env));
            Assert.True(status.CanRollback, "첫 회 되돌리기가 안 섰다 — " + status.Reason);
            Assert.Equal(N6Prev, status.TargetVersion);
            Assert.Equal(SwapMaterialKinds.ManualZip, status.MaterialKind);
            var start = b.Service.Start("gate-user", status.Ticket);
            await b.Service.LastFetchRun.WaitAsync(TimeSpan.FromSeconds(30));
            var request = b.Launcher.ReadLast();
            Assert.True(request is not null, "요청서가 안 적혔다 — " + start.Reason);
            Assert.Equal(N6Prev, request!.To);
            Assert.Equal(SwapMaterialKinds.ManualZip, request.Material?.Kind);
            Assert.EndsWith("hitpan-" + N6Prev + ".zip", request.Material?.Path ?? "", StringComparison.OrdinalIgnoreCase);
            string[] urls;
            lock (b.Server.Urls) urls = b.Server.Urls.ToArray();
            Assert.Equal(prev.DownloadUrl, Assert.Single(urls));                    // 받기 = 받은 확인표의 downloadUrl
        });
    }
}
