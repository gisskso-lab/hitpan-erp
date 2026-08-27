using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작8 — <b>서버가 만든 사슬 메시지가 화면까지 도달하는가.</b>
///
/// <para>
/// <b>이 게이트가 왜 필요한가 — 작7 게이트 9건이 전부 통과했는데 사장님이 반려하셨다.</b>
/// 작7 게이트는 <i>"서버 가드가 반품번호를 담은 문장을 만드는가"</i> 를 쟀고, 그건 <b>사실이었다</b>.
/// 그런데 화면이 그 문장을 <b>받지도 보여주지도 않아</b> 사장님께는
/// <i>"삭제 가능한 draft 상태 매입명세가 없습니다"</i> · <i>"삭제에 실패했습니다"</i> 만 떴다.
/// </para>
///
/// <para>
/// 🔴 <b>고쳤다 ≠ 갔다 (6차).</b> 코드는 매번 정상이었고
/// <b>끊긴 건 전달 경로</b>였으며 <b>끊기는 자리만 옮겨갔다.</b>
/// ⇒ 이 게이트는 <b>만드는 쪽을 보지 않는다.</b> 오직 <b>화면이 받아서 쓰는가</b>만 본다.
/// </para>
/// </summary>
public sealed class ErrorMessageDeliveryGateTests
{
    /// <summary>매입라인 삭제 화면 6곳 — 여기 전부가 서버 문장을 표시해야 한다.</summary>
    private static readonly (string Label, string[] Path)[] DeleteScreens =
    {
        ("매입명세서 목록", new[] { "src", "HitPan.Web", "Components", "Purchase", "PurchaseReceiptList.razor.cs" }),
        ("발주서 목록",     new[] { "src", "HitPan.Web", "Components", "Purchase", "PurchaseOrderList.razor.cs" }),
        ("반품 목록",       new[] { "src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor.cs" }),
        ("매입명세서 화면", new[] { "src", "HitPan.Web", "Pages", "Purchase", "PurchaseReceiptPage.razor.cs" }),
        ("발주서 화면",     new[] { "src", "HitPan.Web", "Pages", "Purchase", "PurchaseOrderPage.razor.cs" }),
        ("반품 화면",       new[] { "src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs" }),
    };

    /// <summary>
    /// 🔴 G1 — <b>목록이 삭제 전에 <c>draft</c> 로 미리 거르지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 이게 1-1 반려의 실제 원인이다. 화면이 먼저 걸러내면 확정 건은
    /// <b>DELETE 요청조차 나가지 않아</b> 서버 가드가 실행될 기회가 없다.
    /// 그 결과 화면이 자기 혼자 만든 <i>"삭제 가능한 …없습니다"</i> 가 사장님께 갔다.
    /// <b>막을지 말지는 서버가 정한다.</b>
    /// </remarks>
    [Fact]
    public void G1_목록삭제에_사전필터가_없다()
    {
        foreach (var (label, path) in DeleteScreens.Take(3))
        {
            var body = CodeOnly(Section(TestSource.Read(path), "BulkDeleteAsync"));

            Assert.False(body.Contains("\"draft\"", StringComparison.Ordinal),
                $"{label}: 일괄삭제가 아직 draft 로 미리 거른다 — 서버 가드가 실행되지 못한다.");
            Assert.False(body.Contains("삭제 가능한", StringComparison.Ordinal),
                $"{label}: 화면이 자체 판정 문구를 만든다 — 서버 사유가 사라진다.");
        }
    }

    /// <summary>
    /// 🔴 G2 — <b>삭제 실패 시 응답 본문을 읽는다.</b>
    /// 발주서·매입명세서 화면은 본문을 버리고 고정문구로 덮어썼다(1-2 반려).
    /// </summary>
    [Fact]
    public void G2_삭제실패시_응답본문을_읽는다()
    {
        foreach (var (label, path) in DeleteScreens)
        {
            var code = CodeOnly(TestSource.Read(path));

            Assert.True(code.Contains("ApiErrorText.Extract", StringComparison.Ordinal),
                $"{label}: 서버 응답 본문을 표시하지 않는다 — 전표번호가 사장님께 도달하지 못한다.");
        }
    }

    /// <summary>
    /// 🔴 G3 — <b>고정문구가 사유를 덮지 않는다.</b>
    /// 사장님이 실제로 받으신 그 문장이다.
    /// </summary>
    [Fact]
    public void G3_사유를_덮는_고정문구가_없다()
    {
        foreach (var (label, path) in DeleteScreens)
        {
            var code = CodeOnly(TestSource.Read(path));

            Assert.False(code.Contains("\"삭제에 실패했습니다.\"", StringComparison.Ordinal),
                $"{label}: 고정문구가 서버 사유를 덮어쓴다.");
            // 원문 JSON 을 그대로 뿌리는 것도 금지 — 고객 화면에 개발 흔적이 보이면 안 된다
            Assert.False(code.Contains("삭제 실패: {error}", StringComparison.Ordinal),
                $"{label}: 응답 원문(JSON)이 그대로 화면에 뜬다.");
        }
    }

    /// <summary>
    /// 🔴 G4 — <b><c>ApiErrorText</c> 가 실제로 문장을 꺼낸다.</b>
    /// G2 는 <i>부르는지</i>만 본다 — <b>부르는 것과 제대로 동작하는 건 다르다.</b>
    /// </summary>
    [Fact]
    public void G4_에러문장_추출이_동작한다()
    {
        // 서버 삭제가드가 실제로 보내는 모양
        var real = "{\"message\":\"매입명세서를 삭제할 수 없습니다. 확정된 반품전표(매반-20260826-003)가 연결돼 있습니다.\"}";
        var got = HitPan.Web.Services.ApiErrorText.Extract(real, 400);

        Assert.Contains("매반-20260826-003", got);          // 🔴 전표번호가 살아남는가
        Assert.DoesNotContain("message", got);              // JSON 껍데기가 남으면 안 된다
        Assert.DoesNotContain("{", got);

        // error 키도 받는다
        Assert.Equal("확정된 반품입니다.",
            HitPan.Web.Services.ApiErrorText.Extract("{\"error\":\"확정된 반품입니다.\"}"));

        // 🔴 파싱 불가한 JSON 을 고객 화면에 그대로 뿌리지 않는다
        var junk = HitPan.Web.Services.ApiErrorText.Extract("{\"weird\":123}", 500);
        Assert.DoesNotContain("weird", junk);

        // 본문이 비면 상태코드로 대체
        Assert.Contains("500", HitPan.Web.Services.ApiErrorText.Extract("", 500));
    }

    /// <summary>
    /// 🔴 G5 — <b>정합성 검사 화면이 서버 API 를 실제로 부른다.</b>
    /// 종전엔 검사 15종이 서버에 살아 있는데 <b>부르는 화면이 0건</b>이었다(반려 3).
    /// </summary>
    [Fact]
    public void G5_검산화면이_API를_부른다()
    {
        var code = CodeOnly(TestSource.Read("src", "HitPan.Web", "Pages", "Finance", "IntegrityCheckPage.razor.cs"));

        Assert.Contains("api/finance/integrity-check", code);
        // 🔴 부르기만 하고 버리면 소용없다 — 받은 걸 화면 상태에 담는가
        Assert.Contains("_report =", code);
    }

    /// <summary>
    /// 🔴 G6 — <b>검사 화면에 점수를 띄우지 않는다</b>(헌법 #32).
    /// 서버가 <c>Score</c> 를 주더라도 화면은 안 쓴다 — <i>"92점"</i> 이 <i>"8건 틀림"</i> 을 가린다.
    /// </summary>
    [Fact]
    public void G6_검산화면에_점수가_없다()
    {
        foreach (var file in new[] { "IntegrityCheckPage.razor", "IntegrityCheckPage.razor.cs" })
        {
            var code = CodeOnly(TestSource.Read("src", "HitPan.Web", "Pages", "Finance", file));
            Assert.False(code.Contains("Score", StringComparison.Ordinal),
                $"{file}: 점수 표시는 금지다(헌법 #32).");
        }

        // 수신 모델에도 담지 않는다
        var models = CodeOnly(TestSource.Read("src", "HitPan.Web", "Models", "FinanceModels.cs"));
        var idx = models.IndexOf("class IntegrityReportModel", StringComparison.Ordinal);
        Assert.True(idx >= 0, "IntegrityReportModel 이 없다.");
        var section = models[idx..Math.Min(models.Length, idx + 600)];
        Assert.False(section.Contains("Score", StringComparison.Ordinal), "수신 모델에 Score 를 담지 않는다.");
    }

    /// <summary>
    /// 🔴 G7 — <b>사이드바에 진입점이 있다.</b> 화면만 만들고 길을 안 내면 없는 것과 같다.
    /// </summary>
    [Fact]
    public void G7_사이드바에_진입점이_있다()
    {
        var side = TestSource.Read("src", "HitPan.Web", "Layout", "Sidebar.razor");

        Assert.Contains("/accounting/integrity", side);
        Assert.Contains("정합성 검사", side);

        // 🔴 라우트가 실제 페이지와 맞는가 — 오타 나면 404 다
        var page = TestSource.Read("src", "HitPan.Web", "Pages", "Finance", "IntegrityCheckPage.razor");
        Assert.Contains("@page \"/accounting/integrity\"", page);
    }

    /// <summary>
    /// 🔴 G8 — <b>가드가 죽어 있지 않다</b>(작7 GPG10 승계).
    /// <c>if (false</c> 로 죽여도 낱말이 남아 글자검사는 통과한다.
    /// </summary>
    [Fact]
    public void G8_화면가드가_죽어있지_않다()
    {
        foreach (var (label, path) in DeleteScreens)
        {
            var code = CodeOnly(TestSource.Read(path));

            Assert.DoesNotContain("if (false", code);
            Assert.DoesNotContain("&& false", code);
            Assert.DoesNotContain("|| true", code);
        }
    }

    /// <summary>
    /// 🔴 G9 — <b>대조군.</b> 이 게이트가 "무조건 통과" 로 굴러가지 않는지 확인한다.
    /// </summary>
    /// <remarks>
    /// 대조군이 없으면 경로를 잘못 읽어 <b>빈 문자열</b>을 검사해도 전부 통과한다
    /// (<c>DoesNotContain</c> 은 빈 문자열에서 늘 참이다).
    /// </remarks>
    [Fact]
    public void G9_대조군_검사가_실제로_읽고있다()
    {
        foreach (var (label, path) in DeleteScreens)
        {
            var code = TestSource.Read(path);

            Assert.True(code.Length > 500, $"{label}: 파일을 못 읽었다 — 경로가 바뀌었으면 게이트도 고쳐야 한다.");
            Assert.Contains("Snackbar", code);                       // 화면 맞는지
            Assert.DoesNotContain("절대_없는_문구_XYZZY", code);       // 늘 참이 아닌지
        }
    }

    /// <summary>
    /// 🔴 G10 — <b>사슬 검사가 상태 검사보다 먼저 나온다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>화면을 다 고치고도 사장님은 여전히 번호를 못 받으실 뻔했다.</b>
    /// 서버가 <c>"확정된 매입명세서는 삭제할 수 없습니다"</c> 를 <b>먼저</b> 던지면
    /// 아래 반품 사슬 검사까지 가지 못한다. 사장님 실측 건이 정확히
    /// <i>확정 매입 + 확정 반품 연결</i> 이라 이 순서에 걸려 있었다.
    /// <para>
    /// 둘 다 삭제를 막지만 <b>알려주는 정보량이 다르다</b> — 사슬 쪽이 더 무거운 사실이므로
    /// 먼저 나와야 한다. <b>막는 것과 알려주는 것은 다른 문제다.</b>
    /// </para>
    /// </remarks>
    [Fact]
    public void G10_사슬검사가_상태검사보다_먼저다()
    {
        var src = CodeOnly(TestSource.Read("src", "HitPan.Application", "Services", "PurchaseService.cs"));

        foreach (var (method, chainToken, statusToken) in new[]
        {
            ("DeletePurchaseReceiptAsync", "blockingReturns.Count > 0", "확정된 매입명세서는 삭제할 수 없습니다"),
            ("DeletePurchaseOrderAsync",   "blockingReceipts.Count > 0", "상태의 발주서는 삭제할 수 없습니다"),
        })
        {
            var body = Section(src, method);

            var chainAt = body.IndexOf(chainToken, StringComparison.Ordinal);
            var statusAt = body.IndexOf(statusToken, StringComparison.Ordinal);

            Assert.True(chainAt >= 0, $"{method}: 사슬 검사가 없다.");
            Assert.True(statusAt >= 0, $"{method}: 상태 검사가 없다.");
            Assert.True(chainAt < statusAt,
                $"{method}: 상태 검사가 먼저 나와 사슬 전표번호가 가려진다 — 사장님이 받은 그 화면이다.");
        }
    }

    // ────────────────────────────────────────────────────────────────

    /// <summary>메서드 한 구간만 잘라낸다 — 파일 전체를 보면 다른 메서드 낱말이 잡힌다.</summary>
    private static string Section(string src, string methodName)
    {
        var idx = src.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"{methodName} 을 찾지 못했다.");
        var rest = src[idx..];
        var next = rest.IndexOf("\n    private ", StringComparison.Ordinal);
        var next2 = rest.IndexOf("\n    public ", StringComparison.Ordinal);
        var end = next < 0 ? next2 : (next2 < 0 ? next : Math.Min(next, next2));
        return end > 0 ? rest[..end] : rest;
    }

    /// <summary>주석 줄을 걷어낸다 — 같은 글자가 주석에도 살아 거짓 판정을 만든다.</summary>
    private static string CodeOnly(string src)
    {
        var lines = src.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal)
                     && !l.StartsWith("///", StringComparison.Ordinal)
                     && !l.StartsWith("@*", StringComparison.Ordinal)
                     && !l.StartsWith("*", StringComparison.Ordinal));
        return string.Join("\n", lines);
    }
}
