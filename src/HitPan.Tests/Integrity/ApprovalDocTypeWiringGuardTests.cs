using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-21) 결재 문서유형 배선 게이트 — 사장님 오더(그룹웨어 재설계) 선행 P0.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 시험이 존재하는 이유</b> — 결재 문서유형은 <b>세 곳</b>이 따로 논다:
/// ① <c>ApprovalService.DocTypeLabels</c> (설정화면 행을 <b>여기서 생성</b>한다)
/// ② <c>ApprovalTriggerHelper.DocTypeLabel</c> (알림 문구)
/// ③ 업무 서비스의 <c>TryCreateApprovalAsync(..., docType, ...)</c> 호출부
/// </para>
/// <para>
/// 셋이 어긋나면 <b>화면은 멀쩡한데 결재가 조용히 실패</b>한다. 실측으로 두 방향 모두 나왔다:
/// </para>
/// <list type="bullet">
///   <item><b>휴직</b> — ③이 <c>"absence"</c> 로 부르는데 ①에 없다. 설정화면에 "휴직" 행이
///   아예 안 떠서 <c>is_enabled</c> 를 켤 방법이 없고, 트리거는 <c>if (!setting.IsEnabled) return;</c>
///   으로 <b>조용히 종료</b>한다. 화면은 "결재상신" 버튼을 보여주는데 눌러도 아무 일이 없다.</item>
///   <item><b>초과근무</b> — ①에 있어서 <b>켤 수 있는데</b> ③이 없다. 결재선까지 짜도
///   아무 일이 안 일어난다.</item>
/// </list>
/// <para>
/// ⚠️ <c>ApprovalTriggerHelper.cs</c> 주석이 <i>"두 곳을 함께 고쳐야 하며, 빠뜨리면…"</i> 이라고
/// <b>경고해 놓고 정작 absence 에서 빠뜨렸다.</b> 주석은 게이트가 아니다 — 그래서 시험으로 세운다.
/// </para>
/// <para>
/// ⚠️ 주석 안의 문구를 코드로 오인하지 않도록 판정 전에 주석 줄을 걸러낸다
/// (이 레포 가드 시험들의 공통 방식).
/// </para>
/// </remarks>
public sealed class ApprovalDocTypeWiringGuardTests
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

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var kept = noBlock
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && t.Length > 0;
            });
        return string.Join("\n", kept);
    }

    private static string ApprovalServiceCode() =>
        CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs"));

    private static string TriggerHelperCode() =>
        CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs"));

    /// <summary>
    /// <c>ApprovalService.DocTypeLabels</c> <b>블록 안</b>의 키만 뽑는다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 파일 전체에서 <c>["x"] =</c> 를 긁으면 같은 파일의 <b>다른 사전</b>
    /// (결재상태 pending/approved…, 결제수단 cash/card…) 까지 딸려와 거짓 경보가 난다.
    /// 실제로 이 시험을 처음 짤 때 그렇게 나서, 블록을 잘라 읽도록 고쳤다.
    /// </remarks>
    private static List<string> DocTypeLabelKeys()
    {
        var code = ApprovalServiceCode();
        var start = code.IndexOf("DocTypeLabels", StringComparison.Ordinal);
        Assert.True(start >= 0, "DocTypeLabels 선언을 찾아야 한다");

        var open = code.IndexOf('{', start);
        Assert.True(open >= 0, "DocTypeLabels 초기화 블록을 찾아야 한다");

        var close = code.IndexOf("};", open, StringComparison.Ordinal);
        Assert.True(close > open, "DocTypeLabels 블록 끝을 찾아야 한다");

        var block = code[open..close];
        return Regex.Matches(block, @"\[""(?<v>[a-z_]+)""\]\s*=")
            .Select(m => m.Groups["v"].Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 🔴 휴직(absence) — 상신 코드가 부르는 docType 은 설정화면 목록에 <b>반드시</b> 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 설정화면 행은 <c>GetSettingsAsync</c> 가 <c>foreach (var (docType, label) in DocTypeLabels)</c>
    /// 로 만든다. 즉 <b>이 사전에 없으면 그 문서유형은 화면에 존재하지 않는다</b> →
    /// <c>approval_settings</c> 행이 안 생긴다 → <c>is_enabled</c> 가 참이 될 수 없다 →
    /// 트리거가 조용히 종료한다.
    /// </remarks>
    [Fact]
    public void 휴직_상신_docType_이_설정화면_목록에_있다()
    {
        var absenceCode = CodeLines(ReadSource(
            "src", "HitPan.Application", "Services", "AbsenceService.cs"));

        // 휴직 서비스가 실제로 쓰는 docType 을 코드에서 뽑는다(상수를 그대로 읽는다).
        var declared = Regex.Match(absenceCode, @"DocType\s*=\s*""(?<v>[a-z_]+)""");
        Assert.True(declared.Success,
            "AbsenceService 가 쓰는 docType 상수를 찾아야 한다");
        var docType = declared.Groups["v"].Value;

        // 그 docType 으로 실제 상신을 부르는지 확인 — 부르지도 않으면 이 시험의 전제가 무너진다.
        Assert.Contains("TryCreateApprovalAsync", absenceCode);

        Assert.True(
            ApprovalServiceCode().Contains($"[\"{docType}\"]"),
            $"""
            AbsenceService 가 "{docType}" 로 결재를 상신하는데 ApprovalService.DocTypeLabels 에
            그 항목이 없다. 설정화면 행은 DocTypeLabels 를 순회해 만들어지므로, 없으면
            "휴직" 행이 화면에 뜨지 않고 → is_enabled 를 켤 수 없고 → 트리거가
            'if (!setting.IsEnabled) return;' 로 조용히 종료한다.
            화면은 "결재상신" 버튼을 보여주는데 눌러도 아무 일이 일어나지 않는다.
            """);
    }

    /// <summary>
    /// 🔴 설정화면에 뜨는 문서유형은 <b>알림 라벨</b>도 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 없으면 <c>_ => "결재 문서"</c> 폴백으로 뭉개지거나 영문 코드가 노출된다
    /// (고객 노출 영역 개발용어 금지 — 헌법 #23).
    /// </remarks>
    [Fact]
    public void 설정화면_문서유형은_알림라벨도_가진다()
    {
        var settingsTypes = DocTypeLabelKeys();

        Assert.True(settingsTypes.Count >= 10,
            $"DocTypeLabels 를 제대로 읽어야 한다 (읽은 수: {settingsTypes.Count})");

        var helper = TriggerHelperCode();
        var missing = settingsTypes
            .Where(t => !helper.Contains($"\"{t}\""))
            .ToList();

        Assert.True(missing.Count == 0,
            $"""
            설정화면에는 뜨는데 ApprovalTriggerHelper.DocTypeLabel 에 라벨이 없는 문서유형:
            {string.Join(", ", missing)}
            → 알림에 "결재 문서" 로 뭉개지거나 영문 코드가 그대로 노출된다.
            """);
    }

    /// <summary>
    /// 🔴 결재로 승인했을 때 <b>원본 문서 상태</b>도 함께 바뀌어야 하는 문서유형을 고정한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ApprovalService.ProcessAsync</c> 의 원본 동기화는 <b>자동이 아니라 doc_type 마다
    /// 손으로 짠 블록</b>이다. 실측(2026-08-21) 기준 동기화되는 원본은 <c>leave_requests</c> 와
    /// <c>hr_reports</c> <b>둘뿐</b>이다.
    /// </para>
    /// <para>
    /// ⚠️ 그래서 새 doc_type 을 배선할 때 <b>상신만 연결하고 원본 동기화를 빠뜨리면</b>
    /// "결재는 승인됐는데 신청서는 대기중" 이 된다. 코드 주석이 이미 그 사고를 적고 있다 —
    /// <i>"결재함서 승인했는데 연차 미반영"</i> 이라 <c>leave</c> 블록을 나중에 넣었다.
    /// </para>
    /// <para>
    /// 이 시험은 <b>이미 동기화하는 2건이 사라지지 않게</b> 지킨다. 초과근무·휴직·경비의
    /// 동기화 신설은 사장님 결재(V1·V2) 후 본안에서 다룬다 —
    /// <c>docs/검증/병렬이슈/20260821_병렬이슈_결재승인이_원본상태를_안바꾼다.md</c>
    /// </para>
    /// </remarks>
    [Fact]
    public void 결재승인은_연차와_보고서_원본상태를_동기화한다()
    {
        var code = ApprovalServiceCode();
        var start = code.IndexOf("ProcessAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "ProcessAsync 를 찾아야 한다");

        var body = code[start..];

        Assert.True(
            Regex.IsMatch(body, @"UPDATE\s+leave_requests\s+SET[^""]*status\s*=\s*'approved'"),
            "결재 최종승인 시 leave_requests 를 approved 로 동기화하는 UPDATE 가 있어야 한다 " +
            "— 빠지면 '결재함서 승인했는데 연차 미반영' 이 재발한다");

        Assert.True(
            Regex.IsMatch(body, @"UPDATE\s+hr_reports\s+SET[^""]*status\s*=\s*'approved'"),
            "결재 최종승인 시 hr_reports 를 approved 로 동기화하는 UPDATE 가 있어야 한다");
    }

    /// <summary>
    /// 🔴 휴직 — 결재 승인이 <b>원본과 사원 상태</b>까지 반영해야 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 휴직은 다른 문서와 다르다. 원본 <c>employee_leave_of_absence</c> 만 바꿔서는 부족하고
    /// <b>사원의 <c>work_status</c> 까지</b> 함께 바뀌어야 한다 —
    /// <c>AbsenceService.ApproveAsync</c> 가 그렇게 하고 있고, 그 주석이 이유를 적어 뒀다:
    /// <i>"따로 하면 휴직은 '휴직중' 인데 사원은 '재직' 으로 남아 급여가 그대로 나간다."</i>
    /// </para>
    /// <para>
    /// ⚠️ 그래서 결재 경로에도 <b>같은 두 가지</b>가 있어야 한다. 원본만 바꾸고 사원 상태를
    /// 빠뜨리면 <b>급여가 휴직자를 재직자로 계산</b>한다.
    /// </para>
    /// <para>
    /// 🔴 <b>시작일 판정도 함께 지킨다.</b> 승인 시점에 이미 시작일이 지났으면 <c>'active'</c>(휴직중),
    /// 아직이면 <c>'approved'</c>(시작 전) 다. 이걸 무시하고 늘 <c>'approved'</c> 로 두면
    /// 조직도·급여가 휴직 시작자를 계속 재직자로 본다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 휴직_결재승인은_원본과_사원상태를_함께_반영한다()
    {
        var code = ApprovalServiceCode();
        var start = code.IndexOf("ProcessAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "ProcessAsync 를 찾아야 한다");
        var body = code[start..];

        Assert.True(
            Regex.IsMatch(body, @"UPDATE\s+employee_leave_of_absence\s+SET"),
            """
            결재 승인이 휴직 원본(employee_leave_of_absence)을 반영하지 않는다.
            → 결재함에서 승인해도 신청서는 '대기중' 에 남는다.
            """);

        Assert.True(
            Regex.IsMatch(body, @"UPDATE\s+employees\s+SET[^""]*work_status"),
            """
            결재 승인이 사원 work_status 를 바꾸지 않는다.
            → 휴직은 '휴직중' 인데 사원은 '재직' 으로 남아 급여가 그대로 나간다
              (AbsenceService.ApproveAsync 주석이 경고한 바로 그 사고).
            """);

        Assert.True(
            body.Contains("StartDate") || body.Contains("start_date"),
            """
            결재 승인이 휴직 시작일을 보지 않는다.
            → 시작일이 이미 지났는데도 '승인(시작 전)' 에 멈춰 급여·조직도가 재직자로 본다.
            """);
    }
}
