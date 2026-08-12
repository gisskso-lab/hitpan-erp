using System.Text.RegularExpressions;
using HitPan.Application.Services;
using HitPan.Application.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>공급자 배선</b> 시험 — 고른 공급자로 <b>실제로 호출이 나가는지</b> 본다
/// (검증팀 P0-1 적발 봉합, 2026-08-12 · 20260812작1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 겪고서</b> — 3사 확장 1차 구현에서 <c>AiProviderFactory</c> 를 만들어 놓고
/// <b>실제 대화 경로에 연결하지 않았다.</b> 고객이 화면에서 챗GPT 를 고르면
/// "챗GPT 를 사용하도록 변경했습니다" 라고 답하는데 <b>대화는 계속 클로드로 나갔다.</b>
/// 화면·DB·연결확인이 전부 정상으로 보여서 <b>겉으로는 아무 문제가 없었다.</b>
/// </para>
/// <para>
/// 🔴 <b>왜 기존 시험이 못 잡았나</b> — 그때 만든 게이트 24건은 전부
/// ①순수함수(<c>Normalize</c>/<c>DisplayName</c>) ②DDL·마이그 파일의 문자열 존재 검사
/// ③소스 정규식 검사였다. <b>서비스를 실행하는 시험이 0건</b>이라,
/// 팩토리를 아무 데서도 안 쓰게 만들어도 267건이 전부 통과했다.
/// "게이트를 깨뜨려 봤다"던 것도 순수함수를 깨뜨려 순수함수 시험이 걸린 것이라,
/// <b>배선 결함을 잡는 능력은 증명하지 못했다.</b>
/// </para>
/// <para>
/// ⇒ 그래서 이 시험은 <b>팩토리를 실제로 돌려</b> 어느 어댑터가 선택되는지 확인한다.
/// 배선이 끊기면 여기서 걸린다.
/// </para>
/// </remarks>
public class AiProviderRoutingTests
{
    private static AiProviderFactory BuildFactory(
        out AnthropicChatProvider claude,
        out OpenAiChatProvider gpt,
        out GeminiChatProvider gemini)
    {
        // 어댑터는 HttpClientFactory 만 있으면 만들어진다(호출 전까지 통신 없음).
        var httpFactory = new Mock<IHttpClientFactory>();

        claude = new AnthropicChatProvider(
            httpFactory.Object, NullLogger<AnthropicChatProvider>.Instance);
        gpt = new OpenAiChatProvider(
            httpFactory.Object, NullLogger<OpenAiChatProvider>.Instance);
        gemini = new GeminiChatProvider(
            httpFactory.Object, NullLogger<GeminiChatProvider>.Instance);

        return new AiProviderFactory(
            claude, gpt, gemini, NullLogger<AiProviderFactory>.Instance);
    }

    // ─────────────────────────────────────────────────────────────
    // 1) 고른 공급자의 어댑터가 실제로 선택되는가
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void 챗GPT를_고르면_챗GPT_어댑터가_나온다()
    {
        var factory = BuildFactory(out _, out var gpt, out _);

        var resolved = factory.Resolve(AiProviderIds.OpenAi);

        // 🔴 여기가 P0-1 의 심장이다. 같은 인스턴스여야 실제 호출이 챗GPT 로 나간다.
        Assert.Same(gpt, resolved);
        Assert.IsType<OpenAiChatProvider>(resolved);
    }

    [Fact]
    public void 제미나이를_고르면_제미나이_어댑터가_나온다()
    {
        var factory = BuildFactory(out _, out _, out var gemini);

        var resolved = factory.Resolve(AiProviderIds.Google);

        Assert.Same(gemini, resolved);
        Assert.IsType<GeminiChatProvider>(resolved);
    }

    [Fact]
    public void 클로드를_고르면_클로드_어댑터가_나온다()
    {
        var factory = BuildFactory(out var claude, out _, out _);

        var resolved = factory.Resolve(AiProviderIds.Anthropic);

        Assert.Same(claude, resolved);
        Assert.IsType<AnthropicChatProvider>(resolved);
    }

    [Fact]
    public void 세_공급자는_서로_다른_어댑터로_간다()
    {
        // 하나라도 같은 곳을 가리키면 "골라도 안 바뀌는" 사고다.
        var factory = BuildFactory(out _, out _, out _);

        var a = factory.Resolve(AiProviderIds.Anthropic);
        var o = factory.Resolve(AiProviderIds.OpenAi);
        var g = factory.Resolve(AiProviderIds.Google);

        Assert.NotSame(a, o);
        Assert.NotSame(a, g);
        Assert.NotSame(o, g);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("모르는값")]
    public void 모르는_값이면_클로드_어댑터로_떨어진다(string? given)
    {
        // 무회귀 — 값이 이상해도 기존 고객은 종전대로 동작해야 한다.
        var factory = BuildFactory(out var claude, out _, out _);

        Assert.Same(claude, factory.Resolve(given));
    }

    // ─────────────────────────────────────────────────────────────
    // 2) 대화 경로가 팩토리를 실제로 타는가 (소스 배선 검사)
    //    🔴 P0-1 이 정확히 여기서 끊겨 있었다.
    // ─────────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    private static string ReadChatbotService()
        => File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HitPan.Infrastructure", "Services", "ChatbotService.cs"));

    /// <summary>메서드 본문만 잘라낸다 — 파일 전체를 보면 다른 메서드가 걸려 오탐이 난다.</summary>
    private static string MethodBody(string source, string signature)
    {
        var idx = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(idx > 0, $"소스에서 '{signature}' 를 못 찾았다 — 메서드가 사라졌거나 이름이 바뀌었다.");

        var after = source[idx..];
        var end = after.IndexOf("\n    }", StringComparison.Ordinal);
        return end > 0 ? after[..end] : after;
    }

    [Fact]
    public void 일반대화_경로가_선택된_공급자를_탄다()
    {
        // 🔴 이 시험이 P0-1 을 잡는다.
        //    TryProviderAnswerAsync 가 ①현재 공급자를 읽고 ②그 공급자 어댑터로 호출해야 한다.
        var body = MethodBody(ReadChatbotService(), "private async Task<ChatProviderResult?> TryProviderAnswerAsync");

        Assert.True(
            body.Contains("GetActiveProviderAsync", StringComparison.Ordinal),
            "일반 대화 경로가 '지금 고른 공급자' 를 읽지 않는다 — 화면에서 골라도 안 바뀐다.");

        Assert.True(
            body.Contains("_providerFactory.Resolve", StringComparison.Ordinal),
            "일반 대화 경로가 공급자 팩토리를 타지 않는다 — 고른 공급자로 호출이 안 나간다.");

        // 컬럼을 anthropic 으로 고정해 읽고 있으면 다른 공급자 키를 영영 못 쓴다.
        Assert.False(
            body.Contains("anthropic_api_key_encrypted", StringComparison.Ordinal),
            "일반 대화 경로가 클로드 키 컬럼을 고정으로 읽는다 — 3사 확장이 동작하지 않는다.");
    }

    [Fact]
    public void 대화경로가_공급자를_문자열로_박아넣지_않는다()
    {
        // 🔴 재검증(2026-08-12)에서 검증팀이 **우회에 성공**해서 추가한 시험이다.
        //    위 시험은 "GetActiveProviderAsync 와 _providerFactory.Resolve 가 본문에 있나" 만 봤다.
        //    그래서 아래 한 줄을 덧붙이면 세 Assert 를 전부 만족하면서 P0-1 이 완전히 되살아났다:
        //
        //        adapter = _providerFactory.Resolve("anthropic");   // ← 이 한 줄
        //
        //    검사 대상 문자열이 그대로 남아 있으니 시험이 못 봤다. 284건 전부 통과했다.
        //    ⇒ Resolve 에 **리터럴을 넘기는 것 자체를 금지**한다. 공급자는 항상 읽어온 값이어야 한다.
        var source = ReadChatbotService();

        // Resolve("...") / Resolve( "..." ) 형태 — 따옴표 리터럴을 넘기는 모든 호출.
        var literalCalls = Regex.Matches(source, @"_providerFactory\s*\.\s*Resolve\s*\(\s*""[^""]*""\s*\)");

        Assert.True(
            literalCalls.Count == 0,
            $"공급자를 문자열로 고정해 Resolve 를 부르는 곳이 {literalCalls.Count}군데 있다 — "
            + "화면에서 무엇을 고르든 그 공급자로만 나간다(P0-1 재발). "
            + $"발견: {string.Join(" / ", literalCalls.Select(m => m.Value))}");
    }

    [Fact]
    public void 대화경로는_어댑터를_한_번만_정한다()
    {
        // 위 우회는 adapter 를 **두 번 대입**해 뒤엣것으로 덮는 수법이었다.
        //   var adapter = _providerFactory.Resolve(provider);
        //   adapter = _providerFactory.Resolve("anthropic");   // ← 덮어쓰기
        // 리터럴이 아니어도(예: Resolve(someOtherVar)) 같은 수법이 통하므로, 대입 횟수 자체를 고정한다.
        var body = MethodBody(ReadChatbotService(), "private async Task<ChatProviderResult?> TryProviderAnswerAsync");

        var assignments = Regex.Matches(body, @"\badapter\s*=");

        Assert.True(
            assignments.Count == 1,
            $"대화 경로에서 adapter 대입이 {assignments.Count}번이다 — "
            + "한 번 고른 어댑터를 뒤에서 덮어쓰면 고객이 고른 공급자가 무시된다(P0-1 재발 수법).");
    }

    [Fact]
    public void AI직원_경로의_클로드고정은_이유가_적혀있다()
    {
        // AI 직원(Tool Use)은 클로드 고정이 맞다 — 신규 2사가 Tool Use 를 못 하기 때문.
        // 다만 '조용한 고정' 은 다음 사람이 결함으로 오해하거나, 반대로 결함을 정상으로 오해한다.
        // ⇒ 왜 고정인지가 코드에 적혀 있어야 한다.
        var body = MethodBody(ReadChatbotService(),
            "private async Task<HitPan.Application.Services.Ai.AgentRunResult?> TryRunAgentAsync");

        Assert.True(
            body.Contains("의도적으로", StringComparison.Ordinal)
            || body.Contains("실수가 아니다", StringComparison.Ordinal),
            "AI 직원 경로가 클로드 고정인 이유가 코드에 없다 — 다음 사람이 결함으로 오해한다.");
    }

    // ─────────────────────────────────────────────────────────────
    // 3) 연결 확인 실패를 키 문제와 일시 장애로 구분하는가 (P1-2)
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(401, true)]   // 키 거부
    [InlineData(403, true)]   // 키 거부
    [InlineData(400, true)]   // 제미나이는 틀린 키에 400 을 준다(2026-08-12 실측)
    [InlineData(429, false)]  // 한도 초과 — 키는 멀쩡하다
    [InlineData(500, false)]  // 상대 서버 장애
    [InlineData(503, false)]  // 상대 서버 장애
    public void 키거부와_일시장애를_구분한다(int statusCode, bool expectedAuthFailure)
    {
        var result = ChatProviderResult.Fail(statusCode);

        Assert.Equal(expectedAuthFailure, result.IsAuthFailure);
    }

    [Fact]
    public void 응답을_못받은_실패는_키문제가_아니다()
    {
        // 네트워크 끊김·타임아웃 — 상태코드가 없다. 이걸 키 문제로 처리하면
        // 잠깐 끊긴 사이 [연결 확인] 을 누른 고객의 멀쩡한 키가 죽는다.
        var result = ChatProviderResult.Fail();

        Assert.Null(result.StatusCode);
        Assert.False(result.IsAuthFailure);
    }

    [Fact]
    public void 연결확인은_키가_거부됐을때만_invalid를_적는다()
    {
        // 🔴 P1-2 봉합 고정. IsAuthFailure 검사 없이 invalid 를 적으면
        //    일시 장애에도 멀쩡한 키가 죽는다.
        var raw = MethodBody(ReadChatbotService(),
            "public async Task<AiConnectionCheckDto> CheckConnectionAsync");

        // ⚠️ 주석 줄을 걷어내고 본다. 설명문에 'invalid'·IsAuthFailure 를 적었다고
        //    순서를 오판하면 거짓 경보다(2026-08-12 이 시험을 처음 돌렸을 때 실제로 그렇게 걸렸다).
        var body = string.Join("\n",
            raw.Split('\n')
               .Select(l => l.Trim())
               .Where(l => !l.StartsWith("//", StringComparison.Ordinal)));

        var invalidIdx = body.IndexOf("'invalid'", StringComparison.Ordinal);
        Assert.True(invalidIdx > 0, "연결 확인이 실패 상태를 기록하지 않는다.");

        var authIdx = body.IndexOf("IsAuthFailure", StringComparison.Ordinal);
        Assert.True(authIdx > 0, "연결 확인이 키 거부와 일시 장애를 구분하지 않는다.");

        Assert.True(
            authIdx < invalidIdx,
            "invalid 기록이 키 거부 판정보다 먼저 일어난다 — 일시 장애에도 멀쩡한 키가 죽는다.");
    }
}
