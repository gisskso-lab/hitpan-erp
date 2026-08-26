using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작2 — 매입반품 목록 SQL 조립 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측 반려(2026-08-26): <i>"매입반품 목록 500"</i>.
/// 진범은 스키마도 마이그도 아니고 <b>문자열 이어붙이기</b>였다.
/// </para>
/// <para>
/// <c>GetReturnsAsync</c> 의 raw string 은 <c>... r.is_deleted = 0</c> 에서 끝난다
/// (C# raw string 은 닫는 따옴표 앞 줄바꿈을 버린다). 여기에 <c>"AND r.return_date >= @From"</c>
/// 을 공백 없이 이어붙이면 <c>0AND</c> 가 되어 MariaDB 가 파싱하다 죽는다.
/// </para>
/// <para>
/// 🔴 <b>왜 아무도 못 잡았나</b> — <b>날짜가 하나라도 있을 때만</b> 터진다.
/// 날짜 0개면 이어붙일 것이 없어 정상 200 이다. 화면은 항상 최근 30일을 보내므로
/// <b>고객은 100% 500</b> 인데, 날짜 없이 부르는 시험은 전부 통과한다.
/// </para>
/// <para>
/// ⚠️ <b>글자검사가 아니다</b>(가짜 게이트 금지). 소스에서 실제 조각을 읽어
/// <b>조립한 뒤</b> 토큰이 붙어버렸는지를 본다 — 봉합을 빼면 이 시험은 FAIL 한다.
/// </para>
/// </remarks>
public class PurchaseReturnListSqlGateTests
{
    private const string TargetMethod =
        "public async Task<List<PurchaseReturnListDto>> GetReturnsAsync";

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

    private static string MethodBody()
    {
        var src = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HitPan.Application", "Services", "PurchaseService.cs"));

        var start = src.IndexOf(TargetMethod, StringComparison.Ordinal);
        Assert.True(start >= 0, "GetReturnsAsync 를 찾아야 한다");

        var end = src.IndexOf("    public ", start + TargetMethod.Length, StringComparison.Ordinal);
        if (end < 0) end = src.Length;
        return src[start..end];
    }

    /// <summary>
    /// 소스에서 base SQL 과 조건 조각을 읽어 <b>실제로 조립해 본다.</b>
    /// 조립 결과에 <c>0AND</c> 같은 토큰 융합이 생기면 FAIL.
    /// </summary>
    [Fact]
    public void 반품목록_날짜조건_이어붙일때_토큰이_붙으면_안된다()
    {
        var body = MethodBody();

        // ① base SQL raw string 을 뽑는다.
        var rawMatch = Regex.Match(body, @"var sql = """"""\r?\n(.*?)\n(\s*)"""""";", RegexOptions.Singleline);
        Assert.True(rawMatch.Success, "base SQL raw string 을 찾아야 한다");

        // 🔴 C# raw string 의 실제 의미를 그대로 재현한다:
        //   ① 닫는 따옴표 줄의 들여쓰기만큼을 **모든 줄에서 제거**하고
        //   ② 닫는 따옴표 앞 줄바꿈을 버린다.
        //   ⚠️ 이 재현을 안 하면 들여쓰기 공백이 남아 "공백으로 끝난다" 고 잘못 판정한다
        //     (이 게이트를 처음 짰을 때 실제로 그렇게 통과해 버렸다 — 가짜 게이트였다).
        var indent = rawMatch.Groups[2].Value;
        var baseSql = string.Join("\n", rawMatch.Groups[1].Value
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.StartsWith(indent, StringComparison.Ordinal)
                ? line[indent.Length..]
                : line.TrimStart()));

        // ② conditions.Add("...") 조각을 전부 뽑는다.
        var conds = Regex.Matches(body, @"conditions\.Add\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(conds);

        // ③ sql += ... 이어붙이기 표현식을 뽑는다.
        var appendMatch = Regex.Match(body, @"sql \+= ([^;]+);", RegexOptions.Singleline);
        Assert.True(appendMatch.Success, "sql += 구문을 찾아야 한다");
        var appendExpr = appendMatch.Groups[1].Value;

        // 이어붙이기가 conditions 앞에 공백을 보장하는지 — 표현식 또는 base SQL 로 판정.
        var leadingSpaceLiteral = Regex.IsMatch(appendExpr, @"^\s*""\s+""\s*\+");
        var baseEndsWithSpace = baseSql.Length > 0 && char.IsWhiteSpace(baseSql[^1]);
        var separated = leadingSpaceLiteral || baseEndsWithSpace;

        // ④ 실제 조립 결과를 만들어 토큰 융합을 확인한다.
        var assembled = baseSql + (separated ? " " : string.Empty) + string.Join(" ", conds);

        // 🔴 20260827작1 — SQL 주석(`--`)은 검사에서 제외한다.
        //   이 게이트는 조립 결과를 **한 줄로 눌러** `[0-9A-Za-z_]AND` 를 찾는다. 그런데
        //   base SQL 안의 `--` 주석에 20260826작2 사고를 설명하느라 `0AND` 라는 **글자**를
        //   적어두면, 주석이 눌리면서 그 글자가 그대로 걸려 **정상 SQL 을 FAIL** 시킨다.
        //   실제로 작1 봉합(`AND r.status <> 'canceled'`)을 넣자 이 오탐이 터졌고,
        //   같은 SQL 을 MariaDB 에 직접 돌려 3행이 정상 반환되는 것을 확인했다.
        //   ⇒ 주석은 파서가 버리는 것이니 검사도 버려야 한다. **코드만 검사한다.**
        var codeOnly = string.Join("\n", assembled
            .Split('\n')
            .Select(line =>
            {
                var idx = line.IndexOf("--", StringComparison.Ordinal);
                return idx >= 0 ? line[..idx] : line;
            }));

        Assert.False(
            Regex.IsMatch(codeOnly, @"[0-9A-Za-z_]AND\b"),
            "조립된 SQL 에 토큰이 붙었다 — MariaDB 파싱 실패(500).\n실제 조립 결과 꼬리:\n"
            + codeOnly[Math.Max(0, codeOnly.Length - 200)..]);
    }

    /// <summary>
    /// ORDER BY 리터럴도 앞에 공백을 달고 있어야 한다(조건 마지막 토큰과 융합 방지).
    /// </summary>
    [Fact]
    public void 반품목록_ORDER_BY_앞에_공백이_있어야_한다()
    {
        var body = MethodBody();

        var appendMatch = Regex.Match(body, @"sql \+= ([^;]+);", RegexOptions.Singleline);
        Assert.True(appendMatch.Success, "sql += 구문을 찾아야 한다");

        Assert.Contains("\" ORDER BY", appendMatch.Groups[1].Value);
    }
}
