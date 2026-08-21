using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-21) 퇴사 시 결재선 정리 게이트 — 김삼성 상무 최우선 지적.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>상무님</b>: <i>"부장이 퇴사하면, 그 부장이 2단계 결재자인 <b>모든 문서가 동시에 멈춘다.</b>
/// 한 건이 아니라 전부다. … 인사 실무에서 이건 <b>매달 일어나는 일</b>이다."</i>
/// </para>
/// <para>
/// 🔴 <b>왜 스스로 못 빠져나오나</b> — 세 개가 동시에 막는다:
/// <list type="number">
///   <item><c>GetPendingAsync</c> 가 <c>approver_id</c> 로 INNER JOIN → <b>퇴사자 외엔 결재함에 안 뜬다</b></item>
///   <item><c>ProcessAsync</c> 는 <c>line.ApproverId != employeeId</c> → <b>"결재 권한이 없습니다"</b></item>
///   <item><c>SaveLinesAsync</c> 는 <c>pendingCount &gt; 0</c> 이면 <b>결재선 변경 자체를 막는다</b></item>
/// </list>
/// ⇒ 승인 0 · 반려 0 · 결재선 수정 0. <c>status='pending'</c> 영구 고착(헌법 #20 워크플로우 끊김 = P0).
/// </para>
/// <para>
/// ⚠️ 퇴사를 <b>막지는 않는다</b> — <c>GetResignPrecheckAsync</c> 주석:
/// <i>"막지는 않고 무슨 일이 벌어지는지 알려준다(반자동 원칙)"</i>. 사장님 반자동 원칙과 일치한다.
/// <b>막는 게 아니라 뒤처리를 한다.</b>
/// </para>
/// <para>⚠️ 주석 문구를 코드로 오인하지 않도록 판정 전에 주석 줄을 걸러낸다.</para>
/// </remarks>
public sealed class ResignApprovalLineGuardTests
{
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

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    private static string CodeLines(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join("\n", noBlock
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && t.Length > 0;
            }));
    }

    /// <summary>퇴사 처리 본문만 잘라낸다.</summary>
    private static string ResignBody()
    {
        var code = CodeLines(ReadSource(
            "src", "HitPan.Application", "Services", "EmployeeService.cs"));
        var start = code.IndexOf("ResignAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "ResignAsync 를 찾아야 한다");

        // 다음 public 메서드 전까지 = ResignAsync 본문 범위.
        var next = code.IndexOf("GetResignPrecheckAsync", start, StringComparison.Ordinal);
        return next > start ? code[start..next] : code[start..];
    }

    /// <summary>
    /// 🔴 퇴사 처리가 <b>결재선에서 그 사람을 뺀다</b>.
    /// </summary>
    [Fact]
    public void 퇴사하면_결재선에서_빠진다()
    {
        var body = ResignBody();

        Assert.True(
            Regex.IsMatch(body, @"approval_doc_lines"),
            """
            퇴사 처리가 approval_doc_lines 를 전혀 건드리지 않는다.
            → 퇴사자가 결재선에 그대로 남아, 그가 결재자인 모든 문서가 동시에 멈춘다.
              결재함엔 안 뜨고(approver_id INNER JOIN),
              결재선 수정도 막힌다(pendingCount > 0 가드).
              승인 0 · 반려 0 · 수정 0 = 영구 고착 (헌법 #20 P0).
            """);

        // 찾기만 하고 안 바꾸면 막은 적이 없는 것과 같다 — 실제로 끄는 UPDATE 가 있어야 한다.
        Assert.True(
            Regex.IsMatch(body, @"UPDATE\s+approval_doc_lines\s+SET[\s\S]{0,200}?is_active\s*=\s*0"),
            """
            approval_doc_lines 를 언급만 하고 실제로 비활성화하지 않는다.
            → 결재선에 퇴사자가 그대로 남는다.
            """);
    }

    /// <summary>
    /// 🔴 퇴사 처리가 <b>그 사람이 결재자인 미결 문서를 회수</b>한다.
    /// </summary>
    /// <remarks>
    /// 결재선에서 빼기만 하면 <b>이미 올라간 pending 문서</b>는 그대로 갇힌다.
    /// 상무님이 말한 <b>회수</b>가 이 자리다.
    /// </remarks>
    [Fact]
    public void 퇴사하면_그가_결재자인_미결문서를_회수한다()
    {
        var body = ResignBody();

        Assert.True(
            Regex.IsMatch(body, @"approval_documents"),
            """
            퇴사 처리가 미결 결재 문서를 회수하지 않는다.
            → 결재선에서 빼도 이미 상신된 pending 문서는 그대로 갇힌다.
              그 문서는 아무도 결재할 수 없고, 결재선 수정도 그 pending 때문에 막힌다.
            """);

        // ⚠️ 테이블 별칭(UPDATE approval_documents d SET d.status=…)을 쓸 수 있으므로
        //    별칭 유무 양쪽을 받는다. 실제로 별칭을 써서 1차 판정이 헛나갔다.
        Assert.True(
            Regex.IsMatch(body,
                @"UPDATE\s+approval_documents(\s+\w+)?\s+SET[\s\S]{0,300}?\w*\.?status\s*=\s*'cancelled'"),
            """
            approval_documents 를 언급만 하고 상태를 바꾸지 않는다.
            → 미결 문서가 pending 에 남아 결재선 수정까지 막는다.
            """);
    }

    /// <summary>
    /// 🔴 회수는 <b>퇴사자가 결재자인 건만</b> 대상이다.
    /// </summary>
    /// <remarks>
    /// 테넌트의 pending 을 통째로 취소하면 <b>멀쩡한 결재까지 죽인다.</b>
    /// 반드시 <c>employee_id</c> 로 한정해야 한다.
    /// </remarks>
    [Fact]
    public void 회수는_퇴사자_건만_대상으로_한다()
    {
        var body = ResignBody();

        if (!Regex.IsMatch(body, @"UPDATE\s+approval_documents"))
        {
            return; // 앞 시험이 이미 잡는다.
        }

        var stmt = Regex.Match(body, @"UPDATE\s+approval_documents[\s\S]{0,900}?""",
            RegexOptions.Singleline).Value;

        Assert.True(
            stmt.Contains("@EmployeeId") || stmt.Contains("approver_id"),
            """
            미결 회수가 퇴사자 건으로 한정되지 않았다.
            → 테넌트의 pending 결재를 통째로 취소해 멀쩡한 결재까지 죽인다.
            """);

        Assert.True(
            stmt.Contains("@TenantId"),
            "테넌트 한정이 없다 — 헌법 #2 테넌트 격리 위반.");
    }
}
