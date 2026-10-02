using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
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

/// <summary>
/// N4b 시험 키(작업지시서 18-7) — 제품에 시험 주입 자리를 새로 내지 않고 <b>기존 운영 키 출처</b>
/// (<c>db.conf</c> 의 <c>HITPAN_UPDATE_PUBLIC_KEY__{kid}</c> · <c>UpdateSignatureVerifier.ResolvePublicKeyPem</c>)를 그대로 쓴다.
/// </summary>
/// <remarks>
/// <para>kid = <see cref="Kid"/>(운영 <c>upd-v1</c> 아님) — 시험 키가 남아도 운영 manifest 검증에는 닿지 않는다.</para>
/// <para>쓰는 자리 = 워치독 <c>DbConfReader</c> 가 찾는 순서(<c>BaseDirectory\..\db.conf</c> → <c>BaseDirectory\db.conf</c>)의 첫 있는 파일 ·
/// 없으면 <c>BaseDirectory\db.conf</c> 를 새로 만든다. 있던 파일은 바이트 그대로 백업 → finally 에서 복원 · 새로 만든 파일은 finally 에서 지운다.</para>
/// <para>병렬 끈 xUnit 컬렉션(<see cref="N4bTestKeyCollection"/>) 안에서만 부른다 — 그 컬렉션은 다른 시험이 모두 끝난 뒤 혼자 돈다.</para>
/// </remarks>
internal static class N4bTestKey
{
    public const string Kid = "hp-test-n4b";
    public const string ConfKey = "HITPAN_UPDATE_PUBLIC_KEY__" + Kid;

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>db.conf 한 줄에 들어갈 공개키 — 줄바꿈을 글자 <c>\n</c> 으로(검증기 <c>NormalizePem</c> 이 되돌린다).</summary>
    private static string PublicPemOneLine =>
        Key.ExportSubjectPublicKeyInfoPem().Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>서명 본문(<c>UpdateManifestSigning.BuildSigningPayload</c>)을 시험 키로 서명한다. der = openssl 모양(NCP 서버 원문).</summary>
    public static string Sign(string payload, bool der) => Convert.ToBase64String(Key.SignData(
        Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256,
        der ? DSASignatureFormat.Rfc3279DerSequence : DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public static UpdateManifest Signed(UpdateManifest m, bool der = false) =>
        m with { Kid = Kid, Signature = Sign(UpdateManifestSigning.BuildSigningPayload(m), der) };

    /// <summary>지금 이 시험 실행에서 <c>DbConfReader</c> 가 읽을 db.conf 자리.</summary>
    public static string ConfPath()
    {
        var b = AppContext.BaseDirectory;
        foreach (var c in new[] { Path.Combine(b, "..", "db.conf"), Path.Combine(b, "db.conf") })
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return Path.GetFullPath(Path.Combine(b, "db.conf"));
    }

    public static async Task WithKeyAsync(Func<Task> body)
    {
        var path = ConfPath();
        var existed = File.Exists(path);
        var backup = existed ? File.ReadAllBytes(path) : null;
        try
        {
            var head = existed ? File.ReadAllText(path).TrimEnd('\r', '\n') + Environment.NewLine : string.Empty;
            File.WriteAllText(path, head + ConfKey + "=" + PublicPemOneLine + Environment.NewLine, new UTF8Encoding(false));
            await body();
        }
        finally
        {
            if (existed) File.WriteAllBytes(path, backup!);
            else File.Delete(path);
        }
    }
}

/// <summary>N4b 시험 키를 쓰는 시험 묶음 — 병렬 끔(db.conf 는 프로세스 공유 파일).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class N4bTestKeyCollection
{
    public const string Name = "N4b 시험 키 db.conf (병렬 끔)";
}

/// <summary>모든 기록을 손에 쥐는 로거 — 경고 수를 잰다.</summary>
internal sealed class ListLoggerFactory : ILoggerFactory
{
    public List<(string Category, LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    public int Count(LogLevel level) { lock (Entries) return Entries.Count(e => e.Level == level); }

    private sealed class Logger(ListLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Entries) owner.Entries.Add((category, logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>
/// N4b — 서명 <b>통과</b> 저장본이 필요한 G-NK1 · G-NK4 · G-NK2(바꿔치기 대조 대체) · G-NK6. 시험 키 = <see cref="N4bTestKey"/>(db.conf · 제품 diff 0).
/// </summary>
/// <remarks>
/// 대조(Edit 도구로 제품을 바꿔 돌림 → 원복 · 개발명세서 N4 「N4b」 절): G-NK1 = <c>CheckAsync</c> 의 <c>KeepSignedManifest(manifest);</c> 뺌 ·
/// G-NK2b = <c>LoadVerified</c> 의 <c>Verify</c> 줄 뺌 · G-NK4 = <c>KeepSignedManifest</c> 의 catch 뺌 · G-NK6 = 읽기 규칙의 <c>JsonStringEnumConverter</c> 뺌.
/// </remarks>
[Collection(N4bTestKeyCollection.Name)]
public sealed partial class LocalSwapManifestKeepGateTests
{
    private static UpdateManifest Plain(string version) => new(
        version, UpdateChannel.Normal, "https://feed.invalid/hitpan-" + version + ".zip",
        "0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789ABCDEF", 123_456_789,
        new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), "판 안내 " + version, RequiresMigration: false, ConsentMessage: null);

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Serve(UpdateManifest m) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(m, FeedJson), Encoding.UTF8, "application/json") });

    /// <summary>남기기 담당 없는 어댑터(기존 생성자)로 같은 응답을 본 결과 — 「남기기 전과 같다」 기준.</summary>
    private static async Task<FeedCheckResult> WithoutKeeper(UpdateManifest m, string current)
    {
        var http = new RecordingFactory();
        http.Handler.Respond = Serve(m);
        return await new WatchdogUpdateCoreAdapter(http, NullLoggerFactory.Instance).CheckAsync(current, CancellationToken.None);
    }

    [Fact(DisplayName = "N4b G-NK 시험 키 — kid 가 운영 upd-v1 아님 · db.conf 에 키가 없으면 시험 키 서명 저장본은 통과 못 함(검증기가 진짜 키를 본다) · 끝나면 db.conf 원상")]
    public async Task Nk_test_key_is_isolated()
    {
        Assert.NotEqual(UpdateManifestSigning.DefaultKid, N4bTestKey.Kid);
        var path = N4bTestKey.ConfPath();
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            var signed = N4bTestKey.Signed(Plain(env.CurrentVersion));
            var keeper = new SignedManifestKeeper(env, new HookedFolderGuard(), NullLogger<SignedManifestKeeper>.Instance);
            Assert.True(keeper.Keep(signed.Version, JsonSerializer.Serialize(signed, FeedJson)));
            Assert.Null(adapter.LoadVerified(signed.Version));                 // 키 없음 → 거부
            await N4bTestKey.WithKeyAsync(() =>
            {
                Assert.NotNull(adapter.LoadVerified(signed.Version));          // 키 있음 → 통과
                return Task.CompletedTask;
            });
            Assert.Null(adapter.LoadVerified(signed.Version));                 // 복구 뒤 → 다시 거부
            Assert.Empty(http.Handler.Requests);
        }
        finally { Cleanup(root); }
        var after = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Assert.True(before is null ? after is null : after is not null && before.AsSpan().SequenceEqual(after), "db.conf 가 원상으로 안 돌아왔다: " + path);
    }

    // ── G-NK1 — 남김 (지금 판 · 더 높은 판) ──

    [Theory(DisplayName = "N4b G-NK1 🚨 시험 키 서명 manifest → CheckAsync 가 manifests\\{V}.json 을 남김 · LoadVerified 서명 통과·값 동일 · 결과는 남기기 없는 어댑터와 같음")]
    [InlineData("1.3.48")] // 지금 판(NotNewer)
    [InlineData("1.3.51")] // 더 높은 판(Newer)
    public async Task Nk1_signed_manifest_kept_and_round_trips(string version)
    {
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            await N4bTestKey.WithKeyAsync(async () =>
            {
                var signed = N4bTestKey.Signed(Plain(version));
                http.Handler.Respond = Serve(signed);
                var r = await adapter.CheckAsync(env.CurrentVersion, CancellationToken.None);
                Assert.Equal(await WithoutKeeper(signed, env.CurrentVersion), r);
                Assert.Equal(version == env.CurrentVersion ? FeedCheckStatus.NotNewer : FeedCheckStatus.Newer, r.Status);
                Assert.Contains(version + ".json", Kept(env));
                var pkg = adapter.LoadVerified(version);
                Assert.True(pkg is not null, "남긴 저장본이 서명 확인을 못 지났다 — 왕복이 서명 본문을 바꿨다");
                Assert.Equal(new FeedPackage(signed.Version, signed.Channel.ToString(), signed.DownloadUrl, signed.Sha256, signed.SizeBytes, signed.ReleaseNotes), pkg);
                Assert.Single(http.Handler.Requests);                          // 피드 GET 1 · 처리기 밖 0
            });
        }
        finally { Cleanup(root); }
    }

    // ── G-NK2 대조 대체(18-7) — 바꿔치기 저장본 ──

    [Fact(DisplayName = "N4b G-NK2b 🚨 서명 통과 저장본의 받는 주소를 바꿔치기 → LoadVerified null(바꾸기 전엔 통과) · 대조 = Verify 줄 뺀 사본에서 통과")]
    public async Task Nk2b_swapped_stored_manifest_rejected()
    {
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            await N4bTestKey.WithKeyAsync(async () =>
            {
                var signed = N4bTestKey.Signed(Plain(env.CurrentVersion));
                http.Handler.Respond = Serve(signed);
                await adapter.CheckAsync(env.CurrentVersion, CancellationToken.None);
                Assert.NotNull(adapter.LoadVerified(signed.Version));
                var file = Path.Combine(SignedManifestKeeper.ManifestsDir(env.AppRoot)!, signed.Version + ".json");
                var text = File.ReadAllText(file);
                Assert.Contains(signed.DownloadUrl, text);
                File.WriteAllText(file, text.Replace(signed.DownloadUrl, "https://evil.invalid/hitpan-" + signed.Version + ".zip", StringComparison.Ordinal));
                Assert.Null(adapter.LoadVerified(signed.Version));
            });
        }
        finally { Cleanup(root); }
    }

    // ── G-NK4 — 남기기 실패 무해 ──

    [Fact(DisplayName = "N4b G-NK4 🚨 폴더 문지기가 던짐 → CheckAsync 결과 그대로 · 경고 정확히 1(어댑터) · 예외 0 · 파일 0")]
    public async Task Nk4_keep_failure_is_harmless()
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-nk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var env = FakeSwapEnvironment.Under(root);
            var logs = new ListLoggerFactory();
            var guard = new HookedFolderGuard { Before = p => throw new IOException("gate: folder unsafe " + p) };
            var keeper = new SignedManifestKeeper(env, guard, logs.CreateLogger<SignedManifestKeeper>());
            var http = new RecordingFactory();
            var adapter = new WatchdogUpdateCoreAdapter(http, logs, keeper);
            await N4bTestKey.WithKeyAsync(async () =>
            {
                var signed = N4bTestKey.Signed(Plain("1.3.51"));
                http.Handler.Respond = Serve(signed);
                FeedCheckResult? r = null;
                var ex = await Record.ExceptionAsync(async () => r = await adapter.CheckAsync(env.CurrentVersion, CancellationToken.None));
                Assert.True(ex is null, "남기기 실패가 확인 결과로 새어 나왔다: " + ex);
                Assert.Equal(await WithoutKeeper(signed, env.CurrentVersion), r);
                Assert.Equal(1, logs.Count(LogLevel.Warning));
                var w = logs.Entries.Single(e => e.Level == LogLevel.Warning);
                Assert.Equal(typeof(WatchdogUpdateCoreAdapter).FullName, w.Category);
                Assert.IsType<IOException>(w.Error);
                Assert.Empty(Kept(env));
            });
        }
        finally { Cleanup(root); }
    }

    // ── G-NK6 — 서버 원문 모양 저장본 ──

    /// <summary>
    /// <c>installer/updates/build-manifest.ps1</c> 의 <c>[ordered]@{…} | ConvertTo-Json</c>(PowerShell 5.1 모양 · channel 은 글자) +
    /// NCP <c>sign-manifest.sh</c> 가 붙이는 signature(openssl DER · 표준 Base64)·kid.
    /// </summary>
    private static string ServerShapedManifest(string version, string channelText, string sig) =>
        "{\r\n" +
        "    \"version\":  \"" + version + "\",\r\n" +
        "    \"channel\":  \"" + channelText + "\",\r\n" +
        "    \"downloadUrl\":  \"https://updates.hitpan.kr/hitpan-" + version + ".zip\",\r\n" +
        "    \"sha256\":  \"" + new string('c', 64) + "\",\r\n" +
        "    \"sizeBytes\":  98765432,\r\n" +
        "    \"releasedAt\":  \"2026-10-01T00:00:00Z\",\r\n" +
        "    \"releaseNotes\":  null,\r\n" +
        "    \"requiresMigration\":  false,\r\n" +
        "    \"consentMessage\":  null,\r\n" +
        "    \"signature\":  \"" + sig + "\",\r\n" +
        "    \"kid\":  \"" + N4bTestKey.Kid + "\"\r\n" +
        "}";

    [Theory(DisplayName = "N4b G-NK6 🚨 서버 원문 모양(channel 글자 · openssl DER 서명) 저장본 {P}.json → LoadVerified 통과 · 판 == P")]
    [InlineData("Normal")]
    [InlineData("normal")]
    public async Task Nk6_server_shaped_stored_manifest_loads(string channelText)
    {
        var (adapter, http, env, root) = MakeAdapter();
        try
        {
            const string p = "1.3.47";
            var payload = UpdateManifestSigning.BuildSigningPayload(new UpdateManifest(
                p, UpdateChannel.Normal, "https://updates.hitpan.kr/hitpan-" + p + ".zip", new string('c', 64), 98765432,
                DateTime.MinValue, null, RequiresMigration: false, ConsentMessage: null));
            var dir = SignedManifestKeeper.ManifestsDir(env.AppRoot)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, p + ".json"), ServerShapedManifest(p, channelText, N4bTestKey.Sign(payload, der: true)), new UTF8Encoding(false));
            await N4bTestKey.WithKeyAsync(() =>
            {
                var pkg = adapter.LoadVerified(p);
                Assert.True(pkg is not null, "서버 원문 모양 저장본을 못 읽었다(읽기 규칙·서명 본문 어긋남)");
                Assert.Equal(p, pkg!.Version);
                Assert.Equal(new string('c', 64), pkg.Sha256);
                Assert.Equal(98765432, pkg.SizeBytes);
                return Task.CompletedTask;
            });
            Assert.Empty(http.Handler.Requests);
        }
        finally { Cleanup(root); }
    }
}
