using System.Text.RegularExpressions;
using HitPan.Application.Services.Ai;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// AI 연동 3사(클로드AI·챗GPT·제미나이) 확장을 지키는 게이트
/// (작업지시서 20260812작1 · 사장님 결재 2026-08-12).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 오더</b> — "기존 : 클로드API만 지원 -&gt; 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게".
/// 화면 표기 결재: <b>클로드AI / 챗GPT / 제미나이</b>.
/// </para>
/// <para>
/// 🔴 <b>이 시험이 지키는 것 3가지</b>
/// <list type="number">
///   <item>기존 클로드 고객의 <b>무회귀</b> — 모르는 값·빈 값은 반드시 클로드AI 로 떨어져야 한다.
///         이게 깨지면 마이그레이션만 돌아도 기존 고객의 도우미가 죽는다.</item>
///   <item>화면 표기 문구 <b>고정</b> — 사장님이 글자 그대로 결재하신 것이다.
///         "Claude AI"·"ChatGPT"·"챗지피티" 로 슬쩍 바뀌면 결재를 벗어난다.</item>
///   <item>컬럼명 조립의 <b>안전</b> — 공급자 문자열이 SQL 에 실리므로,
///         화이트리스트를 벗어난 값이 통과하면 안 된다.</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>왜 시험으로 막나</b> — 공급자는 앞으로 더 늘어난다.
/// 그때 Normalize 의 default 가 실수로 "throw" 나 "그대로 반환" 으로 바뀌면,
/// 빌드는 통과하고 화면도 멀쩡한데 <b>기존 고객만 조용히 죽는다.</b>
/// </para>
/// </remarks>
public class AiProvider3WayGuardTests
{
    // ─────────────────────────────────────────────────────────────
    // 1) 무회귀 — 모르는 값은 전부 클로드AI 로 떨어진다
    //    🔴 이번 작업 최대 리스크(작업지시서 §6). 여기가 이번 시험의 심장이다.
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("모르는값")]
    [InlineData("claude")]        // 사람이 헷갈려 넣기 쉬운 값
    [InlineData("gpt")]           // openai 가 아니다
    [InlineData("gemini")]        // google 이 아니다
    [InlineData("anthropic; DROP TABLE local_subscription")]  // SQL 조립 방어
    public void 모르는_공급자값은_전부_클로드AI로_떨어진다(string? given)
    {
        var actual = AiProviderIds.Normalize(given);

        Assert.Equal(AiProviderIds.Anthropic, actual);
    }

    [Theory]
    [InlineData("anthropic", "anthropic")]
    [InlineData("openai", "openai")]
    [InlineData("google", "google")]
    [InlineData("OpenAI", "openai")]      // 대소문자 섞여도 인식
    [InlineData("  google  ", "google")]  // 앞뒤 공백 허용
    public void 아는_공급자값은_그대로_인식한다(string given, string expected)
    {
        Assert.Equal(expected, AiProviderIds.Normalize(given));
    }

    [Fact]
    public void 정규화_결과는_반드시_지원목록_안에_있다()
    {
        // 어떤 값이 들어오든 SQL 에 실릴 수 있는 값은 이 3개뿐이어야 한다.
        var 별별입력 = new[]
        {
            null, "", "  ", "openai", "OPENAI", "google", "anthropic",
            "'; DELETE FROM local_subscription; --", "../../etc/passwd", "openai_api_key_encrypted"
        };

        foreach (var s in 별별입력)
        {
            var normalized = AiProviderIds.Normalize(s);
            Assert.Contains(normalized, AiProviderIds.All);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 2) 화면 표기 문구 — 사장님 결재분 그대로
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("anthropic", "클로드AI")]
    [InlineData("openai", "챗GPT")]
    [InlineData("google", "제미나이")]
    public void 화면표기는_사장님_결재문구_그대로다(string providerId, string expected)
    {
        Assert.Equal(expected, AiProviderIds.DisplayName(providerId));
    }

    [Fact]
    public void 모르는_공급자의_표기도_클로드AI다()
    {
        // 표기가 빈 문자열이 되면 화면에 이름 없는 탭이 생긴다.
        Assert.Equal("클로드AI", AiProviderIds.DisplayName(null));
        Assert.Equal("클로드AI", AiProviderIds.DisplayName("모르는값"));
    }

    [Fact]
    public void 지원_공급자는_3사이고_클로드가_먼저다()
    {
        // 화면 탭 순서 = 이 순서. 클로드가 1차 표준이므로 맨 앞이어야 한다.
        Assert.Equal(3, AiProviderIds.All.Count);
        Assert.Equal(AiProviderIds.Anthropic, AiProviderIds.All[0]);
        Assert.Contains(AiProviderIds.OpenAi, AiProviderIds.All);
        Assert.Contains(AiProviderIds.Google, AiProviderIds.All);
    }

    // ─────────────────────────────────────────────────────────────
    // 3) 소스 코드 게이트 — 기존 컬럼·클래스를 지운 게 아닌지 본다 (헌법 #1·#37)
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

    [Fact]
    public void 출하DDL에_기존_anthropic_컬럼이_살아있다()
    {
        // 🚨 헌법 #37 — "안 읽힌다 ≠ 잔재". 3사로 늘리면서 기존 컬럼을 지우면
        //    이미 클로드 키를 넣어 쓰던 고객의 키가 통째로 사라진다.
        var ddl = Path.Combine(FindRepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddl), $"출하 DDL 을 못 찾았다: {ddl}");

        var text = File.ReadAllText(ddl);

        Assert.Contains("anthropic_api_key_encrypted", text);
        Assert.Contains("anthropic_api_key_last4", text);
        Assert.Contains("anthropic_key_status", text);
    }

    [Fact]
    public void 출하DDL에_3사_컬럼이_전부_있다()
    {
        var ddl = Path.Combine(FindRepoRoot(), "installer", "hitpan_db_clean.sql");
        var text = File.ReadAllText(ddl);

        // 신규 2사 + 현재 공급자 칸
        foreach (var col in new[]
                 {
                     "openai_api_key_encrypted", "openai_key_status", "openai_key_verified_at",
                     "google_api_key_encrypted", "google_key_status", "google_key_verified_at",
                     "ai_provider"
                 })
        {
            Assert.Contains(col, text);
        }
    }

    [Fact]
    public void 마이그레이션은_멱등하게_작성돼_있다()
    {
        // 고객 PC 에서 두 번 돌 수 있다(업데이트 재시도). IF NOT EXISTS 가 없으면 두 번째에 죽는다.
        var mig = Path.Combine(FindRepoRoot(), "installer", "migrations", "20260812_ai_provider_3way.sql");
        Assert.True(File.Exists(mig), $"마이그레이션 파일을 못 찾았다: {mig}");

        var text = File.ReadAllText(mig);

        // ADD COLUMN 이 나오는 모든 줄에 IF NOT EXISTS 가 붙어 있어야 한다.
        //   ⚠️ 주석(--) 줄은 SQL 이 아니다. 설명문에 "ADD COLUMN" 이라 적었다고 실패시키면
        //      거짓 경보다(2026-08-12 이 시험을 처음 돌렸을 때 실제로 그렇게 걸렸다).
        var addColumnLines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("--", StringComparison.Ordinal))
            .Where(l => l.Contains("ADD COLUMN", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(addColumnLines);
        foreach (var line in addColumnLines)
        {
            Assert.True(
                line.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase),
                $"멱등하지 않은 ADD COLUMN 이 있다 — 두 번째 실행에서 죽는다: {line.Trim()}");
        }

        // 기본값이 anthropic 이어야 기존 고객이 종전과 같게 동작한다.
        Assert.Contains("DEFAULT 'anthropic'", text);
    }

    [Fact]
    public void 신규_공급자는_되는척하지_않는다()
    {
        // 🔴 오늘(2026-08-12) 본사 알림메일이 "안 보내면서 성공(true)" 을 반환하는 것을 P0 로 적발했다.
        //    같은 함정을 신규 공급자에 만들지 않는다.
        //    Tool Use 는 이번 범위가 아니므로(사장님 "챗봇은 나중에"), 성공을 가장하지 말고 Fail 을 반환해야 한다.
        var root = FindRepoRoot();

        foreach (var file in new[] { "OpenAiChatProvider.cs", "GeminiChatProvider.cs" })
        {
            var path = Path.Combine(root, "src", "HitPan.Infrastructure", "Services", file);
            Assert.True(File.Exists(path), $"공급자 파일을 못 찾았다: {path}");

            var text = File.ReadAllText(path);

            // CompleteWithToolsAsync 본문이 ToolUseResponse.Fail() 을 돌려주는지 본다.
            //   ⚠️ 범위를 그 메서드 본문으로 정확히 끊는다. 2026-08-12 이 시험을 처음 돌렸을 때
            //      ① 파일 끝까지 봐서 뒤쪽 DTO 의 Succeeded 대입을 잡고
            //      ② 메서드가 아니라 상단 주석의 이름을 먼저 잡아 경계가 어긋났다. 둘 다 거짓 경보였다.
            //      그래서 "선언 시그니처" 로 찾는다.
            var idx = text.IndexOf("public Task<ToolUseResponse> CompleteWithToolsAsync", StringComparison.Ordinal);
            Assert.True(idx > 0, $"{file} 에 CompleteWithToolsAsync 선언이 없다.");

            var after = text[idx..];
            var end = after.IndexOf("\n    }", StringComparison.Ordinal);   // 메서드 닫는 중괄호
            var body = end > 0 ? after[..end] : after;

            Assert.True(
                body.Contains("ToolUseResponse.Fail()", StringComparison.Ordinal),
                $"{file} 의 Tool Use 가 실패를 정직하게 반환하지 않는다 — '되는 척' 은 금지다.");

            // 본문 안에서 성공을 세우고 있지 않은지 (되는 척 방지)
            Assert.False(
                Regex.IsMatch(body, @"Succeeded\s*=\s*true"),
                $"{file} 의 Tool Use 경로가 성공을 가장하고 있다.");
        }
    }

    [Fact]
    public void 기존_클로드_어댑터는_그대로_남아있다()
    {
        // 헌법 #1 — 덮어쓰기 금지. 3사로 늘리면서 기존 어댑터를 갈아엎지 않았는지 본다.
        var path = Path.Combine(FindRepoRoot(), "src", "HitPan.Infrastructure", "Services", "AnthropicChatProvider.cs");
        Assert.True(File.Exists(path), "기존 클로드 어댑터가 사라졌다 — 헌법 #1 위반.");

        var text = File.ReadAllText(path);
        Assert.Contains("api.anthropic.com", text);
        Assert.Contains("class AnthropicChatProvider", text);
    }
}
