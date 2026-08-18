using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-45 · G-46</b> — 갇힌 고객이 <b>빠져나갈 길</b>이 실제로 열려 있는가 (20260818작5).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 2026-08-18 사장님 실측. 화면에 *"히트판 업데이트 중"* 이 뜬 채 멈췄고
/// 브라우저 콘솔에 <c>GET /api/app-version 403 (Forbidden)</c> 이 <b>끝없이 반복</b>됐다.
/// </para>
///
/// <para>
/// 🔴 <b>원인은 글자 하나였다.</b>
/// 통과 목록에는 <c>/api/appversion</c>, 실제 컨트롤러는 <c>[Route("api/app-version")]</c> —
/// <b>하이픈 하나</b> 때문에 목록이 그 길을 못 알아봤다.
/// </para>
///
/// <para>
/// 🔴 <b>이것이 최악인 이유</b> — 그 자리 주석은 *"막으면 고칠 방법이 사라진다"* 라고
/// <b>정확히 알고 적혀 있었다.</b> 뜻은 맞았고 <b>글자만 틀렸다.</b>
/// 기기 문제로 갇힌 고객은 <b>업데이트로 봉합을 받아야</b> 풀리는데,
/// 업데이트를 확인하는 길 자체가 막혀 <b>영원히 갇힌다.</b>
/// ⇒ 오늘 사장님이 정확히 그 방에 갇히셨다.
/// </para>
///
/// <para>
/// 🔴 <b>기존 게이트가 왜 못 잡았나 — 가짜게이트 8번째.</b>
/// <c>DeviceApprovalGateTests.G29d</c> 는 <c>InlineData("/api/appversion")</c> 으로
/// <b>없는 경로</b>를 시험했다. 미들웨어에 그 글자가 있으니 <b>초록불</b>이었다.
/// ⇒ 🔴 <b>하드코딩한 글자를 하드코딩한 글자와 맞춰 보면, 둘 다 틀려도 통과한다.</b>
/// 그것은 <b>내 기억을 시험한 것</b>이지 <b>시스템을 시험한 것</b>이 아니다.
/// </para>
///
/// <para>
/// 🟢 <b>그래서 여기서는 양쪽을 다 읽는다.</b> 컨트롤러의 실제 <c>[Route]</c> 를 읽고,
/// 미들웨어의 실제 통과 목록을 읽어 <b>서로 대조</b>한다.
/// 어느 한쪽이 바뀌면 <b>다른 쪽이 따라오지 않는 한</b> 빨간불이 된다.
/// </para>
///
/// <para>
/// ⚠️ <b>한계를 정직하게 적는다</b> — 이것은 <b>소스 대조</b>다. HTTP 를 실제로 쏘지 않는다.
/// 라우팅이 런타임에 달라지는 경우(라우트 규약 변경 등)까지는 못 본다.
/// 🔴 그래도 <b>오늘 사고는 정확히 잡는다</b> — 그것이 이 게이트가 존재하는 이유다.
/// </para>
/// </remarks>
public class EscapeRouteGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    private static string Read(params string[] parts)
    {
        var p = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(p), $"파일이 없다: {p}");
        return File.ReadAllText(p);
    }

    /// <summary>주석을 걷어낸다 — 주석에 적힌 글자를 근거로 삼으면 그것이 곧 가짜게이트다.</summary>
    private static string CodeOnly(string src)
    {
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        src = Regex.Replace(src, @"^\s*//.*$", "", RegexOptions.Multiline);
        return src;
    }

    /// <summary>미들웨어가 실제로 통과시키는 접두어 목록.</summary>
    private static List<string> BypassPrefixes()
    {
        var code = CodeOnly(Read("src", "HitPan.API", "Middleware", "DeviceAuthMiddleware.cs"));

        var block = Regex.Match(code, @"BypassPrefixes\s*=\s*new\[\]\s*\{(.*?)\}", RegexOptions.Singleline);
        Assert.True(block.Success,
            "🔴 통과 목록(BypassPrefixes)을 못 찾았다 — 이름이 바뀌었다면 이 게이트도 함께 고쳐야 한다.");

        return Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\"")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // G-45 — 업데이트 확인 경로가 실제로 열려 있다  🔴 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-45. 업데이트 확인 컨트롤러의 <b>실제 주소</b>가 통과 목록에 있다.</b>
    ///
    /// <para>
    /// [무엇을 막나] 컨트롤러 주소와 통과 목록이 <b>한 글자라도 갈리면</b>
    /// 갇힌 고객이 업데이트를 확인하지 못해 <b>영원히 못 빠져나온다</b>(2026-08-18 실측).
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>양쪽을 다 읽는다.</b> 컨트롤러의 <c>[Route]</c> 를 <b>소스에서</b> 뽑아
    /// 통과 목록과 대조한다 — 내가 외운 글자를 쓰지 않는다.
    /// </para>
    ///
    /// <para>[반증] 통과 목록에서 <c>/api/app-version</c> 을 빼면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-45 🔴 업데이트 확인 컨트롤러의 실제 주소가 통과 목록에 있다")]
    public void G45_업데이트확인_경로가_실제로_열려있다()
    {
        // ① 컨트롤러가 실제로 선언한 주소를 읽는다.
        var ctrl = Read("src", "HitPan.API", "Controllers", "AppVersionController.cs");
        var route = Regex.Match(ctrl, @"\[Route\(""([^""]+)""\)\]");

        Assert.True(route.Success,
            "🔴 AppVersionController 에서 [Route] 를 못 찾았다 — 주소를 확인할 수 없다.");

        // "api/app-version" → "/api/app-version"
        var actual = "/" + route.Groups[1].Value.TrimStart('/');

        // ② 통과 목록과 대조한다.
        var prefixes = BypassPrefixes();

        Assert.True(
            prefixes.Any(p => actual.StartsWith(p, StringComparison.OrdinalIgnoreCase)),
            $"🔴 **업데이트 확인 경로가 막혀 있다.**\n" +
            $"  컨트롤러 실제 주소 : {actual}\n" +
            $"  통과 목록          : {string.Join(", ", prefixes)}\n" +
            "기기 문제로 갇힌 고객은 **업데이트로 봉합을 받아야** 풀린다. " +
            "그 길이 막히면 영원히 갇힌다 — 2026-08-18 사장님이 정확히 그 방에 갇히셨다 " +
            "(`GET /api/app-version 403` 무한 반복).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-46 — 화면이 부르는 주소도 열려 있다  🔴 부르는 쪽
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-46. 업데이트 화면이 <b>실제로 부르는</b> 주소가 통과 목록에 있다.</b>
    ///
    /// <para>
    /// [왜 G-45 만으론 부족한가] 컨트롤러와 통과 목록이 맞아도,
    /// <b>화면이 다른 주소를 부르면</b> 여전히 막힌다.
    /// 🔴 실제로 8/18 에 <b>403 을 만난 것은 화면</b>이었다 — 그러니 <b>부르는 쪽</b>도 봐야 한다.
    /// </para>
    ///
    /// <para>[반증] 화면의 호출 주소를 바꾸거나 통과 목록에서 빼면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-46 🔴 업데이트 화면이 부르는 주소가 통과 목록에 있다")]
    public void G46_화면이_부르는_주소도_열려있다()
    {
        var razor = Read("src", "HitPan.Web", "Components", "Common", "UpdateProgressOverlay.razor");

        // 화면이 실제로 부르는 주소 — Http.GetAsync("api/…") 형태.
        var call = Regex.Match(razor, @"Http\.GetAsync\(\s*""([^""]+)""");

        Assert.True(call.Success,
            "🔴 업데이트 화면에서 버전 확인 호출을 못 찾았다 — " +
            "이 화면이 무엇을 부르는지 모르면 그 길이 열렸는지도 알 수 없다.");

        var called = "/" + call.Groups[1].Value.TrimStart('/');
        var prefixes = BypassPrefixes();

        Assert.True(
            prefixes.Any(p => called.StartsWith(p, StringComparison.OrdinalIgnoreCase)),
            $"🔴 **업데이트 화면이 부르는 길이 막혀 있다.**\n" +
            $"  화면이 부르는 주소 : {called}\n" +
            $"  통과 목록          : {string.Join(", ", prefixes)}\n" +
            "화면은 200 을 받을 때까지 **계속 다시 묻는다** ⇒ 403 이면 " +
            "*'히트판 업데이트 중'* 에서 **끝나지 않는다**(2026-08-18 사장님 실측: 콘솔 반복).");
    }
}
