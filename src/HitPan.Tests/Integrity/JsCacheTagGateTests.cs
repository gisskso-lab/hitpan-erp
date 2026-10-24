using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261024작2 — <b>js 캐시 꼬리표 게이트</b> (사장님 실측 반려 봉합).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 겪고서 세웠나.</b> 1.3.62 에 <c>storage.js</c> 로 <c>hitpanPrintPreviewFrame</c> 을
///   실어 보냈는데 <c>index.html</c> 의 <c>?v=</c> 꼬리표를 <b>안 올렸다</b>. 브라우저는 9/28 판
///   <c>storage.js</c> 를 캐시에서 꺼내 썼고, 함수가 없으니 JS interop 이 예외를 냈다.
///   사장님 화면에 뜬 것은 「인쇄창을 띄우지 못했습니다」였다 — <b>미리보기 7/7 은 통과했는데</b>
///   인쇄만 죽었다(미리보기는 iframe <c>src</c> 라 JS 를 안 타기 때문이다).
/// </para>
/// <para>
/// 🔴 <b>왜 2,687건이 전부 초록이었나.</b> 그 규칙(N-4)은 <c>index.html</c> 주석에만 있었다.
///   <b>주석은 코드가 아니다.</b> 규칙에 게이트가 없으면 규칙이 아니다 — 그래서 고친 것이
///   브라우저에 <b>도달조차 못 했는데</b> 모든 시험이 초록이었다(「고쳤다 ≠ 갔다」).
/// </para>
/// <para>
/// 🔴 <b>어떻게 막나 — 꼬리표와 내용을 쌍으로 묶는다.</b>
///   <list type="bullet">
///     <item>내용이 바뀌면(해시 불일치) → 빨강. 꼬리표를 올리고 이 파일의 기대값 둘을 같이 고치게 한다.</item>
///     <item>내용은 그대로인데 꼬리표만 되돌리면 → 빨강. 봉합을 조용히 되돌리지 못한다.</item>
///   </list>
///   둘 중 한쪽만 재면 반대쪽으로 샌다.
/// </para>
/// <para>
/// 🔴 <b>줄바꿈을 정규화해서 잰다</b>(CRLF→LF). 안 그러면 러너 OS 가 바뀔 때
///   <b>코드가 그대로인데 빨개진다</b> — 거짓 빨강은 거짓 초록만큼 해롭다(다음 사람이 게이트를 끈다).
/// </para>
/// <para>
/// 🔴 <b>이 게이트가 못 재는 것</b>: 꼬리표를 올렸다고 <b>브라우저가 실제로 새로 받는지</b>는
///   못 잰다. 그건 브라우저 실측이다. 이 게이트는 <b>「올렸나」</b>까지만 잰다.
/// </para>
/// </remarks>
public class JsCacheTagGateTests
{
    /// <summary>꼬리표를 쌍으로 지키는 js — 늘리려면 여기 한 줄이면 된다.</summary>
    /// <remarks>
    /// 🔴 <b>기대값 갱신 방법</b>: 빨개지면 메시지에 찍힌 <b>실제 해시</b>를 여기 넣고,
    ///   <c>index.html</c> 의 꼬리표를 오늘 날짜로 올린 뒤 그 값을 여기 같이 적는다. <b>둘 다</b> 고친다.
    /// </remarks>
    public static TheoryData<string, string, string> Pinned() => new()
    {
        // 파일명, 기대 꼬리표, 기대 SHA256(줄바꿈 LF 정규화 후)
        { "storage.js", "20261024a", "9f4023cffda41ea8366d6a4102c05e279d241b362ab001d26a79f9e452c364a5" },
    };

    private static string WebRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.Web")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "HitPan.Web");
    }

    /// <summary>줄바꿈 정규화 후 SHA256 — 러너 OS 에 안 흔들린다.</summary>
    private static string HashNormalized(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>
    /// 🔴 <c>index.html</c> 의 <b>실제 script 줄</b>에서만 꼬리표를 긁는다 — 주석줄은 안 센다.
    /// </summary>
    /// <remarks>
    /// 🔴 80번 줄에 <c>js/storage.js?v=20260928b</c> 가 <b>주석으로</b> 남아 있다.
    ///   줄 단위로 안 가르고 파일 전체를 정규식으로 긁으면 <b>주석을 잡아 거짓 초록</b>이 된다.
    /// </remarks>
    private static string? TagFromScriptLine(string indexHtml, string jsFile)
    {
        var rx = new Regex(@"<script\s[^>]*src\s*=\s*""js/" + Regex.Escape(jsFile) + @"\?v=([0-9a-zA-Z]+)""",
                           RegexOptions.IgnoreCase);
        foreach (var line in File.ReadAllLines(indexHtml))
        {
            var t = line.TrimStart();
            if (t.StartsWith("<!--", StringComparison.Ordinal)) continue;
            var m = rx.Match(line);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    [Theory]
    [MemberData(nameof(Pinned))]
    public void 꼬리표와_내용이_쌍으로_맞는다(string jsFile, string expectedTag, string expectedHash)
    {
        var web = WebRoot();
        var jsPath = Path.Combine(web, "wwwroot", "js", jsFile);
        var indexPath = Path.Combine(web, "wwwroot", "index.html");

        Assert.True(File.Exists(jsPath), $"{jsFile} 이 없다: {jsPath}");
        Assert.True(File.Exists(indexPath), $"index.html 이 없다: {indexPath}");

        var actualTag = TagFromScriptLine(indexPath, jsFile);
        Assert.True(actualTag is not null,
            $"index.html 의 <script> 줄에서 js/{jsFile}?v= 를 못 찾았다. 꼬리표 없이 실으면 캐시가 안 갈린다(N-4).");

        var actualHash = HashNormalized(jsPath);

        Assert.True(actualHash == expectedHash,
            $"🔴 {jsFile} 이 바뀌었다(해시 {actualHash}). " +
            $"index.html 의 ?v= 꼬리표를 올리고 JsCacheTagGateTests.Pinned() 의 기대값 둘을 같이 고쳐라. " +
            $"안 올리면 브라우저가 옛 판을 캐시에서 꺼내 써서 「고친 것이 안 간다」 — 1.3.62 인쇄 사고가 그 자리다.");

        Assert.True(actualTag == expectedTag,
            $"🔴 {jsFile} 내용은 그대로인데 꼬리표가 {actualTag} 다(기대 {expectedTag}). " +
            $"봉합을 조용히 되돌리지 마라.");
    }

    /// <summary>
    /// 🔴 <b>인쇄 경로가 실제로 그 파일에 있나</b> — 1.3.62 가 깨진 그 함수를 이름으로 못 박는다.
    /// </summary>
    /// <remarks>
    /// 🔴 꼬리표만 재면 <b>함수가 통째로 빠져도</b> 초록이다. 부르는 쪽(.razor)과 정의하는 쪽(js)을
    ///   <b>양쪽 다</b> 세서, 한쪽만 남는 순간 빨개지게 한다.
    /// </remarks>
    [Fact]
    public void 인쇄_JS함수가_부르는쪽과_정의하는쪽_양쪽에_있다()
    {
        var web = WebRoot();
        var js = File.ReadAllText(Path.Combine(web, "wwwroot", "js", "storage.js"));
        var razor = File.ReadAllText(Path.Combine(web, "Components", "Common", "DocumentPrintPreviewDialog.razor"));

        Assert.True(js.Contains("window.hitpanPrintPreviewFrame", StringComparison.Ordinal),
            "🔴 storage.js 에 window.hitpanPrintPreviewFrame 정의가 없다 — 「인쇄」가 예외로 죽는다.");
        Assert.True(razor.Contains("\"hitpanPrintPreviewFrame\"", StringComparison.Ordinal),
            "🔴 DocumentPrintPreviewDialog.razor 가 그 함수를 안 부른다 — 정의만 있고 쓰는 곳이 없다.");
    }

    /// <summary>
    /// 🔴 레포 루트 — <b>`installer` 폴더를 찾아 올라가면 안 된다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <c>bin\Debug\net8.0</c> <b>아래에도 `installer` 가 복사돼 있다</b>. 그걸 찾아 올라가면
    ///   출력 폴더에서 멈춰 엉뚱한 곳을 가리킨다 — 세우자마자 이 함정에 걸렸다.
    ///   ⇒ 이미 검증된 <see cref="WebRoot"/>(<c>src/HitPan.Web</c>) 에서 두 칸 올라간다.
    /// </remarks>
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(WebRoot())!)!.FullName;

    /// <summary>
    /// 🔴 <b>인쇄 모듈은 C# 이 경로로 불러온다</b> — 꼬리표와 내용을 쌍으로 묶는다(20261024작2).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 이 축이 따로 있나.</b> <c>doc-print.js</c> 는 <c>index.html</c> 에 안 실린다.
    ///   <b>일부러 안 싣는다</b> — index.html 이 캐시되면 거기 적힌 꼬리표가 옛 값으로 굳기 때문이다.
    ///   대신 <b>`.razor` 안의 경로 상수</b>가 꼬리표를 든다. 그 상수는 DLL 안에 있어 같이 갱신된다.
    /// 🔴 그래서 <b>여기서도 쌍으로 재야 한다</b> — 안 그러면 모듈만 고치고 꼬리표를 안 올려
    ///   <b>같은 사고가 이 경로에서 다시 난다</b>.
    /// </remarks>
    [Fact]
    public void 인쇄모듈_꼬리표와_내용이_쌍으로_맞는다()
    {
        const string expectedTag = "20261024a";
        const string expectedHash = "2f507e4e694951b0013d0f2a0ac5f217763c7d887e20c86333c8acd265aed9a6";

        var web = WebRoot();
        var jsPath = Path.Combine(web, "wwwroot", "js", "doc-print.js");
        var razorPath = Path.Combine(web, "Components", "Common", "DocumentPrintPreviewDialog.razor");

        Assert.True(File.Exists(jsPath), $"doc-print.js 가 없다: {jsPath}");

        var razor = File.ReadAllText(razorPath);
        var m = Regex.Match(razor, @"\./js/doc-print\.js\?v=([0-9a-zA-Z]+)");
        Assert.True(m.Success,
            "🔴 .razor 가 doc-print.js 를 경로로 불러오지 않는다. index.html 의 전역에 기대면 캐시에 묶인다.");

        var actualHash = HashNormalized(jsPath);

        Assert.True(actualHash == expectedHash,
            $"🔴 doc-print.js 가 바뀌었다(해시 {actualHash}). " +
            $"PrintModulePath 의 ?v= 꼬리표를 올리고 이 시험의 기대값 둘을 같이 고쳐라.");

        Assert.True(m.Groups[1].Value == expectedTag,
            $"🔴 doc-print.js 내용은 그대로인데 꼬리표가 {m.Groups[1].Value} 다(기대 {expectedTag}).");
    }

    /// <summary>
    /// 🔴 <b>부르는 쪽·정의하는 쪽이 같은 이름을 쓴다</b> — 한쪽만 고치면 빨개진다.
    /// </summary>
    [Fact]
    public void 인쇄모듈_함수이름이_양쪽에서_같다()
    {
        var web = WebRoot();
        var js = File.ReadAllText(Path.Combine(web, "wwwroot", "js", "doc-print.js"));
        var razor = File.ReadAllText(Path.Combine(web, "Components", "Common", "DocumentPrintPreviewDialog.razor"));

        Assert.True(js.Contains("export function printPreviewFrame", StringComparison.Ordinal),
            "🔴 doc-print.js 가 printPreviewFrame 을 export 하지 않는다.");
        Assert.True(razor.Contains("\"printPreviewFrame\"", StringComparison.Ordinal),
            "🔴 .razor 가 printPreviewFrame 을 부르지 않는다.");
    }

    /// <summary>
    /// 🔴 <b>부트 자산 no-cache 봉합이 지워지지 않았나</b> (20261024작2 뿌리 봉합).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이 게이트가 못 재는 것</b>: 서버를 띄워 헤더가 실제로 나가는지는 <b>안 잰다</b>
    ///   (PowerShell 스크립트라 시험에서 못 돌린다). 봉합 당시 <b>HttpListener 로 띄워
    ///   실제 응답에 헤더가 붙는 것을 사람이 확인했고</b>, 이 시험은 <b>그 줄이 지워지는 것</b>만 막는다.
    ///   ⇒ <b>「헤더가 나간다」가 아니라 「끄는 코드가 남아 있다」를 재는 글자 게이트다.</b> 그렇게 읽어라.
    /// </remarks>
    [Fact]
    public void 부트자산_no_cache_봉합이_남아있다()
    {
        var path = Path.Combine(RepoRoot(), "installer", "web-server.ps1");
        Assert.True(File.Exists(path), $"web-server.ps1 이 없다: {path}");

        var text = File.ReadAllText(path);

        Assert.True(text.Contains("no-cache, no-store, must-revalidate", StringComparison.Ordinal),
            "🔴 web-server.ps1 에서 부트 자산 no-cache 가 사라졌다. index.html 이 캐시되면 " +
            "꼬리표를 아무리 올려도 브라우저가 옛 js 를 쓴다 — 1.3.62·1.3.63 이 그 사고다.");

        // 🔴 index.html 과 blazor.boot.json **둘 다** 걸려 있어야 한다. 하나만 남으면 반쪽이다.
        Assert.True(text.Contains("/index.html", StringComparison.Ordinal),
            "🔴 no-cache 대상에서 index.html 이 빠졌다.");
        Assert.True(text.Contains("blazor.boot.json", StringComparison.Ordinal),
            "🔴 no-cache 대상에서 blazor.boot.json 이 빠졌다.");
    }
}
