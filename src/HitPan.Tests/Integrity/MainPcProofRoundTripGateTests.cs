using System.Collections;
using System.Data;
using System.Net;
using System.Reflection;
using HitPan.API.Security;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Application.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-1a · G-1c · G-2a · G-4 · G-5a · G-5b · G-6 · G-7a~G-7d · G-9 · G-13a</b> —
/// 메인PC 「왕복 증명」의 <b>표·출입증·판정</b> (20260922작3 · 설계 §8).
/// <para>🚨 <b>G-1c 는 이 파일의 다른 게이트가 전부 놓친 P0 를 지킨다</b> — 미들웨어를 실제로 지나가 본다.</para>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 2026-09-22 사장님 실측 4건. 그 중 ④가 보안 결함이었다:
/// <i>"외부접속 컴퓨터라 클라이언트임에도 메인이라고 거짓말 하고 접속이 됨."</i>
/// 그리고 ②는 <b>불안정이 아니라 도메인으로 열면 100% 실패</b>였다 —
/// 종전 판정이 <c>터널 헤더 없음 AND 루프백</c> 하나뿐이었고, Cloudflare 가
/// <c>CF-Connecting-IP</c> 를 반드시 붙이므로 <b>첫 조건에서 항상 탈락</b>했다.
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나 — 글자검사가 아니다.</b>
/// 실제 <see cref="MainPcProofService"/> 와 실제 <see cref="MainPcOnlyAttribute"/> 를
/// <b>불러서</b> 표를 소모시키고 출입증을 받아 판정값을 읽는다.
/// 출입증도 <b>시험이 지어내지 않는다</b> — 생산코드가 발급한 것만 쓴다.
/// </para>
///
/// <para>
/// 🔴 <b>이 파일은 DB 를 만지지 않는다 — 말이 아니라 구조로 막았다.</b>
/// 연결 자리에 <c>ForbiddenDbConnection</c> 을 꽂았다. 누가 이 파일에
/// DB 를 타는 시험을 섞으면 <b>그 자리에서 터진다.</b> 섞지 마라 —
/// DB 게이트는 <c>MainPcSealRegistryGateTests</c> 에 있다. 섞으면 DB 없는 로컬에서
/// <b>순수 게이트까지 통째로 건너뛰게 된다.</b>
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> (초록불을 실측으로 읽지 마라)
/// <list type="bullet">
/// <item>브라우저가 <b>실제로 loopback 을 두드리는지</b> 못 잰다. 🔴 헤드리스로 대신 재면
///       <b>결과가 정반대</b>로 나온다(인계3 §6-2 — <c>headless:true</c> 는 loopback 을 거부한다).</item>
/// <item>자료관리 <b>메뉴가 열리는지</b> 못 잰다. 재는 것은 <c>IsMainPc</c> 판정값까지다.</item>
/// <item>팝업이 <b>몇 번 뜨는지</b> 못 잰다 (G-11b·G-12·G-20).</item>
/// <item>키가 <b>물리적으로 그 PC 에 묶였는지</b> 못 잰다 (G-13b). 임의 바이트가 안 풀리는 것은
///       <i>다른 PC 의 봉인이 안 풀리는 것</i>과 같지 않다.</item>
/// <item>🔴 <b>실물 TPM 봉인 경로는 한 번도 안 돈다</b> — <see cref="ITpmKeyService.SealKey"/> 는
///       <c>CngKey.Create(..., "HitPan.TaxInvoice.MasterKey", OverwriteExistingKey)</c> 라
///       <b>부르면 이 PC 의 그 TPM 키를 덮어쓴다.</b> 이 PC 는 정식설치 실물이다(#39).
///       ⇒ 봉인 쪽은 가짜를 쓴다. <c>UnsealKey</c> 만 비파괴라 실물을 쓴다.</item>
/// </list>
/// 🔴 <b>이 파일이 전부 초록이어도 실물 실측(M-1)은 여전히 0회다.</b>
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MainPcProofRoundTripGateTests
{
    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>DB 를 만지면 터진다.</b> 이 파일이 "DB 무접촉" 이라는 주장을 구조로 증명한다.
    /// </summary>
    private sealed class ForbiddenDbConnection : IDbConnection
    {
        private static Exception Boom() => new Xunit.Sdk.XunitException(
            "이 게이트는 DB 를 만지면 안 된다 — DB 를 타는 시험은 MainPcSealRegistryGateTests 로 옮겨라. "
          + "섞으면 DB 없는 환경에서 순수 게이트까지 함께 건너뛴다(20260828 작14 W1 계통).");

        // ⚠️ [AllowNull] 이 있어야 한다 — IDbConnection 의 설정자가 null 을 허용하는 것으로
        //   주석돼 있어, 없으면 CS8767(널 허용 불일치)로 빌드가 0/0 을 못 지킨다(#19).
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get => throw Boom(); set => throw Boom(); }
        public int ConnectionTimeout => throw Boom();
        public string Database => throw Boom();
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw Boom();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw Boom();
        public void ChangeDatabase(string databaseName) => throw Boom();
        public void Close() => throw Boom();
        public IDbCommand CreateCommand() => throw Boom();
        public void Open() => throw Boom();
        public void Dispose() { }
    }

    /// <summary>봉인은 부르지 않는다 — 실물 <c>SealKey</c> 는 이 PC 의 TPM 키를 덮어쓴다(#39).</summary>
    private sealed class NeverCalledTpm : ITpmKeyService
    {
        public bool IsTpmAvailable() => true;

        public byte[] SealKey(byte[] masterKey) => throw new Xunit.Sdk.XunitException(
            "이 파일은 봉인을 부르지 않는다 — 실물 SealKey 는 이 PC 의 TPM 키를 덮어쓴다(#39).");

        public byte[] UnsealKey(byte[] sealedKey) => throw new Xunit.Sdk.XunitException(
            "이 파일은 봉인 해제를 부르지 않는다 — DB 를 타는 경로다.");

        public bool IsSealedKeyValid(byte[] sealedKey) => throw new Xunit.Sdk.XunitException("같은 이유.");
    }

    private static MainPcProofService NewService() =>
        new(new ForbiddenDbConnection(), new NeverCalledTpm(), NullLogger<MainPcProofService>.Instance);

    /// <summary>회사·세션은 시험마다 고유하게 — 표 보관소가 <b>static 전역</b>이다(작3 §4-2).</summary>
    private static string Fresh(string tag) => $"{tag}-{Guid.NewGuid():N}";

    // ── 무대 세우기 (리플렉션) ────────────────────────────────────
    //
    // 🔴 왜 리플렉션인가 — ②(로컬 직결 확인)는 DB 를 타므로 이 파일에서 부를 수 없다.
    //   그래서 ②가 남길 상태(Outcome)만 세워 두고, **판정은 생산코드가 하게** 한다.
    //   재는 것은 Consume 의 1회용·만료·회사·세션·출입증 발급 조건 — 전부 생산코드다.
    //
    // 🚨 이름이 바뀌면 리플렉션은 조용히 null 을 물고 시험이 **아무것도 안 재고 초록**이 된다.
    //   ⇒ 못 찾으면 그 자리에서 떨어뜨린다. 이름이 바뀌면 빨간불이 되는 것이 맞다.

    private static IDictionary ChallengeStore()
    {
        var f = typeof(MainPcProofService).GetField("_challenges",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(f is not null,
            "MainPcProofService._challenges 를 못 찾았다 — 이름이 바뀌었다면 이 게이트를 함께 고쳐라. "
          + "못 찾은 채로 통과시키면 아무것도 안 재는 가짜 게이트가 된다.");

        var store = f!.GetValue(null) as IDictionary;
        Assert.True(store is not null, "_challenges 가 사전이 아니다 — 자료구조가 바뀌었다.");
        return store!;
    }

    private static object TicketOf(string challenge)
    {
        var e = ChallengeStore()[challenge];
        Assert.True(e is not null, "발급한 표가 보관소에 없다 — IssueChallenge 가 표를 안 담고 있다.");
        return e!;
    }

    private static void SetOn(object ticket, string prop, object value)
    {
        var p = ticket.GetType().GetProperty(prop,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(p is not null, $"표의 {prop} 칸을 못 찾았다 — 이름이 바뀌었다면 이 게이트를 함께 고쳐라.");
        p!.SetValue(ticket, value);
    }

    /// <summary>②가 통과했을 때의 상태만 세운다. <b>판정은 세우지 않는다</b> — 그것은 생산코드가 한다.</summary>
    private static void StageOutcome(string challenge, MainPcProofOutcome outcome) =>
        SetOn(TicketOf(challenge), "Outcome", outcome);

    private static void StageExpired(string challenge) =>
        SetOn(TicketOf(challenge), "ExpiresAt", DateTimeOffset.UtcNow.AddSeconds(-1));

    // ── 요청 만들기 ──────────────────────────────────────────────

    private sealed class OneService : IServiceProvider
    {
        private readonly Type _type;
        private readonly object? _impl;

        public OneService(Type type, object? impl)
        {
            _type = type;
            _impl = impl;
        }

        public object? GetService(Type serviceType) => serviceType == _type ? _impl : null;
    }

    /// <summary>고객이 <b>도메인(터널)</b>으로 접속한 요청. Cloudflare 가 헤더를 붙인 그 모양이다.</summary>
    private static DefaultHttpContext ViaTunnel(IMainPcProofService? svc, string? pass)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["CF-Connecting-IP"] = "124.194.59.154";   // 실측값 (선행검증서)

        // 🔴 터널은 그 PC 안에서 히트판을 다시 부른다 ⇒ 소켓 주소는 **메인PC 든 외부든 항상 루프백**이다.
        //   이 한 줄이 "IP 로는 영영 못 가린다" 를 시험 안에 새겨 둔 자리다.
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;

        if (pass is not null) ctx.Request.Headers[MainPcOnlyAttribute.PassHeader] = pass;
        ctx.RequestServices = new OneService(typeof(IMainPcProofService), svc);
        return ctx;
    }

    /// <summary>그 컴퓨터에서 <b>직접</b> 연 화면 (<c>localhost</c> 직결 · 종전 경로).</summary>
    private static DefaultHttpContext LocalConsole()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.RequestServices = new OneService(typeof(IMainPcProofService), null);
        return ctx;
    }

    /// <summary>생산코드가 실제로 발급한 출입증을 받아 온다. 시험이 지어내지 않는다.</summary>
    private static string RealPass(MainPcProofService svc, string tenant, string session)
    {
        var challenge = svc.IssueChallenge(tenant, session);
        StageOutcome(challenge, MainPcProofOutcome.MainPcConfirmed);

        var outcome = svc.Consume(tenant, session, challenge, out var pass);
        Assert.Equal(MainPcProofOutcome.MainPcConfirmed, outcome);
        Assert.False(string.IsNullOrWhiteSpace(pass), "통과한 표에는 출입증이 나와야 한다.");
        return pass!;
    }

    // ══════════════════════════════════════════════════════════════
    // G-1a · G-2a — 도메인으로 접속해도 메인PC 를 알아본다 / 아닌 것은 막는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-1a — 이 게이트의 수문장.</b> 도메인(터널)으로 들어와도 <b>출입증이 있으면</b> 메인PC 다.
    /// </summary>
    /// <remarks>
    /// 종전에는 이 경우가 <b>100% 실패</b>였다(<c>CF-Connecting-IP</c> 때문에 첫 조건에서 탈락).
    /// ⇒ <c>MainPcOnlyAttribute.IsMainPc</c> 의 갈래②를 지우면 <b>즉시 FAIL</b> 한다 (대조군 C-7).
    /// </remarks>
    [Fact(DisplayName = "G-1a 🔴 도메인으로 접속해도 출입증이 있으면 메인PC 로 통과한다")]
    public void G1a_도메인접속도_출입증이_있으면_통과한다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");

        var pass = RealPass(svc, tenant, session);

        Assert.True(MainPcOnlyAttribute.IsMainPc(ViaTunnel(svc, pass)),
            "도메인으로 접속한 메인PC 가 막혔다 — 사장님 실측 ②③(자료관리 403)이 그대로 돌아온 것이다.");
    }

    /// <summary>G-2a — 출입증이 없는 도메인 접속(클라이언트 PC)은 <b>메인PC 가 아니다</b>.</summary>
    [Fact(DisplayName = "G-2a 🔴 출입증이 없는 도메인 접속은 메인PC 가 아니다")]
    public void G2a_출입증이_없으면_메인PC가_아니다()
    {
        var svc = NewService();

        Assert.False(MainPcOnlyAttribute.IsMainPc(ViaTunnel(svc, pass: null)),
            "출입증 없이 통과했다 — 클라이언트 PC 가 자료관리를 열 수 있다는 뜻이다.");

        Assert.False(MainPcOnlyAttribute.IsMainPc(ViaTunnel(svc, pass: "지어낸출입증")),
            "지어낸 출입증이 통과했다 — 출입증은 서버가 만든 것만 유효해야 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-4 · G-5 · G-6 — 표는 1회용 · 짧게 살고 · 그 세션에만
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-4 — 같은 표는 두 번 쓰이지 않는다.</b> 1번째만 출입증이 나온다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>"둘 다 거부됐다" 로 통과시키면 가짜 게이트다</b> — 표가 처음부터 무효였을 수도 있기 때문이다.
    /// 그래서 <b>1번째에 출입증이 실제로 나오는 것</b>까지 함께 본다.
    /// ⇒ <c>Consume</c> 의 <c>TryRemove</c> 를 <c>TryGetValue</c> 로 바꾸면 FAIL (대조군 C-1).
    /// </remarks>
    [Fact(DisplayName = "G-4 🔴 같은 표를 두 번 쓰면 두 번째는 거부된다")]
    public void G4_같은_표는_한_번만_통한다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");

        var challenge = svc.IssueChallenge(tenant, session);
        StageOutcome(challenge, MainPcProofOutcome.MainPcConfirmed);

        var first = svc.Consume(tenant, session, challenge, out var pass1);
        Assert.Equal(MainPcProofOutcome.MainPcConfirmed, first);
        Assert.False(string.IsNullOrWhiteSpace(pass1),
            "1번째가 통과하지 않으면 이 시험은 아무것도 못 잰다 — 무대가 안 섰다는 뜻이다.");

        var second = svc.Consume(tenant, session, challenge, out var pass2);
        Assert.Equal(MainPcProofOutcome.NotProven, second);
        Assert.Null(pass2);
    }

    /// <summary>🔴 <b>G-5a — 만료된 표는 거부된다.</b> ⇒ <c>IsExpired</c> 검사를 지우면 FAIL (대조군 C-2).</summary>
    /// <remarks>
    /// ⚠️ <b>이 시험이 못 하는 것</b> — <c>IssueChallenge</c> 가 넣는 수명이 60초인지는 못 잰다.
    /// 그것은 <b>G-5b</b> 가 잰다. <b>둘이 다 있어야</b> D-2(60초)를 쟀다고 말할 수 있다.
    /// </remarks>
    [Fact(DisplayName = "G-5a 🔴 만료된 표는 거부된다")]
    public void G5a_만료된_표는_거부된다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");

        var challenge = svc.IssueChallenge(tenant, session);
        StageOutcome(challenge, MainPcProofOutcome.MainPcConfirmed);
        StageExpired(challenge);

        var outcome = svc.Consume(tenant, session, challenge, out var pass);

        Assert.Equal(MainPcProofOutcome.NotProven, outcome);
        Assert.Null(pass);
    }

    /// <summary>G-5b — 표의 수명이 <b>60초</b>인가 (사장님 결재 D-2). G-5a 의 구멍을 메운다.</summary>
    [Fact(DisplayName = "G-5b 🔴 표의 수명은 60초다 (결재 D-2)")]
    public void G5b_표의_수명은_60초다()
    {
        var f = typeof(MainPcProofService).GetField("ChallengeLifetime",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(f is not null,
            "ChallengeLifetime 을 못 찾았다 — 이름이 바뀌었다면 이 게이트를 함께 고쳐라(D-2 를 지키는 유일한 자리다).");

        Assert.Equal(TimeSpan.FromSeconds(60), (TimeSpan)f!.GetValue(null)!);
    }

    /// <summary>🔴 <b>G-6 — 남의 표는 통하지 않는다.</b> 세션·회사 두 축을 각각 본다.</summary>
    /// <remarks>⇒ <c>SessionKey</c> 대조 제거 시 FAIL(C-4) · <c>TenantId</c> 대조 제거 시 FAIL(C-5).</remarks>
    [Fact(DisplayName = "G-6 🔴 다른 세션·다른 회사의 표는 통하지 않는다")]
    public void G6_남의_표는_통하지_않는다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");

        // ① 다른 세션이 주워 쓴다
        var c1 = svc.IssueChallenge(tenant, session);
        StageOutcome(c1, MainPcProofOutcome.MainPcConfirmed);
        Assert.Equal(MainPcProofOutcome.NotProven, svc.Consume(tenant, Fresh("s2"), c1, out var p1));
        Assert.Null(p1);

        // ② 다른 회사가 쓴다
        var c2 = svc.IssueChallenge(tenant, session);
        StageOutcome(c2, MainPcProofOutcome.MainPcConfirmed);
        Assert.Equal(MainPcProofOutcome.NotProven, svc.Consume(Fresh("t2"), session, c2, out var p2));
        Assert.Null(p2);
    }

    // ══════════════════════════════════════════════════════════════
    // G-7 — 자칭 차단 (사장님이 실측으로 잡으신 결함 ④)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-7a — 자칭할 칸 자체가 없다.</b> 왕복이 주고받는 것은 <b>표 하나뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 밖에서 온 값으로 신분을 정하면 <b>언제나 자칭이 가능하다</b> — 그것이 결함 ④의 뿌리였다.
    /// ⇒ 요청 본문에 <c>IsMainPc</c> 같은 칸을 하나 더하면 <b>즉시 FAIL</b> 한다 (대조군 C-9).
    /// </remarks>
    [Fact(DisplayName = "G-7a 🔴 요청 본문에 자칭할 칸이 없다 (표 하나뿐)")]
    public void G7a_요청본문에_자칭할_칸이_없다()
    {
        var props = typeof(HitPan.API.Controllers.DeviceController.MainPcChallengeRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Challenge" }, props);
    }

    /// <summary>
    /// 🔴 <b>G-7b — 서비스가 빠지면 문을 <u>막는다</u></b>(fail-closed). 열어 두는 쪽으로 실패하지 않는다.
    /// </summary>
    /// <remarks>⇒ DI 미구성 시 통과시키도록 바꾸면 FAIL (대조군 C-8).</remarks>
    [Fact(DisplayName = "G-7b 🔴 판정 서비스가 없으면 문을 막는다 (fail-closed)")]
    public void G7b_서비스가_없으면_막는다()
    {
        Assert.False(MainPcOnlyAttribute.IsMainPc(ViaTunnel(svc: null, pass: "무엇이든")),
            "판정 서비스가 DI 에 없을 때 통과했다 — 구성이 빠지면 자료관리가 통째로 열린다.");
    }

    /// <summary>
    /// 🔴 <b>G-7c — 자칭 경로가 꺼져 있다</b> (<c>DeviceAuthGate.JoinServerRowAsync</c> · 작2 절E).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 실측 ④의 <b>현장</b>이다. 이 함수는 <b>접속한 자리를 한 번도 보지 않았다</b> —
    /// 대표 계정이면 집·카페 어디서든 메인PC 줄로 갈아탔고, 그 순간 자료관리가 열렸다.
    /// 본문은 <b>지우지 않았다</b>(#1·#37) — 껐을 뿐이다. 그래서 <b>되살아나는 것을 지켜야 한다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>이것은 소스 회귀 감시이고 동작 시험이 아니다.</b> <c>.razor</c> 는 bUnit 없이 띄울 수 없다.
    /// ⇒ 초록불을 <i>"자칭이 막혔다"</i> 로 읽지 마라. <b>"껐다는 표시가 세 자리에 그대로 있다"</b> 까지가 범위다.
    /// 실제 차단은 실물 실측(G-1b·G-2b)이 확인한다.
    /// </para>
    /// <para>
    /// ⚠️ <b>주석을 먼저 걷어낸다.</b> 안 걷어내면 <i>"이 버튼을 되살리지 마라"</i> 같은 설명문에 적힌
    /// 글자가 코드인 것처럼 잡혀 <b>주석만 고쳐도 통과하는 가짜 게이트</b>가 된다(CSP 게이트 교훈).
    /// ⚠️ <b>문구는 하나도 읽지 않는다</b> — 안내 문구는 M-5 로 아직 초안이다. 구조 3점만 본다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-7c 🔴 메인PC 자칭 경로가 꺼진 채로 있다 (소스 회귀 감시)")]
    public void G7c_자칭경로가_꺼진_채로_있다()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.Web", "Components", "Common", "DeviceAuthGate.razor");
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");

        var code = StripComments(File.ReadAllText(path));

        // ① 꺼짐 표시가 있다. const 가 아니라 static readonly 여야 한다 —
        //    const 면 컴파일러가 뒷부분을 미도달로 보고 경고를 낸다(#19 경고 0).
        Assert.Contains("static readonly bool SelfClaimPathDisabled = true", code);

        // ② 버튼이 꺼짐 표시 안에 들어 있다.
        Assert.Contains("@if (!SelfClaimPathDisabled)", code);

        // ③ 🔴 함수 들머리에서 막는다 — 버튼을 숨기는 것만으로는 부족하다.
        //   화면은 다시 그려질 수 있고, 다음 사람이 이 함수를 다른 곳에서 부를 수도 있다.
        var at = code.IndexOf("JoinServerRowAsync()", StringComparison.Ordinal);
        Assert.True(at >= 0, "JoinServerRowAsync 를 못 찾았다 — 이름이 바뀌었다면 이 게이트를 함께 고쳐라.");

        var body = code[at..];
        var guard = body.IndexOf("if (SelfClaimPathDisabled)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "함수 들머리 차단이 사라졌다 — 자칭 경로가 되살아났다는 뜻이다.");

        var firstCall = body.IndexOf("Http.", StringComparison.Ordinal);
        Assert.True(firstCall < 0 || guard < firstCall,
            "차단이 통신보다 뒤에 있다 — 막기 전에 이미 서버를 부른다.");
    }

    /// <summary>
    /// 🔴 <b>G-7d — 확인되지 않은 표에는 출입증이 나가지 않는다.</b> 자칭의 마지막 통로를 막는 자리다.
    /// </summary>
    /// <remarks>
    /// 표를 발급받아 <b>②(로컬 직결)를 건너뛰고</b> 곧바로 ③을 부르는 것이 자칭의 모양이다.
    /// 클라이언트 PC 는 ②를 통과할 수 없으므로 여기서 끝난다.
    /// ⇒ <c>Outcome == MainPcConfirmed</c> 조건을 지우면 <b>즉시 FAIL</b> 한다 (대조군 C-6).
    /// </remarks>
    [Fact(DisplayName = "G-7d 🔴 ②를 건너뛴 표에는 출입증이 나가지 않는다")]
    public void G7d_확인되지_않은_표에는_출입증이_없다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");

        // ②를 부르지 않는다 = 이 컴퓨터 안에 본체가 있다는 증거가 없다.
        var challenge = svc.IssueChallenge(tenant, session);

        var outcome = svc.Consume(tenant, session, challenge, out var pass);

        Assert.Equal(MainPcProofOutcome.NotProven, outcome);
        Assert.Null(pass);
        Assert.False(MainPcOnlyAttribute.IsMainPc(ViaTunnel(svc, pass)),
            "확인 안 된 표로 문이 열렸다 — 사장님 실측 ④(자칭 통과)가 그대로 돌아온 것이다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-9 — 종전 경로 무회귀
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-9 — <c>localhost</c> 직접 접속은 여전히 통과한다</b>(무회귀 · #1).
    /// </summary>
    /// <remarks>
    /// 이 줄이 깨지면 <b>그 컴퓨터에 앉은 사장님이 자기 자료관리에서 잠긴다.</b>
    /// 8/18 에 실제로 그런 사고가 있었다(형제 게이트 <c>MainPcLocalConsoleGateTests</c>).
    /// ⇒ <c>IsMainPc</c> 의 갈래①(<c>IsLocalConsole</c>)을 지우면 FAIL.
    /// </remarks>
    [Fact(DisplayName = "G-9 🔴 localhost 직접 접속은 종전대로 통과한다 (무회귀)")]
    public void G9_로컬콘솔은_종전대로_통과한다()
    {
        Assert.True(MainPcOnlyAttribute.IsLocalConsole(LocalConsole()));
        Assert.True(MainPcOnlyAttribute.IsMainPc(LocalConsole()),
            "그 컴퓨터에서 직접 연 화면이 막혔다 — 사장님이 자기 자료관리에서 잠긴다.");
    }

    /// <summary>
    /// 🔴 <b>G-9b — 터널을 지나온 요청은 <c>IsLocalConsole</c> 이 아니다.</b> 왕복 ②의 자물쇠다.
    /// </summary>
    /// <remarks>
    /// 🔴 ②는 <b>반드시 <c>IsLocalConsole</c></b> 이어야 한다. <c>IsMainPc</c> 를 쓰면
    /// 출입증을 가진 브라우저가 <b>스스로 출입증을 갱신하는 고리</b>가 생겨
    /// 한 번 새어 나간 출입증이 <b>영원히 산다.</b> 두 판정을 절대 합치지 마라(인계3 §6-1).
    /// </remarks>
    [Fact(DisplayName = "G-9b 🔴 터널 헤더가 있으면 로컬 콘솔이 아니다 (두 판정을 합치지 마라)")]
    public void G9b_터널은_로컬콘솔이_아니다()
    {
        var svc = NewService();
        var tenant = Fresh("t");
        var session = Fresh("s");
        var pass = RealPass(svc, tenant, session);

        // 출입증을 가진 터널 요청 — IsMainPc 는 통과하지만 IsLocalConsole 은 아니어야 한다.
        var tunnel = ViaTunnel(svc, pass);
        Assert.True(MainPcOnlyAttribute.IsMainPc(tunnel));
        Assert.False(MainPcOnlyAttribute.IsLocalConsole(tunnel),
            "출입증만으로 IsLocalConsole 이 통과했다 — 출입증 자가갱신 고리가 열렸다(인계3 §6-1).");

        // X-Forwarded-For 만 있어도 마찬가지다.
        var xff = new DefaultHttpContext();
        xff.Request.Headers["X-Forwarded-For"] = "10.0.0.5";
        xff.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.False(MainPcOnlyAttribute.IsLocalConsole(xff));
    }

    // ══════════════════════════════════════════════════════════════
    // 🚨 G-1c — ②가 미들웨어를 지나 컨트롤러까지 닿는가 (20260922작3 P0 봉합)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🚨 <b>G-1c — 토큰 없는 ②가 <c>TenantMiddleware</c> 를 지나 컨트롤러에 닿는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>무엇이 났나</b> (2026-09-22 실측) — `/api/devices/mainpc-proof` 가 <b>401 · 컨트롤러 도달 안 함</b>이었다.
    /// ②는 브라우저가 자기 PC 안의 히트판을 두드리는 길이라 <b>설계상 토큰을 싣지 않는데</b>
    /// (<c>hitpan-mainpc-proof.js</c> 의 <c>credentials:'omit'</c>), 이 미들웨어가 <b>먼저 잘랐다.</b>
    /// ⇒ <c>confirmed</c> 가 영원히 <c>false</c> ⇒ <b>모든 PC 가 클라이언트로 판정</b> ⇒ 자료관리가 안 열린다.
    /// 사장님이 보신 증상 ②③ 이 <b>하나도 봉합되지 않은 상태</b>였다.
    /// </para>
    /// <para>
    /// 🔴 <b>같은 파일에서 세 번째 사고다</b>(작10 <c>update-status-local</c> · 작12 <c>update-consent-local</c>).
    /// <c>[AllowAnonymous]</c> 는 <b>이 미들웨어보다 뒤</b>라 소용이 없다.
    /// </para>
    /// <para>
    /// 🔴 <b>이 게이트가 왜 따로 필요한가</b> — 이 파일의 다른 게이트 전부가 이 결함을 <b>못 잡았다.</b>
    /// 나머지는 <c>MainPcOnlyAttribute</c>·<c>MainPcProofService</c> 를 <b>따로</b> 부른다.
    /// <b>게이트가 초록인 것과, 실제 요청이 그 함수까지 닿는 것은 다른 질문이다.</b>
    /// ⇒ 이 게이트만 <b>문을 실제로 지나가 본다.</b>
    /// </para>
    /// <para>
    /// 🟢 <b>음성 대조군을 안에 넣었다</b> — ③·등록은 <b>여전히 401</b> 이어야 하고, 없는 길도 401 이어야 한다.
    /// 그래야 <i>"`/api/devices` 를 통째로 열어 버렸다"</i> 를 이 게이트가 잡는다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-1c 🚨 토큰 없는 ②는 문을 지나간다 · ③과 등록은 여전히 막힌다 (음성 대조군 포함)")]
    public async Task G1c_토큰없는_왕복확인만_문을_지나간다()
    {
        // 🟢 지나가야 하는 것 — ②뿐이다.
        var (status, reached) = await ThroughTenantGateAsync("/api/devices/mainpc-proof");
        Assert.True(reached,
            $"②(mainpc-proof)가 컨트롤러에 닿지 못했다(status={status}). "
          + "TenantMiddleware 화이트리스트에서 이 주소가 빠졌다는 뜻이고, 그러면 왕복 증명은 "
          + "실물에서 절대 완성되지 않는다 — 모든 PC 가 클라이언트로 판정된다(2026-09-22 P0).");

        // 🔴 막혀야 하는 것 — 통째로 열지 않았음을 지킨다.
        foreach (var mustBlock in new[]
        {
            "/api/devices/mainpc-verify",     // ③ — 세션 대조가 걸리는 자리다. 토큰이 반드시 있어야 한다
            "/api/devices/mainpc-register",   // 등록 — 대표(부모계정) 확인이 걸리는 자리다
            "/api/devices",                   // 기기 목록
            "/api/devices/zzz-없는-길",       // 없는 길
        })
        {
            var (blockedStatus, blockedReached) = await ThroughTenantGateAsync(mustBlock);
            Assert.False(blockedReached,
                $"{mustBlock} 가 토큰 없이 지나갔다(status={blockedStatus}) — "
              + "/api/devices 를 통째로 열어 버렸다는 뜻이다. 이 주소 하나만 열어야 한다.");
            Assert.Equal(StatusCodes.Status401Unauthorized, blockedStatus);
        }
    }

    /// <summary>
    /// 인증 없는 요청을 <b>실제 <c>TenantMiddleware</c> 에 통과시켜</b> 본다.
    /// </summary>
    /// <remarks>
    /// 🟢 글자를 읽지 않는다 — 미들웨어를 <b>불러서</b> 다음 단계가 실행됐는지를 본다.
    /// ⚠️ DB·포트·설치본 무접촉이다. 뒤 단계는 세우지 않는다(닿았는지만 본다).
    /// </remarks>
    private static async Task<(int status, bool reachedNext)> ThroughTenantGateAsync(string path)
    {
        var reached = false;
        var middleware = new HitPan.API.Middleware.TenantMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "POST";
        // 브라우저가 자기 PC 를 두드린 그 모양 — 터널 헤더 없음 · 루프백.
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;

        await middleware.InvokeAsync(ctx, new HitPan.Infrastructure.Security.CurrentTenant());
        return (ctx.Response.StatusCode, reached);
    }

    // ══════════════════════════════════════════════════════════════
    // G-13a — 이 컴퓨터가 봉인하지 않은 키는 풀리지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-13a — 남의 봉인키는 이 PC 에서 안 풀린다.</b> 그 <b>안 풀리는 것</b>이 곧
    /// <i>"컴퓨터가 바뀌었다"</i> 신호다(G-14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🟢 <b>실물 <see cref="TpmKeyService"/> 를 쓴다</b> — <c>UnsealKey</c> 는 <b>비파괴</b>다(열기만 한다).
    /// 🚨 <c>SealKey</c> 는 부르지 않는다. <c>CngKey.Create(..., OverwriteExistingKey)</c> 이므로
    /// <b>이 PC 의 TPM 키를 덮어쓴다.</b> 이 PC 는 정식설치 실물이다(#39).
    /// </para>
    /// <para>
    /// ⚠️ <b>이 시험이 못 하는 것</b> — 임의 바이트가 안 풀리는 것은
    /// <b><i>다른 PC 의 봉인</i>이 안 풀리는 것과 같지 않다</b>(G-13b). 그것은 실물 실측이다.
    /// 여기서 초록불이라고 <i>"키가 PC 에 묶였다"</i> 로 적지 마라.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-13a 🔴 이 컴퓨터가 봉인하지 않은 키는 풀리지 않는다")]
    public void G13a_남의_봉인키는_안_풀린다()
    {
        var tpm = new TpmKeyService(NullLogger<TpmKeyService>.Instance);

        var foreign = new byte[256];
        System.Security.Cryptography.RandomNumberGenerator.Fill(foreign);

        Assert.False(tpm.IsSealedKeyValid(foreign),
            "이 컴퓨터가 봉인하지 않은 바이트가 유효하다고 나왔다 — 「컴퓨터가 바뀌었다」를 영영 감지할 수 없다.");

        Assert.ThrowsAny<Exception>(() => tpm.UnsealKey(foreign));
    }

    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }

        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>주석을 걷어낸다 — 설명문이 판정에 끼면 <b>주석만 고쳐도 통과</b>한다.</summary>
    private static string StripComments(string src)
    {
        // Razor 주석 @* ... *@ (여러 줄)
        var noRazor = System.Text.RegularExpressions.Regex.Replace(
            src, @"@\*.*?\*@", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        // C# 한 줄 주석
        return string.Join('\n', noRazor
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }
}
