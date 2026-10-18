using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 갈래 E — <b>협력업체 신청자에게 거짓을 말하는 자리</b>
/// (G-B1-E-1 ~ G-B1-E-5 · 작지서 §14).
///
/// <para><b>무엇이 문제였나.</b> 발송기는 자격증명이 안 들어와 있으면 <b>예외를 던지지 않고
/// <c>false</c> 를 돌려준다</b>. 컨트롤러는 그 <c>bool</c> 을 받지 않았고 <c>catch</c> 는 예외만 잡았다.
/// ⇒ 메일이 0통인데 신청자는 <i>"이메일로 발송되었습니다"</i> 를 읽었다.
/// 버려지는 반환값보다 나쁘다 — 버리는 것은 우리가 모르는 것이고, 이것은 우리가 틀린 것을
/// <b>손님에게 통보하는 것</b>이다.</para>
///
/// <para><b>글자가 아니라 동작을 잰다.</b> 소스를 읽어 호출 모양을 세는 자리는 이 파일에 없다.
/// 무는 것은 실물뿐이다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용
///       (<c>reseller_applications</c> 는 <c>00_backoffice_core.sql</c> 몫).</item>
/// <item>판정 과녁 = 실물 <see cref="ResellerApplicationController"/> 의 <c>Submit</c> 을 불러
///       <b>그가 돌려주는 응답 문장</b>을 읽는다.</item>
/// <item>G-B1-E-1·E-2·E-4 의 발송기는 <b>실물 <see cref="EmailSender"/></b> 다 — 자격증명을 한 칸도
///       안 준 구성으로 세워 <b>사고 당시의 기전(예외 0 · <c>false</c> 반환)</b> 을 그대로 재현한다.
///       🔴 메일 실발송 0통(붙을 곳이 없으니 보낼 수도 없다).</item>
/// </list></para>
///
/// <para>🔴 <b>반환값을 버렸는지는 소스가 아니라 두 경로의 응답으로 잰다</b>(G-B1-E-4) —
/// 첨부 있는 호출과 첨부 없는 호출은 <b>서로 다른 메서드</b>라, 한쪽만 반환값을 받으면
/// 그 경로의 응답이 다시 거짓이 된다. 두 경로를 각각 태워 둘 다 사실인지 본다.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 조용한 초록은 못 낸다.</para>
/// </summary>
public sealed class BackofficeResellerMailTruthGateTests
{
    /// <summary>종전 문구 — 보냈을 때만 나가야 하는 문장(#1 덮어쓰기 금지로 그대로 보존됐다).</summary>
    private const string SentPhrase = "가입계약서 초안이 이메일로 발송되었습니다";

    /// <summary>🔴 안 갔는데 나가면 거짓이 되는 낱말. "발송됩니다"(앞날의 약속)와 구분된다.</summary>
    private const string PastTenseSent = "발송되었";

    /// <summary>
    /// 🔴 손님이 읽는 문장에 섞이면 안 되는 개발 용어(#23).
    /// 주석으로 적는 것은 코드가 아니므로 <b>게이트로</b> 막는다(PM 재결재 조건 10).
    /// </summary>
    private static readonly string[] DevWords =
    {
        "SMTP", "smtp", "Smtp", "메일 서버", "메일서버", "서버", "설정", "미설정", "구성",
        "오류", "에러", "error", "Error", "예외", "Exception", "코드", "null", "false",
        "로그", "API", "DB", "데이터베이스", "Claude", "AI",
    };

    // ══════════════════════════════════════════════════════════════
    // G-B1-E-1 · G-B1-E-2 · G-B1-E-5 — 자격증명 미저장으로 신청 1건 (첨부 있는 경로)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-E-1·2·5 메일이 안 갔으면 「발송되었습니다」를 말하지 않는다 · 접수는 그대로 성공")]
    public async Task G_B1_E_1_안_갔으면_발송되었다고_말하지_않는다()
    {
        const string gate = "G-B1-E-1 신청자 문구 정직성";
        await using var env = await MailTruthGateDb.TryCreateAsync(gate, "hitpan_bo_b1e_gate_1");
        if (env is null) return;

        // 🔴 실물 발송기 + 자격증명 0칸 = 사고 당시의 기전. 예외가 아니라 false 가 돌아온다.
        var sender = new SpyEmailSender(RealSenderWithoutCredentials());
        var controller = NewController(env, sender, new FakePdfGenerator(new byte[] { 1, 2, 3 }));

        var res = await controller.Submit(NewRequest("정직상사"), default);

        Assert.True(StatusOf(res) == 200, $"{gate} 접수가 200 이 아니다. {Describe(res)}");
        var value = ValueOf(res);

        // G-B1-E-2 — 메일이 실패해도 접수는 성공이고 신청번호는 그대로 나간다(#20).
        Assert.True(Field<bool>(value, "success"), $"{gate} 메일 실패로 신청을 거절했다 — #20 위반이다. {Describe(res)}");
        var appId = Field<string>(value, "applicationId");
        Assert.False(string.IsNullOrWhiteSpace(appId), $"{gate} applicationId 가 비어 나갔다. {Describe(res)}");

        // 접수가 말뿐이 아닌지 표에서 확인한다 — 「성공」이라 말하고 행이 없으면 그것도 거짓이다.
        Assert.Equal(1, await env.CountApplicationAsync(appId));

        // 🔴 발송기는 던지지 않았다(그래서 종전 catch 가 못 잡았다) · 그리고 false 를 돌려줬다.
        Assert.True(sender.Calls == 1, $"{gate} 발송 시도가 1회가 아니다(실측 {sender.Calls}).");
        Assert.True(sender.WithAttachment == 1, $"{gate} 첨부 있는 경로를 안 탔다. 첨부호출 {sender.WithAttachment}.");
        Assert.False(sender.Threw, $"{gate} 발송기가 던졌다 — 이 게이트가 재현하려는 기전은 「던지지 않고 false」다.");
        Assert.False(sender.LastResult, $"{gate} 자격증명 0칸인데 발송기가 true 를 돌려줬다.");

        // G-B1-E-1 — 손님이 읽는 문장에 과거형 발송 주장이 없다.
        var message = Field<string>(value, "message");
        Assert.False(message.Contains(SentPhrase),
            $"{gate} 메일 0통인데 「{SentPhrase}」가 나갔다. 손님에게 거짓을 통보한 것이다.\n  문구: {message}");
        Assert.False(message.Contains(PastTenseSent),
            $"{gate} 「{PastTenseSent}…」 과거형 발송 주장이 남아 있다.\n  문구: {message}");

        // 접수 사실은 여전히 말해 줘야 한다 — 입 닫는 것도 불친절이다.
        Assert.True(message.Contains("접수"), $"{gate} 접수됐다는 사실을 안 알려 준다.\n  문구: {message}");

        // G-B1-E-5 — 개발 용어 0건.
        AssertNoDevWords(gate, message);
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-E-4 — 첨부 없는 경로(SendAsync)도 반환값을 받는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-E-4 첨부 없는 경로(계약서 생성 실패)도 반환값을 받아 사실만 말한다")]
    public async Task G_B1_E_4_첨부없는_경로도_반환값을_받는다()
    {
        const string gate = "G-B1-E-4 두 호출 경로 모두";
        await using var env = await MailTruthGateDb.TryCreateAsync(gate, "hitpan_bo_b1e_gate_4");
        if (env is null) return;

        // 계약서 생성이 깨지면 컨트롤러는 첨부 없는 호출로 간다 — 그쪽 반환값을 버리면 다시 거짓이 된다.
        var sender = new SpyEmailSender(RealSenderWithoutCredentials());
        var controller = NewController(env, sender, new FakePdfGenerator(null));

        var res = await controller.Submit(NewRequest("정직상사2"), default);

        Assert.True(StatusOf(res) == 200, $"{gate} 접수가 200 이 아니다. {Describe(res)}");
        var value = ValueOf(res);

        Assert.True(sender.WithAttachment == 0 && sender.WithoutAttachment == 1,
            $"{gate} 첨부 없는 경로를 안 탔다(첨부 {sender.WithAttachment} · 비첨부 {sender.WithoutAttachment}).");

        Assert.True(Field<bool>(value, "success"), $"{gate} 접수가 실패로 바뀌었다 — #20 위반. {Describe(res)}");
        Assert.False(string.IsNullOrWhiteSpace(Field<string>(value, "applicationId")),
            $"{gate} applicationId 가 비어 나갔다.");

        var message = Field<string>(value, "message");
        Assert.False(message.Contains(SentPhrase),
            $"{gate} 첨부 없는 경로의 반환값을 버렸다 — 메일 0통인데 「{SentPhrase}」가 나갔다.\n  문구: {message}");
        Assert.False(message.Contains(PastTenseSent),
            $"{gate} 첨부 없는 경로에 과거형 발송 주장이 남아 있다.\n  문구: {message}");
        AssertNoDevWords(gate, message);
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-E-3 — 보낸 경로는 종전 문구 그대로 (#1)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-E-3 보낸 경로는 종전 문구 그대로 · G-B1-E-5 개발 용어 0건")]
    public async Task G_B1_E_3_보낸_경로는_종전_문구_그대로()
    {
        const string gate = "G-B1-E-3 성공 경로 문구 보존";
        await using var env = await MailTruthGateDb.TryCreateAsync(gate, "hitpan_bo_b1e_gate_3");
        if (env is null) return;

        // 🔴 실발송은 하지 않는다 — 「보냈다」를 돌려주는 대역으로 성공 경로만 태운다.
        var sender = new SpyEmailSender(new AlwaysSentSender());
        var controller = NewController(env, sender, new FakePdfGenerator(new byte[] { 9 }));

        var res = await controller.Submit(NewRequest("정직상사3"), default);
        var value = ValueOf(res);

        Assert.True(sender.LastResult, $"{gate} 대역이 true 를 안 돌려줬다 — 시험 설정이 틀렸다.");
        Assert.True(Field<bool>(value, "success"), $"{gate} 접수가 성공이 아니다. {Describe(res)}");

        var message = Field<string>(value, "message");
        Assert.True(message.Contains(SentPhrase),
            $"{gate} 실제로 보냈는데 종전 안내가 사라졌다 — 보냈으면 보냈다고 말해야 한다.\n  문구: {message}");
        Assert.True(message.Contains("정식 계약서"),
            $"{gate} 다음 절차 안내가 사라졌다.\n  문구: {message}");
        AssertNoDevWords(gate, message);

        // 🔴 음성 대조 — 두 경로가 같은 문장이면 분기가 없는 것이다(= 봉합 전 상태).
        var failSender = new SpyEmailSender(RealSenderWithoutCredentials());
        var failController = NewController(env, failSender, new FakePdfGenerator(new byte[] { 9 }));
        var failMsg = Field<string>(ValueOf(await failController.Submit(NewRequest("정직상사4"), default)), "message");
        Assert.NotEqual(message, failMsg);
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 G-B1-G-3 — **(첨부 X · 발송 O)** 조합 (갈래 G · P1-3)
    //
    //   적발: 응답 `message` 가 `mailSent` 만 보고 `hasAttachment` 를 안 봤다 ⇒ 이 조합에서
    //     화면은 「가입계약서 초안이 이메일로 **발송되었습니다**」, 같은 건의 **메일 본문**은
    //     「초안은 본사 확인 후 별도로 보내드립니다」 — 서로 반대고 **거짓인 쪽이 화면**이었다.
    //   도달 경로는 실재한다: PDF 생성 실패(컨트롤러 catch) → pdfBytes 널 → 첨부 없는 발송이 **성공**.
    //   종전 게이트는 이 조합을 **0건** 물었다(E-3 = 첨부O·발송O · E-4 = 첨부X·발송X).
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-G-3 메일은 갔고 초안은 안 붙은 경우, 화면 문구가 메일 본문과 같은 말을 한다")]
    public async Task G_B1_G_3_첨부없이_발송된_경우_화면과_메일이_같은_말을_한다()
    {
        const string gate = "G-B1-G-3 (첨부 X · 발송 O) 조합";
        await using var env = await MailTruthGateDb.TryCreateAsync(gate, "hitpan_bo_b1g_gate_3");
        if (env is null) return;

        // 계약서 생성 실패(null) + 발송은 성공(대역) = 바로 그 조합. 실발송 0통.
        var sender = new SpyEmailSender(new AlwaysSentSender());
        var controller = NewController(env, sender, new FakePdfGenerator(null));

        var res = await controller.Submit(NewRequest("정직상사5"), default);

        Assert.True(StatusOf(res) == 200, $"{gate} 접수가 200 이 아니다. {Describe(res)}");
        var value = ValueOf(res);

        // ── ① 조합이 실제로 섰나 — 첨부 없는 경로로 갔고, 발송기는 true 를 돌려줬다
        Assert.True(sender.WithAttachment == 0 && sender.WithoutAttachment == 1,
            $"{gate} 첨부 없는 경로를 안 탔다(첨부 {sender.WithAttachment} · 비첨부 {sender.WithoutAttachment}).");
        Assert.True(sender.LastResult, $"{gate} 대역이 true 를 안 돌려줬다 — 시험 설정이 틀렸다.");

        // ── ② 접수는 성공이고 신청번호는 그대로 나간다(#20)
        Assert.True(Field<bool>(value, "success"), $"{gate} 접수가 실패로 바뀌었다 — #20 위반. {Describe(res)}");
        Assert.False(string.IsNullOrWhiteSpace(Field<string>(value, "applicationId")),
            $"{gate} applicationId 가 비어 나갔다.");

        var message = Field<string>(value, "message");

        // ── ③ 🔴 거짓 금지 — 초안이 안 붙었는데 「초안이 이메일로 발송되었습니다」를 말하면 안 된다
        Assert.False(message.Contains(SentPhrase, StringComparison.Ordinal),
            $"{gate} 초안이 안 붙었는데 화면 문구가 「{SentPhrase}」라고 말한다. " +
            $"같은 건의 메일 본문은 「초안은 별도로 보내드립니다」다 — 서로 반대다.\n  문구: {message}");

        // ── ④ 🔴 완료형 단정 3모양 — **화면 게이트와 같은 한 벌**을 쓴다(§19 판정2).
        //     금지어 목록이 두 벌이 되면 한 벌만 고쳐지는 날이 온다.
        foreach (var shape in BackofficeResellerScreenTruthGateTests.SendCompletedShapes)
        {
            Assert.False(message.Contains(shape, StringComparison.Ordinal),
                $"{gate} 손님 문구에 완료형 단정 「{shape}」이 있다 — 초안은 안 갔다.\n  문구: {message}");
        }

        // ── ⑤ 🔴 화면과 메일이 **같은 말**을 한다 — 이게 이 게이트의 본문이다
        var html = ResellerApplicationController.BuildEmailHtml(
            "정직상사5", "00000000-1111-2222-3333-444444444444", hasAttachment: false);
        Assert.DoesNotContain("첨부된", html);
        Assert.True(message.Contains("담당자 확인 후", StringComparison.Ordinal),
            $"{gate} 메일 본문은 「별도로 보내드린다」고 하는데 화면 문구엔 그 말이 없다.\n  문구: {message}");

        // ── ⑥ 접수 사실·다음 절차는 끊지 않는다(#20)
        Assert.True(message.Contains("신청이 접수되었습니다", StringComparison.Ordinal),
            $"{gate} 접수 성공 안내가 사라졌다.\n  문구: {message}");
        Assert.True(message.Contains("정식 계약서", StringComparison.Ordinal),
            $"{gate} 다음 절차 안내가 사라졌다.\n  문구: {message}");
        AssertNoDevWords(gate, message);

        // ── ⑦ 🔴 음성 대조 — 세 경우가 **서로 다른 문장**이어야 분기가 산 것이다.
        //     분기를 되돌리면(mailSent 만 보면) ③·④ 가 FAIL 하고, 여기서는 첨부O 와 같아진다.
        var attachedSender = new SpyEmailSender(new AlwaysSentSender());
        var attached = Field<string>(ValueOf(await NewController(
            env, attachedSender, new FakePdfGenerator(new byte[] { 9 }))
            .Submit(NewRequest("정직상사6"), default)), "message");

        var pendingSender = new SpyEmailSender(RealSenderWithoutCredentials());
        var pending = Field<string>(ValueOf(await NewController(
            env, pendingSender, new FakePdfGenerator(null))
            .Submit(NewRequest("정직상사7"), default)), "message");

        Assert.NotEqual(attached, message);
        Assert.NotEqual(pending, message);
        Assert.NotEqual(attached, pending);
    }

    // ══════════════════════════════════════════════════════════════
    // 거치대
    // ══════════════════════════════════════════════════════════════

    private static void AssertNoDevWords(string gate, string message)
    {
        foreach (var w in DevWords)
        {
            Assert.False(message.Contains(w, StringComparison.Ordinal),
                $"{gate} 손님 문구에 개발 용어 「{w}」가 섞였다(#23).\n  문구: {message}");
        }
    }

    /// <summary>🔴 자격증명을 한 칸도 주지 않은 실물 발송기 — 사고 당시 기전(예외 0 · false).</summary>
    private static IEmailSender RealSenderWithoutCredentials() =>
        new EmailSender(
            new ConfigurationBuilder().Build(),
            NullLogger<EmailSender>.Instance);

    private static ResellerApplicationController NewController(
        MailTruthGateDb env, IEmailSender email, IContractPdfGenerator pdf) =>
        new(env.Config(), email, pdf, NullLogger<ResellerApplicationController>.Instance);

    private static ResellerApplicationController.ApplyRequest NewRequest(string companyName) => new()
    {
        CompanyName = companyName,
        BizNo = ValidBizNo,
        CeoName = "대표",
        CompanyAddress = "어딘가 1길 2",
        ContactName = "담당",
        ContactTitle = "과장",
        Phone = "010-0000-0000",
        Email = "reseller-gate@example.invalid",
        SalesRegion = "서울",
        SalesYears = 3,
        Reason = "게이트",
        AgreeTerms = true,
        AgreePrivacy = true,
    };

    /// <summary>검증식을 통과하는 가짜 사업자번호(연속 숫자 — 실번호 모양 아님).</summary>
    private const string ValidBizNo = "1234567891";

    private static int StatusOf(IActionResult res) => res switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => 200,
    };

    private static object? ValueOf(IActionResult res) => (res as ObjectResult)?.Value;

    private static T Field<T>(object? value, string name)
    {
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty(name);
        Assert.NotNull(prop);
        return (T)prop!.GetValue(value)!;
    }

    private static string Describe(IActionResult res) =>
        $"status={StatusOf(res)} value={System.Text.Json.JsonSerializer.Serialize(ValueOf(res))}";

    /// <summary>발송 시도를 세는 겉포장 — 안쪽은 실물 발송기 또는 「보냈다」 대역이다.</summary>
    private sealed class SpyEmailSender : IEmailSender
    {
        private readonly IEmailSender _inner;
        public SpyEmailSender(IEmailSender inner) => _inner = inner;

        public int Calls { get; private set; }
        public int WithAttachment { get; private set; }
        public int WithoutAttachment { get; private set; }
        public bool Threw { get; private set; }
        public bool LastResult { get; private set; }

        public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Calls++; WithoutAttachment++;
            return LastResult = await TrackAsync(() => _inner.SendAsync(toEmail, subject, htmlBody, ct));
        }

        public async Task<bool> SendWithAttachmentAsync(
            string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType,
            CancellationToken ct = default)
        {
            Calls++; WithAttachment++;
            return LastResult = await TrackAsync(() => _inner.SendWithAttachmentAsync(
                toEmail, subject, htmlBody, attachment, attachmentFileName, attachmentMimeType, ct));
        }

        private async Task<bool> TrackAsync(Func<Task<bool>> call)
        {
            try
            {
                return await call();
            }
            catch (Exception ex)
            {
                // #15 — 삼키지 않는다. 던졌다는 사실 자체가 판정 재료다(기전이 바뀌었다는 뜻).
                Threw = true;
                Console.Error.WriteLine($"[G-B1-E] 발송기가 던졌다: {ex.GetType().Name} {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>「보냈다」만 돌려주는 대역 — 실발송 0통으로 성공 경로를 태운다.</summary>
    private sealed class AlwaysSentSender : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> SendWithAttachmentAsync(
            string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType,
            CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>계약서 생성 대역 — <c>null</c> 을 주면 던진다(첨부 없는 경로를 태우기 위해).</summary>
    private sealed class FakePdfGenerator : IContractPdfGenerator
    {
        private readonly byte[]? _bytes;
        public FakePdfGenerator(byte[]? bytes) => _bytes = bytes;

        public byte[] CreateResellerContract(ResellerContractInput input) =>
            _bytes ?? throw new InvalidOperationException("[G-B1-E] 계약서 생성 실패 재현");
    }

    /// <summary>격리 DB 한 벌 — 실물 적용기로 <c>installer/backoffice/*.sql</c> 전부 적재.</summary>
    private sealed class MailTruthGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private MailTruthGateDb(string name, bool created) { _name = name; _created = created; }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
                dir = dir.Parent;
            }
            throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — installer/backoffice 를 읽을 수 없다.");
        }

        private static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

        private static string ServerConnString()
        {
            var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
            var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
            var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
            return $"Server={host};Port={port};User={user};Password={pass};"
                 + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
        }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:BackofficeDb"] = ConnString })
            .Build();

        public static async Task<MailTruthGateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
                var lower = name.ToLowerInvariant();
                if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice" or "hitpan_e2e"
                    || (!lower.Contains("test") && !lower.Contains("gate")))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"HITPAN_BO_GATE_DB={name} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
                }
            }

            var created = false;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                if (useOverride)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = name })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{name}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{name}`; "
                        + $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    created = true;
                }
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gateName}] DB 준비 실패({ex.Number}): {ex.Message}");
                DbGateEnvironment.SkipOrFailStrict(gateName);
                return null;
            }

            var env = new MailTruthGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        /// <summary>신청 행이 실제로 들어갔나 — 「접수됐다」가 말뿐이 아닌지 센다.</summary>
        public async Task<int> CountApplicationAsync(string applicationId)
        {
            await using var db = new MySqlConnection(ConnString);
            await db.OpenAsync();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM reseller_applications WHERE application_id = @Id",
                new { Id = applicationId });
        }

        public async ValueTask DisposeAsync()
        {
            if (!_created) return;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작B1갈래E게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
