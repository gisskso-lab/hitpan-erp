using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작11) — <b>다이얼로그로 열리는 컴포넌트에 <c>AuthorizeView</c> 를 쓰면 화면이 죽는다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측 반려(2026-08-25, 1.3.14): <i>"매출반품목록에 확정버튼없음 = 반려"</i> ·
/// <i>"매출반품목록에 들어가면 오류뜸"</i>
/// (<c>Authorization requires a cascading parameter of type Task&lt;AuthenticationState&gt;</c>).
/// </para>
/// <para>
/// 🔴 <b>증상 2개, 원인 1개.</b> 버튼이 안 보인 게 아니라 <b>목록 자체가 렌더 중에 죽었다.</b>
/// 작10 이 <c>PurchaseReturnList</c> 에 <c>AuthorizeView</c> 를 넣었는데, 이 컴포넌트는
/// <c>DialogService.ShowAsync</c> 로 열린다. <c>App.razor</c> 에서
/// <c>&lt;MudDialogProvider /&gt;</c>(8행) 는 <c>&lt;CascadingAuthenticationState&gt;</c>(10행)
/// <b>바깥</b>에 있어서, 다이얼로그 안에는 <c>Task&lt;AuthenticationState&gt;</c> 캐스케이드가 없다.
/// </para>
/// <para>
/// 🔴 <b>같은 함정을 이미 겪고 주석까지 남겨놨다</b> — <c>SalesListDialog.razor</c> 가
/// <i>"AuthorizeView 는 MudDialog 캐스케이드 밖에서 … 오류 발생"</i> 이라고 적어뒀는데
/// 작10 이 그걸 못 보고 되밟았다. 사람 기억으로 막던 것을 <b>게이트로 바꾼다.</b>
/// </para>
/// <para>
/// ⚠️ <b>권한이 사라지는 게 아니다.</b> 서버가 강제한다 —
/// <c>POST sales/returns/{id}/confirm</c> 은 <c>[Authorize(Policy = "SalesManager")]</c> 다.
/// 화면은 항상 보이고, 권한이 없으면 403 이 내려와 사용자에게 문장으로 안내된다.
/// </para>
/// <para>
/// 🔴 <b>이 게이트는 봉합을 빼면 실제로 FAIL 한다</b>(가짜 게이트 누적 16번의 교훈).
/// <c>PurchaseReturnList.razor</c> 에 <c>&lt;AuthorizeView</c> 를 한 줄 되돌리면 즉시 빨간불이 된다.
/// 낱말 하나가 아니라 <b>"다이얼로그로 열리는 파일"</b> 이라는 자리를 검사한다.
/// </para>
/// </remarks>
public class DialogAuthorizeViewGateTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string RepoPath(params string[] parts) =>
        Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());

    /// <summary>
    /// 주석을 걷어낸 실제 마크업만 남긴다.
    /// 🔴 <c>@* … *@</c> 는 <b>여러 줄</b>이라 시작 줄만 걸러선 안 된다 —
    /// 봉합 설명 안에 <c>AuthorizeView</c> 라는 낱말이 들어 있어 거짓 경보가 난다.
    /// </summary>
    private static string MarkupWithoutComments(string source)
    {
        // @* ... *@ (여러 줄) 제거
        var sb = new System.Text.StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var open = source.IndexOf("@*", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(source, i, source.Length - i);
                break;
            }

            sb.Append(source, i, open - i);
            var close = source.IndexOf("*@", open + 2, StringComparison.Ordinal);
            if (close < 0) break;   // 닫히지 않은 주석 — 나머지는 통째로 주석
            i = close + 2;
        }

        // 남은 C# 한 줄 주석(// …) 제거
        return string.Join('\n', sb.ToString().Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0 && !t.StartsWith("//", StringComparison.Ordinal);
            }));
    }

    /// <summary>
    /// <c>DialogService.ShowAsync&lt;X&gt;</c> / <c>Show&lt;X&gt;</c> 로 열리는 컴포넌트 이름을 모은다.
    /// 🔴 <b>글자 검사가 아니라 배선 검사다</b> — 실제로 다이얼로그로 열리는 자리만 딴다.
    /// </summary>
    private static HashSet<string> DialogHostedComponents()
    {
        var root = FindRepoRoot();
        var web = Path.Combine(root, "src", "HitPan.Web");
        var found = new HashSet<string>(StringComparer.Ordinal);

        var sources = Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var rx = new System.Text.RegularExpressions.Regex(
            @"DialogService\s*\.\s*Show(?:Async)?\s*<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>");

        foreach (var path in sources)
        {
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(path)))
            {
                var name = m.Groups[1].Value;
                var shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                found.Add(shortName);
            }
        }

        return found;
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 반려 ②③ — 목록이 통째로 죽었다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>다이얼로그로 열리는 컴포넌트는 <c>AuthorizeView</c> 를 쓰면 안 된다.</b>
    /// 캐스케이드가 없어 렌더 즉시 <c>InvalidOperationException</c> 이고, 화면이 안 뜬다.
    /// </summary>
    [Fact]
    public void 다이얼로그로_열리는_컴포넌트에_AuthorizeView_가_없어야_한다()
    {
        var hosted = DialogHostedComponents();
        Assert.True(hosted.Count > 0,
            "DialogService.ShowAsync<T> 배선을 하나도 못 찾았다 — 게이트가 헛돌고 있다");

        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var razor in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "HitPan.Web"), "*.razor", SearchOption.AllDirectories))
        {
            if (razor.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
             || razor.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var component = Path.GetFileNameWithoutExtension(razor);
            if (!hosted.Contains(component)) continue;

            var markup = MarkupWithoutComments(File.ReadAllText(razor));
            if (markup.Contains("<AuthorizeView", StringComparison.Ordinal))
            {
                offenders.Add(component);
            }
        }

        Assert.True(offenders.Count == 0,
            "다이얼로그로 열리는 컴포넌트에 AuthorizeView 가 있다 — 열리는 순간 화면이 죽는다: "
            + string.Join(", ", offenders)
            + ". 권한은 서버 정책([Authorize])으로 강제하고 화면은 항상 노출한다.");
    }

    /// <summary>
    /// 🔴 <b>반품확정 버튼 자체는 목록에 남아 있어야 한다</b>(작10 의 본래 목적).
    /// <c>AuthorizeView</c> 를 걷어내면서 버튼까지 지워버리면 반려 ② 가 그대로 살아난다.
    /// </summary>
    [Fact]
    public void 매출반품_목록에_반품확정_버튼이_있어야_한다()
    {
        var markup = MarkupWithoutComments(
            File.ReadAllText(RepoPath("src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor")));

        Assert.Contains("ConfirmOneAsync", markup, StringComparison.Ordinal);
        Assert.Contains("IsSalesReturn", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>확정 경로의 권한은 서버가 들고 있어야 한다.</b>
    /// 화면에서 <c>AuthorizeView</c> 를 뺀 근거가 바로 이것이다 —
    /// 서버 정책이 없으면 화면만 열어둔 꼴이 된다.
    /// </summary>
    [Fact]
    public void 매출반품_확정_취소_API_가_SalesManager_정책을_요구해야_한다()
    {
        var controller = File.ReadAllText(
            RepoPath("src", "HitPan.API", "Controllers", "SalesController.cs"));

        foreach (var route in new[] { "returns/{id}/confirm", "returns/{id}/cancel" })
        {
            var at = controller.IndexOf($"HttpPost(\"{route}\")", StringComparison.Ordinal);
            Assert.True(at > 0, $"{route} 엔드포인트가 있어야 한다");

            // 라우트 바로 뒤 200자 안에 정책이 붙어 있어야 한다.
            var window = controller.Substring(at, Math.Min(200, controller.Length - at));
            Assert.Contains("Policy = \"SalesManager\"", window, StringComparison.Ordinal);
        }
    }
}
