using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-14) 🔴 <b><c>Forbid(문자열)</c> 금지 게이트.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>무엇을 겪고서</b> — 1.2.74 실사용에서 사장님 화면 아래 <b>흰 띠</b>가
/// 모든 화면·모바일에서 떠 있었다. Network 탭이 범인을 잡았다:
/// <c>messages?limit=50</c> 이 <b>403 → 400</b> 으로 반복 실패하고 있었다.
/// </para>
/// <para>
/// 진범은 <c>ChatController</c> 의 <c>return Forbid(ex.Message)</c> 4곳이었다.
/// ASP.NET Core 의 <c>Forbid(string)</c> 은 그 문자열을 <b>에러 메시지가 아니라
/// 인증 스킴(authentication scheme) 이름</b>으로 받는다. "이 대화를 볼 수 있는
/// 권한이 없습니다." 라는 스킴은 등록돼 있지 않으므로 <b>서버가 예외를 던지고</b>,
/// 403 대신 400/500 이 나가 클라이언트가 해석하지 못해 흰 띠가 떴다.
/// </para>
/// <para>
/// 🔴 <b>왜 시험으로 막나</b> — 이건 <b>읽으면 맞아 보이는 코드</b>다.
/// "권한 없음이니 Forbid, 이유도 같이" 는 자연스러운 발상이라 누구나 다시 쓴다.
/// 게다가 <b>빌드 0/0 을 통과하고</b> 컴파일러도 경고하지 않는다 —
/// 오버로드가 실제로 존재하기 때문이다. 사람 눈으로는 안 걸린다.
/// </para>
/// <para>
/// ⚠️ 인자 <b>없는</b> <c>Forbid()</c> 는 정상이다. 막는 것은 인자를 넘기는 형태뿐이다.
/// </para>
/// </remarks>
public class ForbidSchemeMisuseGuardTests
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

    /// <summary>
    /// 컨트롤러 어디에도 <c>Forbid(인자)</c> 가 없어야 한다.
    /// </summary>
    [Fact]
    public void 컨트롤러가_Forbid에_문자열을_넘기지_않는다()
    {
        var controllersDir = Path.Combine(FindRepoRoot(), "src", "HitPan.API", "Controllers");
        Assert.True(Directory.Exists(controllersDir), "컨트롤러 폴더가 있어야 한다");

        // Forbid( 뒤에 바로 ) 가 오지 않는 경우 = 인자를 넘긴 것.
        var offender = new Regex(@"Forbid\(\s*[^)\s]");
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(controllersDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // 주석은 건너뛴다 — 이 시험의 설명문에도 Forbid( 가 나온다.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                if (offender.IsMatch(line))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}  {trimmed}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Forbid(문자열) 은 그 문자열을 **인증 스킴 이름**으로 해석해 서버가 터진다.\n"
            + "403 을 이유와 함께 돌려주려면 StatusCode(StatusCodes.Status403Forbidden, new { message = ... }) 를 쓴다.\n"
            + "적발:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>
    /// 메신저의 권한 거부가 <b>사람이 읽을 이유</b>와 함께 403 으로 나가야 한다.
    /// </summary>
    /// <remarks>
    /// 봉합이 "터지지만 않게" 로 끝나면 안 된다. 이유를 안 주면 고객은
    /// "왜 안 되는지 모르는 채" 막힌다 — 그것도 되는 척의 한 형태다.
    /// </remarks>
    [Fact]
    public void 메신저_권한거부가_이유와_함께_403으로_나간다()
    {
        var path = Path.Combine(FindRepoRoot(),
            "src", "HitPan.API", "Controllers", "ChatController.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("StatusCodes.Status403Forbidden", source);

        // UnauthorizedAccessException 을 잡는 자리마다 메시지를 실어 보내야 한다.
        var catches = Regex.Matches(source, @"catch \(UnauthorizedAccessException ex\)").Count;
        var replies = Regex.Matches(source,
            @"StatusCode\(StatusCodes\.Status403Forbidden, new \{ message = ex\.Message \}\)").Count;

        Assert.True(catches > 0, "권한 거부를 잡는 자리가 있어야 한다");
        Assert.True(catches == replies,
            $"UnauthorizedAccessException 을 잡는 {catches}곳 전부가 이유를 실어 403 을 돌려줘야 한다(실측 {replies}곳)");
    }
}
