using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 외부 AI 반출 게이트 — <b>DB 불필요 몫</b> (20261006작9 §5 G-8 + #36 동시 반영 검사).
/// 동작 측정은 <see cref="ExternalAiExportGateDbTests"/>(CI db-gate 잡)가 맡고,
/// 이 파일은 ①고객 노출 문구(§4-4 초안 · #23 금칙어) ②출하 DDL 동시 반영(#36)
/// ③Tool 등록부 무접촉(#1 · G-5 대조군의 정적 반쪽)을 소스에서 확인한다.
/// </summary>
public sealed class ExternalAiExportGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string ReadSrc(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    // §4-4 초안 문구 — 사장님 확인 전 초안. 확정되면 운영 코드와 이 상수를 **함께** 교체한다.
    private const string NoticeLine1 = "외부 도우미 연결은 이용 안내 동의 절차가 마련된 뒤 열립니다";
    private const string NoticeLine2 = "지금은 연결 정보를 저장해도 회사 자료가 밖으로 나가지 않습니다";

    // #23 금칙어 — 고객 노출 신규 문구에 업체명·개발용어·「AI」 0.
    //   (화면의 기존 제목 「AI 도우미 연동」은 이번 봉합 이전 문구 — 범위 밖, 개발명세서에 기록.)
    private static readonly string[] Forbidden =
    {
        "Anthropic", "OpenAI", "Google", "Claude", "Gemini",
        "클로드", "챗GPT", "제미나이",
        "게이트", "API", "차단", "BYOK", "토큰", "AI", "키"
    };

    // ─────────────────────────────────────────────────────────────
    //  G-8 — 화면 안내 노출 + 금칙어
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void G8a_설정화면에_안내_문구가_상시_노출된다()
    {
        var razor = ReadSrc("src", "HitPan.Web", "Pages", "Settings", "AiAssistantPage.razor");
        Assert.Contains(NoticeLine1, razor);
        Assert.Contains(NoticeLine2, razor);
        // 조건부(@if) 뒤에 숨지 않았는지 — 안내 MudAlert 는 로딩 분기 밖(상시)이어야 한다.
        var idx = razor.IndexOf(NoticeLine1, StringComparison.Ordinal);
        var before = razor[..idx];
        Assert.True(before.LastIndexOf("<SectionCard", StringComparison.Ordinal) < 0,
            "안내 문구가 카드(조건부 렌더 구간) 안으로 들어갔다 — 상시 노출이 아니다");
    }

    [Fact]
    public void G8b_챗봇_KB_only_안내_한_줄이_같은_문구다()
    {
        var svc = ReadSrc("src", "HitPan.Infrastructure", "Services", "ChatbotService.cs");
        // 서버 안내 상수(ExportClosedNotice)가 §4-4 초안 1문장과 같은 글자인가.
        var m = Regex.Match(svc,
            "ExportClosedNotice\\s*=\\s*\\r?\\n?\\s*\"(?<text>[^\"]+)\"");
        Assert.True(m.Success, "ChatbotService.ExportClosedNotice 상수를 찾지 못했다");
        Assert.Equal(NoticeLine1 + ".", m.Groups["text"].Value);
    }

    [Fact]
    public void G8c_고객_노출_신규_문구에_업체명_개발용어_AI_0()
    {
        // 검사 대상은 **이번 봉합의 신규 문구 두 문장** 자체다(#23).
        var notice = NoticeLine1 + " " + NoticeLine2;
        foreach (var word in Forbidden)
        {
            Assert.DoesNotContain(word, notice, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  #36 — 마이그 + 출하 DDL 동시 반영 (DB-126 누락 사고 재발 금지)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void G8d_DB138_마이그와_출하DDL이_함께_있다()
    {
        var mig = ReadSrc("src", "HitPan.API", "Migrations", "SQL", "DB-138_ai_export_consents.sql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS ai_export_consents", mig);
        Assert.Contains("ENGINE=InnoDB", mig);                       // #17
        Assert.DoesNotContain("INSERT INTO ai_export_consents", mig); // 시드 0건

        var ddl = ReadSrc("installer", "hitpan_db_clean.sql");
        Assert.Contains("CREATE TABLE `ai_export_consents`", ddl);   // 표 정의
        Assert.Contains("('DB-138','clean-ddl',1)", ddl);            // schema_migrations 시드
    }

    // ─────────────────────────────────────────────────────────────
    //  #1 — Tool 등록부 정적 제거 안 함(작지 §4-2 · PM 결재 §8-1) — G-5 대조군의 정적 반쪽
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void G8e_Tool_등록부_3줄은_그대로_있다()
    {
        var program = ReadSrc("src", "HitPan.API", "Program.cs");
        Assert.Contains("Tools.SalesProfitabilityTool", program);
        Assert.Contains("Tools.PartnerSearchTool", program);
        Assert.Contains("Tools.CreateDeliveryDraftTool", program);
        // 게이트 등록도 실제로 있어야 한다 — 등록이 빠지면 DI 가 기동에서 터진다(이중 안전).
        Assert.Contains("AddScoped<IExternalAiGate", program);
    }
}
