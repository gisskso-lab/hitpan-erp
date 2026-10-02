using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// G-NK 동작 시험(N1 `19d6e424` 위) — 가짜 <c>IHttpClientFactory</c> 처리기만 쓴다(NCP 실접속 0 · 요청은 처리기가 손에 쥐고 잰다).
/// </summary>
/// <remarks>
/// ⚠️ 시험 키로 「서명 통과」 manifest 를 만들 수 없다 — 검증기 키 = 내장 <c>upd-v1</c>(개인키 NCP) 또는 <c>db.conf</c> 뿐이고
/// 어댑터에 검증기 주입 이음매가 없다(개발명세서 N4 §5 첫 행동 ②). 그래서 서명 통과가 전제인 G-NK1·G-NK4 는 여기 없다.
/// </remarks>
public sealed partial class LocalSwapManifestKeepGateTests
{
    /// <summary>모든 요청을 손에 쥐는 처리기 — 바깥으로 나가는 길이 없다(밑단 소켓 처리기 0).</summary>
    internal sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request);
            return Respond(request, ct);
        }
    }

    internal sealed class RecordingFactory : IHttpClientFactory
    {
        public RecordingHandler Handler { get; } = new();
        public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
    }

    private sealed class CountingFeed : IUpdateFeed
    {
        public int Calls;
        public Func<CancellationToken, Task<FeedCheckResult>> Next { get; set; } =
            _ => Task.FromResult(new FeedCheckResult(FeedCheckStatus.NotNewer, null, "same"));

        public Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Next(ct);
        }
    }

    private static readonly JsonSerializerOptions FeedJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string ManifestJson(string version, string signature) => JsonSerializer.Serialize(new UpdateManifest(
        version, UpdateChannel.Normal, "https://feed.invalid/hitpan-" + version + ".zip", new string('a', 64), 1234,
        new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), null, RequiresMigration: false, ConsentMessage: null,
        Signature: signature, Kid: UpdateManifestSigning.DefaultKid), FeedJson);

    private static (WatchdogUpdateCoreAdapter Adapter, RecordingFactory Http, FakeSwapEnvironment Env, string Root) MakeAdapter()
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-nk-" + Guid.NewGuid().ToString("N"));
        var env = FakeSwapEnvironment.Under(root);
        var keeper = new SignedManifestKeeper(env, new HookedFolderGuard(), NullLogger<SignedManifestKeeper>.Instance);
        var http = new RecordingFactory();
        return (new WatchdogUpdateCoreAdapter(http, NullLoggerFactory.Instance, keeper), http, env, root);
    }

    private static string[] Kept(FakeSwapEnvironment env)
    {
        var dir = SignedManifestKeeper.ManifestsDir(env.AppRoot);
        return dir is not null && Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n).ToArray() : Array.Empty<string>();
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine("[G-NK] 임시 폴더 정리 실패(시험 결과 무관): " + root + " — " + ex.Message); }
    }

    // ── G-NK2 — 서명 불량 안 남김 ──

    [Fact(DisplayName = "N4 G-NK2 🚨 서명 불량 manifest → CheckAsync = Invalid · manifests 파일 0 · 요청 = 피드 GET 1(바깥 주소 실접속 0)")]
    public async Task Nk2_bad_signature_not_kept()
    {
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            http.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(ManifestJson("1.3.51", "AAAA"), Encoding.UTF8, "application/json") });
            var r = await adapter.CheckAsync(env.CurrentVersion, CancellationToken.None);
            Assert.Equal(FeedCheckStatus.Invalid, r.Status);
            Assert.Empty(Kept(env));
            Assert.Single(http.Handler.Requests);
            Assert.Equal(HttpMethod.Get, http.Handler.Requests[0].Method);
            Assert.Null(adapter.LoadVerified("1.3.51"));
        }
        finally { Cleanup(root); }
    }

    // ── G-NK3 — 보관 규칙 ──

    [Fact(DisplayName = "N4 G-NK3 🚨 저장본 1.3.46~1.3.49 + 지금 1.3.48 · 판 이력 직전 = 1.3.46 → 남는 것 = 1.3.46·1.3.48·1.3.49")]
    public void Nk3_retention()
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-nk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var env = FakeSwapEnvironment.Under(root);
            env.CurrentVersion = "1.3.48";
            var keeper = new SignedManifestKeeper(env, new HookedFolderGuard(), NullLogger<SignedManifestKeeper>.Instance);
            var work = Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, LocalSwapLauncher.VersionsSeenFileName),
                "1.3.45|2026-09-01T00:00:00Z\n1.3.46|2026-09-10T00:00:00Z\n1.3.48|2026-10-01T00:00:00Z\n");
            foreach (var v in new[] { "1.3.45", "1.3.46", "1.3.47", "1.3.49" })
                Assert.True(keeper.Keep(v, "{\"version\":\"" + v + "\"}"), v + " 남기기 실패");
            Assert.True(keeper.Keep("1.3.48", "{\"version\":\"1.3.48\"}"));
            Assert.Equal(new[] { "1.3.46.json", "1.3.48.json", "1.3.49.json" }, Kept(env));
        }
        finally { Cleanup(root); }
    }

    // ── G-NK5 — 기동 1회 (X-2 나 · 병렬이슈 22 보강) ──

    private static SignedManifestStartupCheck Startup(IUpdateFeed feed, FakeSwapEnvironment env) =>
        new(feed, env, NullLogger<SignedManifestStartupCheck>.Instance);

    [Fact(DisplayName = "N4 G-NK5 🚨 호스트 시작 → 피드 확인 정확히 1회 · 실패(예외)여도 StartAsync 던지지 않음 · 재시도 0")]
    public async Task Nk5_once_and_never_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-nk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var env = FakeSwapEnvironment.Under(root);
            var feed = new CountingFeed { Next = _ => throw new HttpRequestException("끊김") };
            var svc = Startup(feed, env);
            await svc.StartAsync(CancellationToken.None);
            if (svc.ExecuteTask is not null) await svc.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(300);
            Assert.Equal(1, feed.Calls);
            await svc.StopAsync(CancellationToken.None);
        }
        finally { Cleanup(root); }
    }

    [Fact(DisplayName = "N4 G-NK5a 🚨 피드가 응답을 붙잡아도 StartAsync 는 곧바로 끝난다(기동 비차단)")]
    public async Task Nk5a_start_does_not_wait_for_feed()
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-nk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var env = FakeSwapEnvironment.Under(root);
            var hold = new TaskCompletionSource<FeedCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var feed = new CountingFeed { Next = ct => hold.Task.WaitAsync(ct) };
            var svc = Startup(feed, env);
            var start = svc.StartAsync(CancellationToken.None);
            var done = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(ReferenceEquals(done, start), "피드가 붙잡힌 동안 StartAsync 가 5초 안에 안 끝났다 — 기동을 막는다");
            await svc.StopAsync(CancellationToken.None);
            hold.TrySetCanceled();
        }
        finally { Cleanup(root); }
    }

    [Fact(DisplayName = "N4 G-NK5b 🚨 기동 확인 요청 모양 = 피드 GET 1 · 쿼리 0 · 헤더 0(User-Agent 포함) · 처리기 밖으로 나간 요청 0")]
    public async Task Nk5b_request_shape()
    {
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            var svc = Startup(adapter, env);
            await svc.StartAsync(CancellationToken.None);
            if (svc.ExecuteTask is not null) await svc.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
            await svc.StopAsync(CancellationToken.None);
            var req = Assert.Single(http.Handler.Requests);
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.NotNull(req.RequestUri);
            Assert.True(string.IsNullOrEmpty(req.RequestUri!.Query), "쿼리가 붙었다: " + req.RequestUri);
            Assert.EndsWith("manifest.json", req.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(req.Headers);          // User-Agent·인증·고객 식별 헤더 전부 0
            Assert.Empty(req.Headers.UserAgent);
        }
        finally { Cleanup(root); }
    }
}
