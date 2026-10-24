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
}
