using System.Text.Json;
using HitPan.API.Controllers;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Application.Services.Ai;
using HitPan.Application.Services.Ai.Tools;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-S-01 갈래 A (2026-11-27 · 작업지시서 20261127작 §5 · 설계 §4) — 서버 문 닫힘을 <b>동작</b>으로 잰다. DB 불필요.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>G-A1</b> — MVC 가 HitPan.API 에서 <b>실제로 찾은</b> 액션 목록을 읽는다(글자 대조 아님). 닫힌 10 경로 없음 + 대조군 12 경로 있음.</item>
/// <item><b>G-A2</b> — 대화 분석(<see cref="AiEmployeeAnalysisService"/>)을 「부르면 던지는」 보고서 가짜로 불러 보고서 호출 0 · 안내 결과 · 수익 아닌 질문은 null.</item>
/// <item><b>G-A3</b> — 도구(<see cref="SalesProfitabilityTool"/>)를 같은 가짜로 불러 보고서 호출 0 · 기존 실패 꼴 + 안내 글.</item>
/// </list>
/// 변이 대조(빼면 빨강)는 개발명세서 <c>docs/개발/erp/20261127_W-S-01_갈래A_개발명세서.md</c> 에 런 기록.
/// </remarks>
public sealed class BetaClosedServerGateTests
{
    // ─────────────────────────────────────────────────────────────
    //  G-A1 — MVC 실제 탐색
    // ─────────────────────────────────────────────────────────────

    /// <summary>닫힌 10 경로(01a-6 4 · 01a-10 5 · 01b-06 1) — 「메서드 템플릿」 꼴.</summary>
    public static TheoryData<string> ClosedRoutes => new()
    {
        "GET api/settings",
        "PUT api/settings",
        "POST api/settings/validate-unit-price",
        "POST api/settings/verify-force-edit-password",
        "GET api/ai-settings",
        "PUT api/ai-settings/apikey",
        "PUT api/ai-settings/apikey/{providerId}",
        "PUT api/ai-settings/provider/{providerId}",
        "POST api/ai-settings/check/{providerId}",
        "GET api/reports/sales-profitability",
    };

    /// <summary>
    /// 대조군 — 같은 컨트롤러 · 같은 기능 묶음에서 <b>살아 있어야 하는</b> 경로.
    /// 이게 없으면 「아무것도 못 찾아서 없음」 이 통과로 보인다.
    /// </summary>
    public static TheoryData<string> OpenRoutes => new()
    {
        "GET api/settings/company",
        "PUT api/settings/company",
        "GET api/settings/print-common",
        "PUT api/settings/print-common",
        "GET api/settings/cooltime",                  // 🆕 11/28 쿨타임1b — 자동 로그아웃 세 칸만 연 좁은 주소(루트는 닫힌 채)
        "PUT api/settings/cooltime",                  // 〃
        "POST api/settings/company-image",
        "GET api/settings/company-image-state",
        "DELETE api/settings/company-image",
        "DELETE api/ai-settings/apikey",              // 이미 저장된 키를 지울 길(남김 · P3 ①)
        "DELETE api/ai-settings/apikey/{providerId}", // 〃
        "GET api/finance/purchase-sales-ledger",      // 01b-11 은 화면만 닫음 — 매입매출장이 이 서버를 쓴다
        "POST api/data-reset",                        // 갈래 B 소관 — 갈래 A 는 손대지 않음
        "GET api/reports/sales-ranking",              // 같은 보고서 컨트롤러의 다른 경로
    };

    private static readonly Lazy<HashSet<string>> Discovered = new(DiscoverRoutes);

    /// <summary>
    /// HitPan.API 를 응용 부품으로 얹은 MVC 가 찾은 액션을 「메서드 템플릿」 으로 모은다.
    /// 컨트롤러를 만들지 않는다(생성자 의존 0) — 라우트 표만 읽는다.
    /// </summary>
    private static HashSet<string> DiscoverRoutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(SettingsController).Assembly);
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IActionDescriptorCollectionProvider>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ad in provider.ActionDescriptors.Items)
        {
            var template = ad.AttributeRouteInfo?.Template;
            if (string.IsNullOrEmpty(template))
            {
                continue;
            }

            var methods = (ad.ActionConstraints ?? new List<IActionConstraintMetadata>())
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods);
            foreach (var m in methods)
            {
                set.Add($"{m.ToUpperInvariant()} {template.Trim('/')}");
            }
        }

        return set;
    }

    [Fact(DisplayName = "G-A1-0 MVC 탐색이 실제로 돈다(액션 100개 이상 · 없는 경로는 없음)")]
    public void G_A1_0_탐색이_실제로_돈다()
    {
        var found = Discovered.Value;
        Assert.True(found.Count >= 100, $"G-A1 MVC 가 찾은 액션이 {found.Count} 개뿐이다 — 탐색이 안 돈다.");
        Assert.DoesNotContain("GET api/__gate_nonexistent__", found);
    }

    [Theory(DisplayName = "G-A1-1 닫힌 경로는 MVC 액션 목록에 없다")]
    [MemberData(nameof(ClosedRoutes))]
    public void G_A1_1_닫힌_경로는_없다(string route)
    {
        Assert.True(!Discovered.Value.Contains(route),
            $"G-A1 {route} 가 서버 라우트에 다시 등록됐다 — [NonAction] 한 줄이 빠졌는지 본다(W-S-01 갈래 A).");
    }

    [Theory(DisplayName = "G-A1-2 대조군 경로는 MVC 액션 목록에 있다")]
    [MemberData(nameof(OpenRoutes))]
    public void G_A1_2_대조군은_있다(string route)
    {
        Assert.True(Discovered.Value.Contains(route),
            $"G-A1 대조군 {route} 가 사라졌다 — 닫을 것만 닫아야 한다(company · print-common · company-image · DELETE 2 · 매입매출장).");
    }

    // ─────────────────────────────────────────────────────────────
    //  G-A2 · G-A3 — 판매수익성분석 소비자 2곳
    // ─────────────────────────────────────────────────────────────

    /// <summary>부르면 던지는 보고서 가짜(Strict) — 호출 횟수는 <c>Invocations</c> 로 센다.</summary>
    private static Mock<IReportService> ThrowingReport() => new(MockBehavior.Strict);

    [Fact(DisplayName = "G-A2-1 대화 분석: 수익 분석 명령 → 안내 결과 · 보고서 호출 0")]
    public async Task G_A2_1_수익_분석_명령은_안내만()
    {
        var report = ThrowingReport();
        var svc = new AiEmployeeAnalysisService(report.Object, NullLogger<AiEmployeeAnalysisService>.Instance);

        var result = await svc.TryAnalyzeAsync("이번 달 수익 분석해줘", "t-gate");

        Assert.NotNull(result);
        Assert.Equal("sales-profitability", result!.Kind);
        Assert.Equal(AiEmployeeAnalysisService.SalesProfitabilityClosedNotice, result.Title);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Chart);
        Assert.True(report.Invocations.Count == 0,
            $"G-A2 보고서 함수를 {report.Invocations.Count} 번 불렀다 — 닫힘 깃발 뒤에서 숫자가 나간다.");
    }

    [Fact(DisplayName = "G-A2-4 대화 답: 닫힘 안내 결과 → 안내 글만(「자료가 없습니다 · 다시 말씀해 주세요」 0)")]
    public async Task G_A2_4_닫힘_답은_안내_글만()
    {
        var report = ThrowingReport();
        var svc = new AiEmployeeAnalysisService(report.Object, NullLogger<AiEmployeeAnalysisService>.Instance);
        var result = await svc.TryAnalyzeAsync("이번 달 수익 분석해줘", "t-gate");

        var answer = ChatbotService.ClosedAnalysisAnswer(result!);

        Assert.Equal(AiEmployeeAnalysisService.SalesProfitabilityClosedNotice, answer);
        Assert.DoesNotContain("자료가 없습니다", answer);
        Assert.DoesNotContain("다시 말씀해 주세요", answer);
    }

    [Fact(DisplayName = "G-A2-5 대화 답 대조군: 다른 제목의 빈 결과 · 행 있는 결과 → null(기존 요약 그대로)")]
    public void G_A2_5_닫힘_아닌_결과는_null()
    {
        var empty = new HitPan.Application.DTOs.Chatbot.AiAnalysisResultDto { Kind = "sales-profitability", Title = "이번 달 판매 수익성", Rows = new() };
        Assert.Null(ChatbotService.ClosedAnalysisAnswer(empty));

        var withRows = new HitPan.Application.DTOs.Chatbot.AiAnalysisResultDto
        {
            Kind = "sales-profitability",
            Title = AiEmployeeAnalysisService.SalesProfitabilityClosedNotice,
            Rows = new() { new() { "A", "1" } }
        };
        Assert.Null(ChatbotService.ClosedAnalysisAnswer(withRows));
    }

    [Fact(DisplayName = "G-A2-6 대화 답 결선: BuildAnalysisAnswerAsync 가 닫힘 갈래를 「행 0」 안내보다 먼저 본다(소스 순서)")]
    public void G_A2_6_닫힘_갈래가_행0_안내보다_앞()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "src", "HitPan.Infrastructure")))
            dir = dir.Parent;
        Assert.True(dir is not null, "레포 뿌리를 찾지 못했다");
        var src = File.ReadAllText(Path.Join(dir!.FullName, "src", "HitPan.Infrastructure", "Services", "ChatbotService.cs"));

        var start = src.IndexOf("private async Task<ChatAnswerDto> BuildAnalysisAnswerAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "BuildAnalysisAnswerAsync 시작을 찾지 못했다");
        var call = src.IndexOf("ClosedAnalysisAnswer(analysis)", start, StringComparison.Ordinal);
        var empty = src.IndexOf("else if (analysis.Rows.Count == 0)", start, StringComparison.Ordinal);
        var noData = src.IndexOf("해당 조건에 맞는 거래 자료가 없습니다", start, StringComparison.Ordinal);
        Assert.True(call > start, "BuildAnalysisAnswerAsync 안에서 ClosedAnalysisAnswer 를 부르지 않는다");
        Assert.True(empty > call && noData > empty, "닫힘 갈래가 「행 0」 안내 갈래보다 앞이어야 한다");
    }

    [Fact(DisplayName = "G-A2-2 대화 분석 대조군: 「안녕하세요」 → null · 보고서 호출 0")]
    public async Task G_A2_2_수익_아닌_질문은_null()
    {
        var report = ThrowingReport();
        var svc = new AiEmployeeAnalysisService(report.Object, NullLogger<AiEmployeeAnalysisService>.Instance);

        var result = await svc.TryAnalyzeAsync("안녕하세요", "t-gate");

        Assert.Null(result);
        Assert.Empty(report.Invocations);
    }

    [Fact(DisplayName = "G-A3 도구: 실행 → 기존 실패 꼴 + 안내 글 · 보고서 호출 0")]
    public async Task G_A3_도구는_안내만()
    {
        var report = ThrowingReport();
        var tool = new SalesProfitabilityTool(report.Object, NullLogger<SalesProfitabilityTool>.Instance);
        using var doc = JsonDocument.Parse("{\"view\":\"item\",\"from\":\"2026-01-01\"}");

        var result = await tool.ExecuteAsync(doc.RootElement, new ToolContext { TenantId = "t-gate", UserId = "u-gate" });

        Assert.False(result.Succeeded);
        Assert.Equal(AiEmployeeAnalysisService.SalesProfitabilityClosedNotice, result.Content);
        Assert.Null(result.Analysis);
        Assert.True(report.Invocations.Count == 0,
            $"G-A3 도구가 보고서 함수를 {report.Invocations.Count} 번 불렀다 — 닫힘 깃발 뒤에서 숫자가 나간다.");
        // 등록은 그대로(ExternalAiExportGateTests G8e) — 이름이 바뀌면 등록부 대조가 어긋난다.
        Assert.Equal("sales_profitability", tool.Name);
    }

    [Fact(DisplayName = "G-A2-3 안내 글: 개발 낱말 · 「베타」 · 업체명 0(#23)")]
    public void G_A2_3_안내_글_금칙어_0()
    {
        string[] forbidden = { "베타", "beta", "API", "AI", "토큰", "서버", "404", "NonAction" };
        foreach (var w in forbidden)
        {
            Assert.DoesNotContain(w, AiEmployeeAnalysisService.SalesProfitabilityClosedNotice, StringComparison.OrdinalIgnoreCase);
        }
    }
}
