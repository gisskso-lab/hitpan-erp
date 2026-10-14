using System.Collections.Concurrent;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 갈래 B · G3 — 가입 토큰 <b>평문 로그</b> 봉합 (CodeQL 경보 1665 · 진짜 high 1건).
/// (G-B1-B-6 ~ G-B1-B-8)
///
/// <para><b>적발 사실</b>: <c>LandingSignupController</c> 가 <c>req.SignupToken</c> <b>원문</b>을
/// <c>token={Token}</c> 으로 로그에 남겼다. 이 토큰 하나면 남의 가입 건을 결제확인까지 끌고 갈 수 있다.</para>
///
/// <para><b>같은 레포에 정반대 선례가 있다</b> — <c>TelemetryController.cs:106</c> 주석
/// <i>"원문은 여기서만 쓰고 절대 저장·로깅하지 않는다"</i> + <c>:118</c> 이 해시만 남긴다.
/// 그 규범을 따랐는지를 잰다.</para>
///
/// <para><b>글자가 아니라 동작을 잰다.</b> G-B1-B-6 은 <b>실물 컨트롤러를 호출해</b> 로그를 받아
/// 그 문자열에 토큰 원문이 있는지 본다. 커넥션을 <b>닫힌 포트</b>로 돌려 예외 경로를 확정시키므로
/// DB 가 필요 없고(<c>build</c> 잡) 바깥으로 나가는 연결도 0 이다.</para>
///
/// <para>DB 불필요 ⇒ CI <c>build</c> 잡 <c>Test (Unit + Integration)</c> 전량 실행에 자동으로 포함되고,
/// 이름 등록(<c>BackofficeSignupTokenLogGate</c>)으로 한 번 더 물린다.</para>
/// </summary>
public sealed class BackofficeSignupTokenLogGateTests
{
    // 🔴 토큰 「모양」을 실물에서 베끼지 않는다(조건4). 런타임 Guid 로 그 자리에서 만든다.
    //   컨트롤러는 토큰 모양을 검사하지 않고 그대로 로그에 쓰므로 이 값으로 충분히 잴 수 있다.
    private static string NewProbeToken() => "gate-" + Guid.NewGuid().ToString("N");

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-6 — 실물 컨트롤러 로그에 토큰 원문이 없다 (동작)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-6 실물 ConfirmPayment 가 남긴 로그에 토큰 원문이 0건 · 해시 표식은 남는다")]
    public async Task G_B1_B_6_실물로그에_원문이_없다()
    {
        const string gate = "G-B1-B-6 토큰 원문 로그";
        var token = NewProbeToken();

        var logger = new CapturingLogger<LandingSignupController>();
        var controller = NewController(logger);

        // 커넥션이 닫힌 포트를 보므로 OpenAsync 가 던지고 catch 의 LogError 로 간다 —
        // 그 줄이 바로 봉합 대상(`confirm payment failed token={Token}`)이다.
        var result = await controller.ConfirmPayment(new LandingSignupController.ConfirmPaymentRequest(token), default);
        Assert.NotNull(result);

        var lines = logger.Lines.ToArray();
        Assert.True(lines.Length > 0,
            $"{gate} 로그가 한 줄도 안 남았다 — 경로가 바뀌었다. 게이트가 과녁을 잃었으니 고쳐야 한다.");

        var all = string.Join("\n", lines);

        // 🔴 이 한 줄이 G3 의 전부다. 마스킹을 빼면 원문이 그대로 나타나 여기서 빨강이 된다.
        Assert.False(all.Contains(token, StringComparison.Ordinal),
            $"{gate} 로그에 토큰 원문이 그대로 있다. 남은 로그:\n{all}");

        // 그냥 지운 것과 구별한다 — 조사에 쓸 대조 표식은 남아야 한다.
        Assert.Contains("sha256:", all, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-7 — 마스킹은 단방향이고, 서로 다른 토큰을 구별한다 (동작)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-7 같은 토큰 = 같은 표식 · 다른 토큰 = 다른 표식 · 원문 조각 0건")]
    public async Task G_B1_B_7_표식이_대조에_쓸_수_있다()
    {
        const string gate = "G-B1-B-7 표식 품질";
        var a = NewProbeToken();
        var b = NewProbeToken();

        var markA1 = await MarkOf(a);
        var markA2 = await MarkOf(a);
        var markB = await MarkOf(b);

        Assert.Equal(markA1, markA2);          // 같은 토큰은 같은 표식 — 로그 두 줄을 이을 수 있다
        Assert.NotEqual(markA1, markB);        // 다른 토큰은 다른 표식 — 섞이지 않는다

        // 🔴 원문 조각이 표식에 섞여 있으면 「마스킹한 척」이다. 앞 8자가 들어 있으면 반려.
        Assert.False(markA1.Contains(a[..8], StringComparison.OrdinalIgnoreCase),
            $"{gate} 표식에 토큰 앞부분이 들어 있다 — 가린 게 아니다. 표식={markA1}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-8 — 토큰을 로그 인자로 **직접** 넘기는 자리 0건 (회귀 자물쇠)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-8 LandingSignupController 의 로그 호출에 토큰 원문 플레이스홀더 0건 (grep 숫자)")]
    public void G_B1_B_8_원문_인자가_0건이다()
    {
        const string gate = "G-B1-B-8 원문 인자 0건";
        var src = ReadSource("src/HitPan.Backoffice.API/Controllers/LandingSignupController.cs");

        // _logger.Log*( … ) 한 덩어리씩 떼어, 그 안에 마스킹을 거치지 않은 토큰 인자가 있는지 본다.
        var calls = System.Text.RegularExpressions.Regex.Matches(
            src, @"_logger\.Log\w+\((?:[^;])*?\);",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var offenders = new List<string>();
        foreach (System.Text.RegularExpressions.Match call in calls)
        {
            var text = call.Value;
            // 인자 목록에서 토큰 변수를 쓰면서 MaskToken 을 안 거친 호출을 잡는다.
            var usesToken = System.Text.RegularExpressions.Regex.IsMatch(
                text, @"(?<!MaskToken\()\b(signupToken|req\.SignupToken|signup\.SignupToken)\b");
            if (!usesToken) continue;
            if (!text.Contains("MaskToken(")) offenders.Add(text.Replace("\r\n", " ").Replace("\n", " "));
        }

        Assert.True(offenders.Count == 0,
            $"{gate} 토큰 원문을 그대로 넘기는 로그 호출이 {offenders.Count}곳 남았다:\n- "
            + string.Join("\n- ", offenders));

        // 🔴 음성 대조의 뒷면 — 마스킹이 **실제로 쓰이고 있다**(0곳이면 그냥 로그를 지운 것이다).
        var masked = System.Text.RegularExpressions.Regex.Matches(src, @"MaskToken\(").Count;
        Assert.True(masked >= 4,
            $"{gate} MaskToken 사용이 {masked}곳뿐이다 — 선언 1 + 로그 3곳(접수·결제확인·실패)이 기대값이다.");

        // 선례 규범을 코드가 알고 있는지 — 다음 사람이 왜 해시인지 되묻지 않게.
        Assert.Contains("TelemetryController", src, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════════════════════
    // 도구
    // ══════════════════════════════════════════════════════════════

    /// <summary>실물 컨트롤러를 한 번 돌려 로그에 남은 토큰 표식(`sha256:xxxxxxxxxxxx`)을 꺼낸다.</summary>
    private static async Task<string> MarkOf(string token)
    {
        var logger = new CapturingLogger<LandingSignupController>();
        var controller = NewController(logger);
        await controller.ConfirmPayment(new LandingSignupController.ConfirmPaymentRequest(token), default);

        var all = string.Join("\n", logger.Lines);
        var m = System.Text.RegularExpressions.Regex.Match(all, @"sha256:[0-9a-f]+");
        Assert.True(m.Success, $"로그에서 토큰 표식을 못 찾았다. 남은 로그:\n{all}");
        return m.Value;
    }

    private static LandingSignupController NewController(ILogger<LandingSignupController> logger)
    {
        // 🔴 닫힌 포트 — 바깥으로 나가는 연결 0. 자격증명 「값」이 아니라 자리표시다(#29 · 조건4).
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] =
                "Server=127.0.0.1;Port=1;Database=hitpan_token_gate_nodb;User=gate-placeholder;Password=;"
                + "ConnectionTimeout=2;DefaultCommandTimeout=5;",
            ["License:Pepper"] = "gate-" + Guid.NewGuid().ToString("N")
        }).Build();

        return new LandingSignupController(
            config,
            new NoopEmailSender(),
            new NoopWebhook(),
            new NoopDomainAlias(),
            new NoopCfDomain(),
            logger);
    }

    private static string ReadSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) dir = dir.Parent;
        Assert.True(dir is not null, "HitPan.sln 을 못 찾았다 — 레포 루트를 모른다.");
        var path = Path.Combine(dir!.Parent!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"원본 파일이 없다: {relative}");
        return File.ReadAllText(path);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue($"{logLevel}: {formatter(state, exception)} {exception?.Message}");
    }

    // ── 대역 — 이 게이트는 메일·웹훅·도메인 축을 재지 않는다. 한 통도 나가지 않는다. ──

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> SendWithAttachmentAsync(string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class NoopWebhook : IWebhookOutboundService
    {
        // #12 — 인터페이스 구현체는 전수. 세 개 다 둔다(10/7 합류 CS0535 전례).
        public Task EmitSubscriptionChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmitDeviceSlotChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmitAccountChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopDomainAlias : IDomainAliasService
    {
        public Task<DomainCheckResult> ValidateAsync(string? alias, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 도메인 축 호출 금지 — ConfirmPayment 경로는 여기 안 온다.");
    }

    // 🔴 바깥으로 나가는 호출은 전부 막는다(#29 인프라 무접촉). 불리면 시험이 터지게 둔다 —
    //   「이 경로가 CF 를 부른다」는 사실 자체가 과녁이 바뀐 신호다.
    private sealed class NoopCfDomain : ICloudflareDomainService
    {
        public bool IsConfigured => false;
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task UpdateTunnelIngressAsync(string tunnelId, string hostname, string originUrl, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
    }
}
