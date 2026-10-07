using System.Net;
using HitPan.Watchdog;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 🔴 20261007작12 1차수 B3-1 — <b>G-7 워치독 계약</b> (PM 결재 ① · 2026-10-07).
///
/// <para><b>무엇을 재나</b>: ⓒ 단계 2 **축소 응답 JSON** 을 <see cref="TunnelTokenRecovery"/> 의
/// <b>실제 파싱 경로</b>(<c>RecoverAsync</c>)에 먹여 <c>domain.tunnelToken</c>·<c>domain.tunnelId</c> 가
/// <b>뽑히는지</b> 본다. 🔴 글자 검사가 아니다 — <b>파서를 실행</b>하고, 뽑힌 값이 실제로
/// <c>db.conf</c> 에 기록되는 것까지 **파일로** 확인한다.</para>
///
/// <para><b>왜 이 프로젝트인가</b>: <c>HitPan.Tests</c> 는 <c>net8.0</c>, <c>HitPan.Watchdog</c> 는
/// <c>net8.0-windows</c> 라 넓은 TFM 이 좁은 TFM 을 참조할 수 없다(NU1201). 이 프로젝트는
/// <b>이미</b> <c>net8.0-windows</c> + <c>HitPan.Watchdog</c> ProjectReference 를 갖고 있고
/// <b>이미</b> <c>src/HitPan.sln</c> 에 들어 있다 ⇒ CI <c>build</c> 잡(windows-latest ·
/// <c>dotnet test src/HitPan.sln</c>)이 그대로 돌린다. DB 불필요.</para>
///
/// <para>🔴 <b>이 게이트가 재지 않는 것</b>: 축소 응답의 **모양**이다. 그건
/// <c>HitPan.Tests/Integrity/InstallerBootstrapPreverifyGateTests.C1_…</c> 가 **실물 컨트롤러**로 잰다.
/// 여기서 쓰는 JSON 은 그 실측과 같은 이름 11개로 세운 **입력 재료**다.
/// ⇒ 모양 = C-1(실물 컨트롤러) · 파싱 = G-7(실물 파서). 이 이음매를 숨기지 않고 적는다.</para>
///
/// <para>⚠️ <see cref="DbConfReader"/>·<see cref="DbConfWriter"/> 는 <c>AppContext.BaseDirectory</c> 의
/// <c>db.conf</c> 를 쓴다 — <see cref="DbConfReaderTests"/> 와 <b>같은 파일</b>이다.
/// 두 클래스가 병렬로 돌면 한쪽이 지운 파일을 다른 쪽이 읽어 깜빡인다
/// ⇒ 같은 collection 으로 **직렬화**한다(상태 공유는 0).</para>
/// </summary>
[Collection(WatchdogDbConfCollection.Name)]
public sealed class InstallerBootstrapReducedResponseGateTests : IDisposable
{
    // 🔴 자격증명 「모양」을 만들지 않는다 — 가짜여도 비밀값처럼 생긴 문자열은 남기지 않는다(누적 3회 지적).
    private const string FakeTunnelToken = "gate-tunnel-token-placeholder-not-a-secret";
    private const string FakeTunnelId = "gate-tunnel-id-placeholder";

    private readonly string _confPath = Path.Combine(AppContext.BaseDirectory, "db.conf");

    public InstallerBootstrapReducedResponseGateTests()
    {
        // 워치독은 db.conf 의 LICENSE_KEY 가 없으면 **요청 자체를 안 보낸다**(TunnelTokenRecovery.cs:76-83).
        //   그래서 파서까지 가게 하려면 이 두 줄이 있어야 한다. 값은 시험 전용 문자열이다.
        File.WriteAllLines(_confPath, new[]
        {
            "LICENSE_KEY=GATE-B31-LICENSE-PLACEHOLDER",
            "BACKOFFICE_URL=http://127.0.0.1:1",
        });
    }

    public void Dispose()
    {
        try { if (File.Exists(_confPath)) File.Delete(_confPath); }
        catch (IOException ex)
        {
            // #15 — 뒷정리 실패를 삼키지 않는다. 다음 실행의 WriteAllLines 가 덮는다.
            Console.Error.WriteLine($"[G-7] db.conf 뒷정리 실패: {ex.Message}");
        }
    }

    /// <summary>ⓒ 단계 2 축소 응답 — 이름 11개. tenant 묶음·bootstrap.token·tokenKey 가 **없다**.</summary>
    private static string ReducedStage2Json() => """
        {
          "success": true,
          "source": "installer-bootstrap",
          "domain": {
            "primary": "gate.hitpan.kr",
            "api": "api-gate.hitpan.kr",
            "tunnelTokenIssued": true,
            "tunnelToken": "gate-tunnel-token-placeholder-not-a-secret",
            "tunnelId": "gate-tunnel-id-placeholder"
          },
          "bootstrap": {
            "expiresInSec": 86400,
            "backofficeUrl": "https://back.hitpan.kr"
          }
        }
        """;

    private static TunnelTokenRecovery NewRecovery(HttpStatusCode code, string body, out CountingHandler handler)
    {
        handler = new CountingHandler(code, body);
        return new TunnelTokenRecovery(
            NullLogger<TunnelTokenRecovery>.Instance, new OneClientFactory(handler));
    }

    // ══════════════════════════════════════════════════════════════
    // G-7 — 축소 응답을 실제 파서에 먹인다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-7. 축소 응답 → <c>RecoverAsync</c> 가 터널 토큰을 <b>반환</b>하고 <c>db.conf</c> 에
    /// <c>TUNNEL_TOKEN</c>·<c>TUNNEL_ID</c> 를 <b>기록</b>한다. ⇒ ⓒ 가 워치독 자가복구를 깨지 않는다.
    /// <para><b>봉합을 빼면</b>(ⓒ 가 <c>domain.*</c> 를 건드리면) 토큰이 <c>null</c> 이 되어 FAIL 한다 —
    /// 아래 G-7b·G-7c 가 그 두 모양을 각각 음성 대조군으로 고정한다.</para>
    /// </summary>
    [Fact]
    public async Task G7_단계2_축소응답을_실제_파서가_읽어_터널토큰을_뽑는다()
    {
        var recovery = NewRecovery(HttpStatusCode.OK, ReducedStage2Json(), out var handler);

        var token = await recovery.RecoverAsync(CancellationToken.None);

        Assert.Equal(FakeTunnelToken, token);                       // 파서가 뽑았다(반환값)
        Assert.Equal(1, handler.Calls);                             // 요청이 실제로 1회 나갔다(대역)

        // 🔴 뽑기만 한 게 아니라 **보관까지** 됐다 — WS-28-D 가 이 값으로 터널을 재설치한다
        var written = File.ReadAllLines(_confPath);
        Assert.Contains($"TUNNEL_TOKEN={FakeTunnelToken}", written);
        Assert.Contains($"TUNNEL_ID={FakeTunnelId}", written);
    }

    /// <summary>
    /// G-7b 음성 대조군 — ⓒ 가 <c>domain</c> 묶음을 통째로 빼면 파서가 못 읽는다(<c>null</c>).
    /// 이 시험이 초록이어야 G-7 의 초록이 「파서를 진짜 태웠다」는 뜻이 된다.
    /// </summary>
    [Fact]
    public async Task G7b_domain_묶음을_빼면_파서가_못_읽는다()
    {
        var recovery = NewRecovery(HttpStatusCode.OK,
            """{"success":true,"source":"installer-bootstrap","bootstrap":{"expiresInSec":86400}}""",
            out _);

        Assert.Null(await recovery.RecoverAsync(CancellationToken.None));
        Assert.False(File.ReadAllText(_confPath).Contains("TUNNEL_TOKEN", StringComparison.Ordinal));
    }

    /// <summary>
    /// G-7c 음성 대조군 — <c>domain</c> 은 있는데 <c>tunnelToken</c> 만 빠지면 <c>null</c>.
    /// (ⓒ 가 「비밀값이니 터널 토큰도 빼자」로 번지면 이 모양이 된다 — 그 길을 막는다.)
    /// </summary>
    [Fact]
    public async Task G7c_tunnelToken_하나만_빠져도_파서가_못_읽는다()
    {
        var recovery = NewRecovery(HttpStatusCode.OK,
            """{"success":true,"domain":{"primary":"gate.hitpan.kr","tunnelId":"gate-tunnel-id-placeholder"}}""",
            out _);

        Assert.Null(await recovery.RecoverAsync(CancellationToken.None));
    }

    /// <summary>
    /// G-7d — 신설 <b>423</b>(ⓑ 잠금)이 와도 워치독이 <b>안 터지고</b> <c>null</c> 로 끝난다
    /// (다음 사이클 재시도 · 설계 §7). <c>db.conf</c> 도 건드리지 않는다.
    /// </summary>
    [Fact]
    public async Task G7d_423이_와도_워치독은_예외없이_null로_끝난다()
    {
        var recovery = NewRecovery((HttpStatusCode)423,
            """{"success":false,"locked":true,"message":"x"}""", out var handler);

        Assert.Null(await recovery.RecoverAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.False(File.ReadAllText(_confPath).Contains("TUNNEL_TOKEN", StringComparison.Ordinal));
    }

    /// <summary>
    /// G-7e — 워치독이 요청에 ⓒ 단계 표시(<c>purpose</c>)를 <b>실제로 싣는다</b>.
    /// 대역이 **보낸 본문**을 캡처해 확인한다(글자 검사가 아니라 나간 요청을 본다).
    /// 이게 빠지면 서버는 전체 응답을 주고 ⓒ 가 **발효되지 않는다**.
    /// </summary>
    [Fact]
    public async Task G7e_워치독_요청에_단계표시가_실제로_실린다()
    {
        var recovery = NewRecovery(HttpStatusCode.OK, ReducedStage2Json(), out var handler);

        await recovery.RecoverAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"purpose\":\"tunnel-recovery\"", handler.LastRequestBody);
        // 종전 4개 필드도 그대로 간다(요청 규약을 안 깨뜨렸다)
        Assert.Contains("\"licenseKey\":", handler.LastRequestBody);
        Assert.Contains("\"machineFingerprint\":", handler.LastRequestBody);
        Assert.Contains("\"hostname\":", handler.LastRequestBody);
        Assert.Contains("\"installerVersion\":", handler.LastRequestBody);
    }

    // ══════════════════════════════════════════════════════════════
    // 대역 — 🔴 외부 실호출 0. 본사로 나가는 패킷이 없다.
    // ══════════════════════════════════════════════════════════════

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public int Calls;
        public string? LastRequestBody;

        public CountingHandler(HttpStatusCode code, string body) { _code = code; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_code) { Content = new StringContent(_body) };
        }
    }

    private sealed class OneClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public OneClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}

/// <summary>
/// <c>AppContext.BaseDirectory</c> 의 <c>db.conf</c> 를 쓰는 시험들을 직렬화한다 —
/// 한 파일을 두 클래스가 동시에 쓰고 지우면 깜빡인다(병렬 기본값).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WatchdogDbConfCollection
{
    public const string Name = "WatchdogDbConf";
}
