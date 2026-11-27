using System.Text.RegularExpressions;
using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-S-01 갈래 A (2026-11-27 · 작업지시서 20261127작 §5 · 설계 §4) — 화면 「한 장치」 게이트. DB 불필요.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>G-A4</b> — 규칙 <see cref="BetaClosedRoutes"/> 를 <b>실제로 불러</b> 5 주소 닫힘 · 대조군 열림을 본다.</item>
/// <item><b>G-A5</b> — <b>전수 대조</b>(목록 대조 아님 · 반증 S-1 · U1): Web 전체(.razor · .cs · bin/obj 뺌)에서 닫힌 5 주소 글자가
/// ① 규칙을 거치는 자리 ② 규칙 파일 · 안내 화면 <c>@page</c> ③ 주석(⬛ 원문 보존 줄 포함) 밖에서 하나라도 나오면 빨강.
/// <c>"/settings"</c> 는 <b>정확히 그 글자만</b> 센다(<c>/settings/…</c> 다른 주소는 세지 않음).
/// 덧붙여 5 화면 <c>@page</c> 0 · 사용자정보가 사용환경 설정 서버를 부르지 않음 · 세무사 자료 화면 서버 호출 자리 0 · P5 목적지.</item>
/// </list>
/// ⚠️ 한계(설계 §8 U1): G-A5 는 소스 글자 대조다 — 화면에 실제로 그려지는지는 [4] 화면 실측(메뉴 9줄 0 · 직접 주소 안내 · 메뉴 밖 3곳 누름)이 맡는다.
/// </remarks>
public sealed class BetaClosedRoutesGateTests
{
    private static readonly string[] Closed =
    {
        "/settings",
        "/settings/ai-assistant",
        "/sales/profitability",
        "/tax-invoice-stats",
        "/accounting/export",
    };

    // ─────────────────────────────────────────────────────────────
    //  G-A4 — 규칙을 불러 본다
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-A4-1 닫힌 주소 목록 = 정확히 5개(행 순서)")]
    public void G_A4_1_목록이_정확히_5개다()
    {
        Assert.Equal(Closed, BetaClosedRoutes.All.ToArray());
    }

    [Theory(DisplayName = "G-A4-2 닫힌 주소는 닫힘(쿼리 · 조각 · 끝 빗금 · 대소문자 무관)")]
    [InlineData("/settings")]
    [InlineData("/settings/ai-assistant")]
    [InlineData("/sales/profitability")]
    [InlineData("/tax-invoice-stats")]
    [InlineData("/accounting/export")]
    [InlineData("/Settings/")]
    [InlineData("/settings?tab=1")]
    [InlineData("/accounting/export#top")]
    [InlineData(" /tax-invoice-stats ")]
    public void G_A4_2_닫힌_주소는_닫힘(string href)
    {
        Assert.True(BetaClosedRoutes.IsClosed(href), $"G-A4 {href} 가 닫히지 않았다.");
        Assert.False(BetaClosedRoutes.IsOpen(href));
    }

    [Theory(DisplayName = "G-A4-3 대조군은 열림(같은 갈래 이웃 화면 · 빈 값)")]
    [InlineData("/accounting/purchase-sales")]
    [InlineData("/settings/devices")]
    [InlineData("/print-settings")]
    [InlineData("/data/reset")]
    [InlineData("/settings/mdb-migration")]
    [InlineData("/settings/user-info")]
    [InlineData("/settings/subscription-billing")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    public void G_A4_3_대조군은_열림(string? href)
    {
        Assert.False(BetaClosedRoutes.IsClosed(href), $"G-A4 대조군 {href} 가 닫혔다 — 닫을 것만 닫아야 한다.");
    }

    [Fact(DisplayName = "G-A4-4 안내 글: 개발 낱말 · 「베타」 · 업체명 0(#23)")]
    public void G_A4_4_안내_글_금칙어_0()
    {
        var text = BetaClosedRoutes.NoticeTitle + " " + BetaClosedRoutes.NoticeBody + " " + BetaClosedRoutes.NoticeButton;
        string[] forbidden = { "베타", "beta", "API", "AI", "토큰", "서버", "404", "라우트", "NonAction" };
        foreach (var w in forbidden)
        {
            Assert.DoesNotContain(w, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  G-A5 — Web 전수 대조
    // ─────────────────────────────────────────────────────────────

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

    private static string WebRoot() => Path.Combine(RepoRoot(), "src", "HitPan.Web");

    private static string ReadWeb(params string[] parts) => File.ReadAllText(Path.Combine(new[] { WebRoot() }.Concat(parts).ToArray()));

    private static string BlankKeepLines(Match m) => Regex.Replace(m.Value, @"[^\r\n]", " ");

    /// <summary>
    /// 주석을 걷어낸다 — 줄 번호는 그대로(지운 자리를 빈칸으로). 순서가 중요하다:
    /// Razor 주석 → HTML 주석 → 줄 머리 <c>//</c> 주석 → <c>/* */</c>
    /// (줄 주석 안의 <c>/admin/*</c> 같은 글이 <c>/*</c> 로 읽혀 뒤를 통째로 지우면 놓침이 생긴다).
    /// 줄 끝에 붙은 <c>//</c> 주석은 걷지 않는다 — 거기서 닫힌 주소가 나오면 빨강(안전한 쪽).
    /// </summary>
    private static string[] CodeLines(string source)
    {
        var s = Regex.Replace(source, @"@\*.*?\*@", BlankKeepLines, RegexOptions.Singleline);
        s = Regex.Replace(s, @"<!--.*?-->", BlankKeepLines, RegexOptions.Singleline);
        var lines = s.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                lines[i] = string.Empty;
            }
        }
        s = string.Join('\n', lines);
        s = Regex.Replace(s, @"/\*.*?\*/", BlankKeepLines, RegexOptions.Singleline);
        return s.Split('\n');
    }

    /// <summary>닫힌 주소 글자를 찾는 정규식 — <c>"/settings"</c> 는 따옴표째 정확히, 나머지는 앞뒤가 경로 글자가 아닐 때.</summary>
    private static Regex RouteRx(string route) => route == "/settings"
        ? new Regex("\"/settings\"", RegexOptions.IgnoreCase)
        : new Regex(@"(?<![\w\-/])" + Regex.Escape(route) + @"(?![\w\-/])", RegexOptions.IgnoreCase);

    private static string PrevSignificant(string[] lines, int i)
    {
        for (var j = i - 1; j >= 0; j--)
        {
            var t = lines[j].Trim();
            if (t.Length == 0 || t == "{") continue;
            return t;
        }
        return string.Empty;
    }

    /// <summary>
    /// 전체 메뉴(More.razor) 항목 줄 — <c>new("글", 아이콘, "주소"),</c> 꼴만. 묶음의 바로가기(QuickItem · <c>)),</c> 로 끝남)는
    /// 그리기 반복의 규칙을 거치지 않으므로 여기에 들지 않는다 — 거기에 닫힌 주소를 쓰면 빨강.
    /// </summary>
    private static readonly Regex MoreEntryLine = new(@"^\s*new\(""[^\r\n]*[^)]\),\s*(//.*)?$");

    [Fact(DisplayName = "G-A5-1 Web 전체에서 닫힌 5 주소로 가는 길은 모두 규칙을 거친다(전수 대조)")]
    public void G_A5_1_닫힌_주소로_가는_길은_모두_규칙을_거친다()
    {
        const string gate = "G-A5";
        var web = WebRoot();
        var files = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var rel = Path.GetRelativePath(web, f).Replace('\\', '/');
                return !rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        Assert.True(files.Count >= 100, $"{gate} Web 소스를 {files.Count} 개만 읽었다 — 훑는 자리가 틀렸다.");

        var violations = new List<string>();
        var guarded = 0;
        var noticePages = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name == "BetaClosedRoutes.cs") continue; // 규칙 파일 자신

            var lines = CodeLines(File.ReadAllText(file));
            var moreGuard = name == "More.razor" && lines.Any(l => l.Contains("BetaClosedRoutes.IsClosed(item.Href)", StringComparison.Ordinal));

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var route in Closed)
                {
                    if (!RouteRx(route).IsMatch(line)) continue;

                    if (name == "BetaClosedNoticePage.razor" && line.TrimStart().StartsWith("@page", StringComparison.Ordinal))
                    {
                        noticePages++;
                        continue;
                    }

                    if (line.Contains("BetaClosedRoutes.", StringComparison.Ordinal))
                    {
                        continue; // 규칙 줄 자신(감싸는 @if)
                    }

                    if (PrevSignificant(lines, i).Contains($"BetaClosedRoutes.IsOpen(\"{route}\")", StringComparison.OrdinalIgnoreCase))
                    {
                        guarded++;
                        continue;
                    }

                    if (moreGuard && MoreEntryLine.IsMatch(line))
                    {
                        guarded++;
                        continue;
                    }

                    violations.Add($"{Path.GetRelativePath(web, file)}:{i + 1} {route} → {line.Trim()}");
                }
            }
        }

        // 위반 목록을 먼저 단언한다 — 어디가 새는지(파일:줄)가 먼저 보여야 고칠 수 있다.
        Assert.True(violations.Count == 0,
            $"{gate} 닫힌 주소로 가는 길이 규칙 밖에 {violations.Count} 곳 있다(W-S-01 갈래 A · 설계 §2-7):\n" + string.Join("\n", violations));
        Assert.True(noticePages == 5, $"{gate} 안내 화면 @page 를 {noticePages} 개 찾았다(기대 5) — 훑기가 안 문다.");
        Assert.True(guarded >= 9, $"{gate} 규칙으로 감싼 메뉴 자리를 {guarded} 개만 찾았다(기대 9 이상 · 사이드바 5 · 전체 메뉴 3 · 도우미 바 1).");
    }

    [Theory(DisplayName = "G-A5-2 원래 화면 5개에 그 주소의 @page 가 없다(⬛ 주석으로만 남음)")]
    [InlineData("/settings", "Pages/Settings.razor")]
    [InlineData("/settings/ai-assistant", "Pages/Settings/AiAssistantPage.razor")]
    [InlineData("/sales/profitability", "Pages/Sales/SalesProfitabilityPage.razor")]
    [InlineData("/tax-invoice-stats", "Pages/Sales/TaxInvoiceStatsPage.razor")]
    [InlineData("/accounting/export", "Pages/Finance/TaxExportPage.razor")]
    public void G_A5_2_원래_화면에_라우트가_없다(string route, string relPath)
    {
        var raw = ReadWeb(relPath.Split('/'));
        var rx = new Regex(@"@page\s+""" + Regex.Escape(route) + @"""", RegexOptions.IgnoreCase);
        Assert.False(rx.IsMatch(raw), $"G-A5 {relPath} 에 {route} 라우트가 살아 있다 — 안내 화면과 겹치면 화면이 터진다.");
        Assert.Contains("⬛ W-S-01 갈래 A", raw); // 원문 보존 줄(#1)
    }

    [Fact(DisplayName = "G-A5-3 사용자정보는 사용환경 설정 서버를 부르지 않는다(불러올 때 · 적용 때)")]
    public void G_A5_3_사용자정보는_설정_서버를_부르지_않는다()
    {
        var code = string.Join('\n', CodeLines(ReadWeb("Pages", "Settings", "UserInfoPage.razor.cs")));
        Assert.DoesNotContain("SettingsSvc.GetAsync(", code);
        Assert.DoesNotContain("SettingsSvc.SaveAsync(", code);
        // 대조군 — 회사 정보 읽기 · 저장은 그대로 있어야 한다(#20 사용자정보 [적용]).
        Assert.Contains("SettingsSvc.GetCompanyAsync(", code);
        Assert.Contains("SettingsSvc.SaveCompanyDetailedAsync(", code);
    }

    [Fact(DisplayName = "G-A5-4 세무사 자료 보내기 화면의 서버 호출 자리 0(주입 줄은 세지 않음)")]
    public void G_A5_4_세무사_자료_화면은_서버를_부르지_않는다()
    {
        var lines = CodeLines(ReadWeb("Pages", "Finance", "TaxExportPage.razor"))
            .Where(l => !l.TrimStart().StartsWith("@inject", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("@using", StringComparison.Ordinal))
            .ToArray();
        Assert.True(lines.Length > 50, "G-A5 TaxExportPage 를 못 읽었다.");
        foreach (var token in new[] { "Http.", "FinSvc.", "api/" })
        {
            var hits = lines.Where(l => l.Contains(token, StringComparison.Ordinal)).ToArray();
            Assert.True(hits.Length == 0,
                $"G-A5 TaxExportPage 가 서버를 부르는 자리({token})가 생겼다 — 「서버 문 0」 이 깨졌다:\n{string.Join("\n", hits)}");
        }
    }

    [Fact(DisplayName = "G-A5-5 메뉴 밖 3곳의 목적지(P5) — 아바타 · 구독 돌아가기 = 사용자정보 / 사용자정보 닫기 = 처음 화면")]
    public void G_A5_5_메뉴_밖_3곳_목적지()
    {
        var avatar = string.Join('\n', CodeLines(ReadWeb("Layout", "MobileHeaderActions.razor")));
        Assert.Contains("Nav.NavigateTo(\"/settings/user-info\")", avatar);

        var billing = string.Join('\n', CodeLines(ReadWeb("Pages", "Settings", "SubscriptionBillingPage.razor")));
        Assert.Contains("Nav.NavigateTo(\"/settings/user-info\")", billing);

        var userInfo = string.Join('\n', CodeLines(ReadWeb("Pages", "Settings", "UserInfoPage.razor.cs")));
        var close = Regex.Match(userInfo, @"private void Close\(\)\s*\{(?<body>[^}]*)\}");
        Assert.True(close.Success, "G-A5 UserInfoPage.Close() 를 못 찾았다.");
        Assert.Contains("Navigation.NavigateTo(\"/\")", close.Groups["body"].Value);
    }
}
