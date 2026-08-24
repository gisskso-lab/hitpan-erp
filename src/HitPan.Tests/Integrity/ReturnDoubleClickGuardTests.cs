using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작4) 반품 화면 더블클릭 가드.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 결재(2026-08-25): <i>"반품 더블클릭 가드 먼저"</i> · <i>"버튼 가드만으로 둔다"</i>
/// </para>
/// <para>
/// 🔴 <b>무방비였던 자리.</b> 확정(<c>ConfirmReturnAsync</c>)에만 진입 가드가 있었다.
/// </para>
/// <list type="bullet">
/// <item><b>저장</b> — 플래그 자체가 없었다. 연타하면 <c>_isNew</c> 가 응답 도착 후에야
/// false 로 바뀌므로 <b>반품 전표가 중복 생성</b>된다.</item>
/// <item><b>삭제</b> — 플래그 없음. 같은 문서에 삭제 요청이 두 번 간다.</item>
/// <item><b>확정취소</b> — <c>_isConfirming = true</c> 를 세우기만 하고
/// <b>진입 체크가 빠져</b> 있어 연타가 그대로 뚫렸다.</item>
/// </list>
/// <para>
/// 🔴 <b>가드는 진입 체크와 해제가 한 쌍이어야 한다.</b> 플래그를 세우기만 하고
/// <c>if (flag) return;</c> 이 없으면 아무것도 막지 못한다 — 그게 확정취소에서 난 사고다.
/// 반대로 <c>finally</c> 로 풀지 않으면 한 번 실패한 뒤 버튼이 영영 죽는다.
/// </para>
/// <para>
/// ⚠️ 서버 멱등은 사장님 결재로 <b>범위 밖</b>이다. 확정·취소·삭제는 서버가 상태로
/// 막지만(<c>draft</c>/<c>confirmed</c> 검사), <b>생성은 서버 방어가 없다</b> —
/// 서버가 연타와 진짜 두 번째 반품을 구분할 수 없기 때문이다. 화면 가드가 유일한 방어선이다.
/// </para>
/// </remarks>
public class ReturnDoubleClickGuardTests
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

    private static string ReadReturnPage()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs");
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    /// <summary>주석 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("*", StringComparison.Ordinal)
                       && !t.StartsWith("/*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    /// <summary>지정한 메서드의 본문을 잘라낸다.</summary>
    private static string MethodBody(string code, string signature)
    {
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{signature} 를 찾아야 한다");

        var close = code.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(close > at, $"{signature} 의 본문 끝을 찾아야 한다");
        return code[at..close];
    }

    /// <summary>가드가 필요한 4개 동작 — 이름, 시그니처, 플래그.</summary>
    public static TheoryData<string, string, string> Guarded => new()
    {
        { "저장",     "private async Task SaveAsync()",         "_isSaving" },
        { "삭제",     "private async Task DeleteAsync()",       "_isDeleting" },
        { "확정",     "private async Task ConfirmReturnAsync()", "_isConfirming" },
        { "확정취소", "private async Task CancelReturnAsync()",  "_isConfirming" },
    };

    // ───────────────────────────────────────────────────────────────
    // 🔴 사고 — 플래그만 세우고 진입 체크가 없으면 아무것도 못 막는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>진입 가드가 있는가.</b>
    /// 확정취소는 플래그를 세우기만 하고 이 체크가 없어 연타가 뚫렸다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Guarded))]
    public void 반품_동작은_진입_가드를_가져야_한다(string label, string signature, string flag)
    {
        var body = MethodBody(CodeLines(ReadReturnPage()), signature);

        Assert.True(body.Contains($"if ({flag}) return;", StringComparison.Ordinal),
            $"{label}: 진입 가드 'if ({flag}) return;' 이 없다. " +
            "플래그를 세우기만 하면 연타를 못 막는다.");
    }

    /// <summary>
    /// 🔴 <b>가드를 실제로 세우고 finally 로 푸는가.</b>
    /// <c>finally</c> 가 없으면 한 번 실패한 뒤 버튼이 영영 죽는다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Guarded))]
    public void 반품_동작은_가드를_세우고_finally로_풀어야_한다(string label, string signature, string flag)
    {
        var body = MethodBody(CodeLines(ReadReturnPage()), signature);

        Assert.True(body.Contains($"{flag} = true;", StringComparison.Ordinal),
            $"{label}: 가드 플래그를 세우지 않는다 — 진입 체크가 무의미해진다.");

        Assert.Contains("finally", body, StringComparison.Ordinal);
        Assert.True(body.Contains($"{flag} = false;", StringComparison.Ordinal),
            $"{label}: finally 에서 가드를 풀어야 한다. 안 풀면 버튼이 영영 죽는다.");
    }

    /// <summary>
    /// 🔴 <b>저장·삭제 플래그가 실제로 선언돼 있는가.</b>
    /// </summary>
    [Fact]
    public void 저장과_삭제의_가드_플래그가_선언돼_있어야_한다()
    {
        var code = CodeLines(ReadReturnPage());

        Assert.Contains("private bool _isSaving;", code, StringComparison.Ordinal);
        Assert.Contains("private bool _isDeleting;", code, StringComparison.Ordinal);
    }
}
