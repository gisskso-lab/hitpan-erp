using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-56 ~ G-59</b> — 업체특별단가를 <b>실제로 등록할 수 있다</b> (20260820작4 · 2차 봉합).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — 사장님 1.2.91 실측: <i>"업체특별단가 입력 자체가 안됨"</i>
/// </para>
///
/// <para>
/// [진범] 품목명 칸이 <b>읽기 전용 <c>PropertyColumn</c></b> 이었다.
/// [추가] 버튼은 빈 줄을 만드는데(<c>AddRow</c>) 그 줄에 <b>품목을 고를 방법이 없었다.</b>
/// <c>ItemId</c> 가 빈 채로 남으니 저장해도 서버가 받을 수 없다 ⇒ <b>등록이 원천 불가.</b>
/// </para>
///
/// <para>
/// 🔴 <b>이것이 8/20 조사에서 본 "0건" 의 진짜 이유였다.</b>
/// PM 은 <c>partner_special_prices</c> 가 0건인 것을 보고 <i>"아직 안 쓴 것"</i> 으로 적었다.
/// <b>안 쓴 게 아니라 못 쓴 것</b>이었다 — <b>없는 것을 "아직" 으로 읽으면 원인을 놓친다.</b>
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 브라우저에서 실제로 저장되는지는 검사하지 못한다.
/// 실측(사장님 화면)의 몫이다. 여기 초록불은 <b>"등록 경로가 화면에 존재한다"</b> 까지다.
/// </para>
/// </remarks>
public sealed class SpecialPriceEntryGateTests
{
    private const string PagePath = "src/HitPan.Web/Pages/Partners/SpecialPricePage.razor";

    private static string RepoRoot()
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

    /// <summary>주석(<c>@* *@</c>·<c>//</c>)을 걷어낸 코드만 남긴다.</summary>
    private static string Code()
    {
        var path = Path.Combine(RepoRoot(), PagePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        var src = File.ReadAllText(path);

        var noRazorComment = Regex.Replace(src, @"@\*.*?\*@", "", RegexOptions.Singleline);
        return string.Join('\n', noRazorComment
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 🔴 <b>G-56 — 수문장.</b> 품목을 <b>고를 수 있어야</b> 한다.
    /// 읽기 전용 칸으로 되돌리면 등록 경로가 통째로 사라진다.
    /// </summary>
    [Fact]
    public void G56_품목명_칸에서_품목을_고를_수_있다()
    {
        var code = Code();

        Assert.False(
            Regex.IsMatch(code, @"PropertyColumn\s+Property=""x\s*=>\s*x\.ItemName"""),
            "품목명이 읽기 전용 PropertyColumn 으로 돌아갔다 — 새 줄에 품목을 고를 방법이 없어진다.");

        Assert.True(code.Contains("SearchItemAsync", StringComparison.Ordinal),
            "품목 검색이 없다 — 등록이 원천 불가해진다.");
    }

    /// <summary>
    /// 🔴 <b>G-57 — <c>ItemId</c> 를 채운다.</b> 이 화면의 급소다.
    /// 이름만 넣고 <c>ItemId</c> 를 비우면 <b>저장은 눌러지는데 서버가 못 받는다</b> — 이번 사고의 모양.
    /// </summary>
    [Fact]
    public void G57_품목을_고르면_ItemId_가_채워진다()
    {
        var code = Code();

        Assert.True(code.Contains("OnItemPicked", StringComparison.Ordinal),
            "품목 선택 처리기가 없다.");
        Assert.True(
            Regex.IsMatch(code, @"row\.ItemId\s*=\s*item\.ItemId"),
            "품목을 골라도 ItemId 가 안 채워진다 — 저장해도 서버가 받을 수 없다.");
    }

    /// <summary>
    /// 🔴 <b>G-58 — 품목 없는 줄을 서버로 보내지 않는다.</b>
    /// ⚠️ 다만 <b>조용히 거르면 안 된다</b> — 몇 건을 건너뛰었는지 사람에게 말해야
    /// <i>"저장했는데 없다"</i> 가 안 된다.
    /// </summary>
    [Fact]
    public void G58_품목없는_줄은_거르고_사람에게_알린다()
    {
        var code = Code();

        Assert.True(
            Regex.IsMatch(code, @"Items\.Where\([^)]*ItemId[^)]*\)"),
            "품목 없는 줄을 거르지 않는다.");
        Assert.True(code.Contains("skipped", StringComparison.Ordinal),
            "건너뛴 줄 수를 사람에게 알리지 않는다 — '저장했는데 없다' 가 된다.");
    }

    /// <summary>
    /// G-59 — 업체를 안 고르고 [추가] 를 누르면 막는다.
    /// 어느 업체의 단가인지 정할 수 없는 줄이 생기면 저장 단계에서 조용히 사라진다.
    /// </summary>
    [Fact]
    public void G59_업체_미선택시_줄을_추가하지_않는다()
    {
        var code = Code();
        var start = code.IndexOf("private void AddRow", StringComparison.Ordinal);
        Assert.True(start > 0, "AddRow 가 있어야 한다");

        var body = code[start..Math.Min(start + 500, code.Length)];
        Assert.True(body.Contains("SelectedPartnerId", StringComparison.Ordinal)
                 && body.Contains("return", StringComparison.Ordinal),
            "업체를 안 골라도 줄이 추가된다 — 그 줄은 저장되지 않는다.");
    }
}
