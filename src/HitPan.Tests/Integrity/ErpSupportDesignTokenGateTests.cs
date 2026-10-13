using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작1 — <b>디자인 토큰 게이트</b> (G-W9-14).
/// 수석웹디자이너·프론트매니저 참여분(사장님 지시 10/14 ③ · 설계 §9).
///
/// <para><b>왜 게이트로 거나</b>: 색·글꼴·간격을 숫자로 적으면 그 화면만 혼자 다른 집이 된다.
/// 조회 게시판 두 화면(① 고객지원 요청 · ② 본사공지사항)이 <b>한 집처럼</b> 보여야 하고,
/// 나중에 토큰을 고치면 두 화면이 함께 따라와야 한다.</para>
///
/// <para>🔴 이 게이트는 <b>간격·글꼴이 눈에 어떻게 보이는지는 못 잰다</b>(렌더 하네스 0건).
/// 재는 것은 「숫자를 직접 적지 않았다」 하나다. 보이는 모양은 사장님 실측표(§8)가 판정한다.</para>
/// </summary>
public sealed class ErpSupportDesignTokenGateTests
{
    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex HardRadius = new(@"border-radius:\s*\d+px", RegexOptions.Compiled);
    private static readonly Regex HardFont = new(@"font-size:\s*\d+px", RegexOptions.Compiled);

    [Fact(DisplayName = "G-W9-14 🔴 CS 화면 3개에 하드코딩 색·둥근정도·글꼴 크기가 0건 — 토큰만 쓴다")]
    public void G_W9_14_CS화면은_토큰만_쓴다()
    {
        var dir = Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web", "Pages", "Support");
        var files = Directory.GetFiles(dir, "*.razor", SearchOption.AllDirectories);

        Assert.True(files.Length >= 3,
            $"CS 화면이 3개보다 적다({files.Length}) — 공지 화면 신설이 빠졌나(G-W9-12 와 함께 본다)");

        var bad = new List<string>();
        foreach (var f in files)
        {
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // 주석 줄은 뺀다 — 사고 이력에 옛 색을 적어 두는 것은 기록이고 꾸밈이 아니다.
                var t = line.TrimStart();
                if (t.StartsWith("@*") || t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;

                if (Hex.IsMatch(line)) bad.Add($"{Path.GetFileName(f)}:{i + 1} 색을 숫자로 적었다 — {line.Trim()}");
                if (HardRadius.IsMatch(line)) bad.Add($"{Path.GetFileName(f)}:{i + 1} 둥근정도를 px 로 적었다 — {line.Trim()}");
                if (HardFont.IsMatch(line)) bad.Add($"{Path.GetFileName(f)}:{i + 1} 글꼴 크기를 px 로 적었다 — {line.Trim()}");
            }
        }

        Assert.True(bad.Count == 0,
            "토큰 대신 숫자를 적은 자리가 있다(설계 §9-1):\n  " + string.Join("\n  ", bad));
    }

    [Fact(DisplayName = "G-W9-14b 🔴 조회 게시판 CSS 가 토큰만 쓴다 + 캐시 버전이 올라갔다 (안 올리면 안 간다)")]
    public void G_W9_14b_게시판_CSS와_캐시버전()
    {
        var root = ErpCsGateDb.RepoRoot();
        var css = File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "wwwroot", "css", "hp-toss.css"));

        var start = css.IndexOf(".hp-cs-board", StringComparison.Ordinal);
        Assert.True(start > 0, "조회 게시판 규격(.hp-cs-*)이 hp-toss.css 에 없다");
        var block = css[start..];

        var bad = block.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("/*") && !l.TrimStart().StartsWith("*"))
            .Where(l => Hex.IsMatch(l) || HardRadius.IsMatch(l) || HardFont.IsMatch(l))
            .Select(l => l.Trim())
            .ToList();

        Assert.True(bad.Count == 0, "게시판 CSS 가 숫자를 직접 적었다:\n  " + string.Join("\n  ", bad));

        // 🔴 「고쳤다 ≠ 갔다」 — 캐시 버전을 안 올리면 고객 브라우저가 옛 CSS 를 그대로 쓴다.
        var index = File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "wwwroot", "index.html"));
        var tag = index.Split('\n').First(l => l.Contains("hp-toss.css"));
        Assert.Contains("?v=20261014", tag);
    }
}
