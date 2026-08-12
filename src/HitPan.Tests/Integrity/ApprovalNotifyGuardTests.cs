using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-13) 그룹웨어 단계2 — 결재 알림 배선 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>지키는 것은 "허브를 만들었나" 가 아니라 "실제로 부르는가" 다.</b>
/// 8/11 AI 연동에서 팩토리를 만들어 놓고 대화 경로에 배선하지 않아, 시험 24개가 전부 통과하는데도
/// 고객이 챗GPT 를 골라도 클로드로 갔다. 알림도 똑같이 될 수 있다 —
/// 허브·서비스·화면을 다 만들고 <b>결재 생성 지점에서 부르지 않으면 아무 알림도 안 온다.</b>
/// </para>
/// <para>
/// 김삼성 상무 조언 1순위이고 사장님 지시(<i>"히트판ERP내 알림되도록"</i>)라 조용히 죽으면 안 된다.
/// </para>
/// </remarks>
public class ApprovalNotifyGuardTests
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

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
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
                       && !t.StartsWith("@*", StringComparison.Ordinal);
            }));

    // ───────────────────────────────────────────────────────────────
    // 배선 — 이것이 핵심이다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 결재 문서를 만든 뒤 <b>실제로 알림을 보내는가.</b>
    /// 이 호출이 빠지면 "알림 기능을 만들었는데 아무 알림도 안 오는" 상태가 된다.
    /// </summary>
    [Fact]
    public void 결재가_생성되면_결재자에게_알린다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs"));

        Assert.Contains("NotifyApproverAsync", code);
        Assert.Contains("notifier.NotifyEmployeeAsync", code);
    }

    /// <summary>
    /// 🔴🔴 <b>검증팀 P0-1 봉합 — 결재가 도는 경로는 셋이다.</b>
    /// <para>
    /// 처음에는 자동 트리거에만 알림을 붙였고, 시험도 그 4개 서비스만 봤다.
    /// <b>10개 시험이 전부 통과하는데 3분의 2가 비어 있었다.</b>
    /// </para>
    /// <list type="number">
    /// <item>자동 트리거(<c>ApprovalTriggerHelper</c>) — 매입·매출·연차·경비 확정 시</item>
    /// <item><b>수동 상신</b>(<c>ApprovalService.CreateApprovalAsync</c>) — 결재함에서 직접 올릴 때</item>
    /// <item><b>다음 결재자</b>(<c>ApprovalService.ProcessAsync</c>) — 승인 후 다음 단계로 넘어갈 때</item>
    /// </list>
    /// <para>
    /// 🔴 특히 3번이 빠지면 <b>1단계 결재자만 알림을 받고 2·3단계는 영원히 모른다.</b>
    /// 사이드바 뱃지가 최초 1회 조회뿐이라(이 작업의 출발점) 새로고침 전엔 아무도 모르고,
    /// 결재선 2단 이상 고객사에서 첫 단계 이후 결재가 조용히 멈춘다(헌법 #20).
    /// </para>
    /// </summary>
    [Fact]
    public void 결재_진행_3경로가_전부_알림을_보낸다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs"));

        // ① 수동 상신 — CreateApprovalAsync
        var manual = MethodBlock(code, "CreateApprovalAsync");
        Assert.Contains("NotifyApproverAsync", manual);

        // ② 다음 결재자 — ProcessAsync. 여기가 이번 반려의 핵심이다.
        var process = MethodBlock(code, "ProcessAsync");
        Assert.Contains("NotifyApproverAsync", process);

        // 다음 단계 번호로 보내야 한다. 1단계로 보내면 방금 결재한 사람에게 또 간다.
        Assert.Contains("doc.CurrentSeq + 1", process);
    }

    /// <summary>지정 메서드 선언부터 다음 <c>public</c> 멤버 직전까지 잘라낸다.</summary>
    private static string MethodBlock(string code, string methodName)
    {
        var start = code.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(start > 0, $"{methodName} 이 있어야 한다");

        var next = code.IndexOf("\n    public ", start, StringComparison.Ordinal);
        return next > start ? code[start..next] : code[start..];
    }

    /// <summary>
    /// 🔴 <b>결재 생성 지점 전부가 알림을 넘기는가.</b>
    /// 한 곳이라도 빠지면 그 문서 종류만 조용히 알림이 안 온다 —
    /// 화면상 아무 표시가 없어 몇 달을 모를 수 있다(6/19 마이그가 두 달 방치됐던 것과 같은 종류).
    /// </summary>
    [Fact]
    public void 결재_생성_호출부가_전부_알림을_넘긴다()
    {
        string[] callers =
        [
            Path.Combine("src", "HitPan.Application", "Services", "LeaveRequestService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "FinanceService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "SalesService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "PurchaseService.cs")
        ];

        foreach (var relative in callers)
        {
            var source = ReadSource(relative.Split(Path.DirectorySeparatorChar));
            var code = CodeLines(source);

            // TryCreateApprovalAsync 호출 각각이 notifier 를 넘기는지 본다.
            // 호출 시작부터 닫는 괄호까지를 잘라 확인한다.
            var calls = Regex.Matches(code, @"TryCreateApprovalAsync\((.*?)\);",
                RegexOptions.Singleline);

            Assert.True(calls.Count > 0, $"{relative} 에 결재 생성 호출이 있어야 한다");

            foreach (Match call in calls)
            {
                Assert.True(call.Value.Contains("otifier", StringComparison.Ordinal),
                    $"{relative} 의 결재 생성 호출이 알림을 넘기지 않는다 — 그 문서 종류만 알림이 조용히 안 온다");
            }
        }
    }

    /// <summary>
    /// 허브를 <b>실제로 노출</b>하는가. 이 줄이 빠지면 화면이 연결 자체를 못 한다.
    /// </summary>
    [Fact]
    public void 알림_허브가_주소에_붙어있다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Program.cs"));

        Assert.Contains("MapHub<NotificationHub>", code);
        Assert.Contains("INotificationService, SignalRNotificationService", code);
    }

    /// <summary>
    /// 화면이 <b>실제로 연결하고 받는가.</b> 서버만 보내면 아무도 못 본다.
    /// </summary>
    [Fact]
    public void 화면이_알림을_받아_띄운다()
    {
        var client = CodeLines(ReadSource("src", "HitPan.Web", "Services", "NotificationClient.cs"));
        Assert.Contains("hubs/notify", client);
        Assert.Contains("\"Notify\"", client);

        var bell = CodeLines(ReadSource("src", "HitPan.Web", "Layout", "NotificationBell.razor"));
        Assert.Contains("Notify.StartAsync", bell);

        // 레이아웃에 실제로 달려 있어야 한다. 컴포넌트만 만들고 안 달면 아무 데도 안 뜬다.
        var header = CodeLines(ReadSource("src", "HitPan.Web", "Layout", "TopHeader.razor"));
        Assert.Contains("<NotificationBell", header);
    }

    // ───────────────────────────────────────────────────────────────
    // 보안 — 알림이 남에게 새면 안 된다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>클라이언트가 그룹을 고르게 하면 안 된다.</b>
    /// 기존 마이그 허브는 <c>SubscribeJob(jobId)</c> 로 클라이언트가 그룹을 골랐다.
    /// jobId 가 추측 불가능한 GUID 라 그때는 성립했지만, 알림에 같은 방식을 쓰면
    /// <b>남의 사원 ID 를 넣어 남의 결재 알림을 받아볼 수 있다.</b>
    /// 그래서 연결 시 서버가 JWT 를 보고 그룹을 정한다(헌법 #2).
    /// </summary>
    [Fact]
    public void 알림_그룹은_서버가_JWT로_정한다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Hubs", "NotificationHub.cs"));

        // 서버가 연결 시점에 클레임을 읽어 그룹에 넣는다.
        Assert.Contains("OnConnectedAsync", code);
        Assert.Contains("FindFirstValue(\"tenant_id\")", code);
        Assert.Contains("FindFirstValue(\"employee_id\")", code);

        // 클라이언트가 그룹을 고르는 공개 메서드가 있으면 안 된다.
        Assert.DoesNotContain("public async Task Subscribe", code);
        Assert.DoesNotContain("public Task Subscribe", code);
    }

    /// <summary>
    /// 인증 없이 붙을 수 없어야 한다.
    /// </summary>
    [Fact]
    public void 알림_허브는_인증을_요구한다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Hubs", "NotificationHub.cs"));
        Assert.Contains("[Authorize]", code);
    }

    /// <summary>
    /// 🔴 알림 그룹에 <b>테넌트가 들어가는가</b>(헌법 #2).
    /// 사원 ID 가 GUID 라 충돌 가능성이 사실상 없지만,
    /// 격리는 우연이 아니라 구조로 보장해야 한다.
    /// </summary>
    [Fact]
    public void 알림_그룹은_테넌트로_격리된다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Hubs", "NotificationHub.cs"));

        var m = Regex.Match(code, @"GroupFor\(string tenantId, string employeeId\)\s*=>\s*(.+)");
        Assert.True(m.Success, "그룹 이름 규칙이 있어야 한다");
        Assert.Contains("tenantId", m.Groups[1].Value);
        Assert.Contains("employeeId", m.Groups[1].Value);
    }

    // ───────────────────────────────────────────────────────────────
    // 알림이 업무를 막지 않는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 알림 전송 실패가 <b>결재를 되돌리면 안 된다.</b>
    /// 결재는 이미 저장됐는데 알림 때문에 500 이 나면 그게 더 큰 사고다.
    /// 다만 삼키지도 않는다 — 로그는 남긴다(헌법 #15).
    /// </summary>
    [Fact]
    public void 알림_실패가_업무를_막지_않는다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Services", "SignalRNotificationService.cs"));

        Assert.Contains("catch (Exception ex)", code);
        Assert.Contains("_logger.LogWarning", code);

        // 예외를 다시 던지면 결재까지 실패한다.
        Assert.DoesNotContain("throw;", code);
    }

    /// <summary>
    /// 🔴 알림 본문에 <b>금액을 넣지 않는다.</b>
    /// 알림은 화면에 떠 있는 동안 옆 사람에게도 보인다.
    /// 무엇이 왔는지만 알리고 자세한 내용은 눌러서 보게 한다.
    /// </summary>
    [Fact]
    public void 알림_본문에_금액을_넣지_않는다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs"));

        var start = code.IndexOf("new AppNotification", StringComparison.Ordinal);
        Assert.True(start > 0, "알림 본문을 만드는 자리가 있어야 한다");

        var end = code.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var body = code[start..end];
        Assert.DoesNotContain("amount", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>검증팀 P0-2 봉합 — 대결(위임) 기간을 본다.</b>
    /// <para>
    /// 처음에는 <c>delegate_id</c> 를 무조건 발송 대상에 넣었다. 그런데 결재 권한 판정은
    /// <c>CURDATE() BETWEEN delegate_start AND delegate_end</c> 로 기간을 본다.
    /// ⇒ <b>작년에 휴가 대결을 한 번 걸어둔 직원이 지금도 남의 결재 알림을 계속 받고</b>,
    /// 눌러 들어가면 "결재 권한이 없습니다" 가 떴다. 타 부서 결재 사실이 새는 정보 노출이기도 하다.
    /// </para>
    /// <para>
    /// 🔴 알림과 권한 판정이 <b>같은 조건</b>이어야 한다.
    /// 다르면 "알림은 왔는데 결재는 못 하는" 사람이 생긴다.
    /// </para>
    /// </summary>
    [Fact]
    public void 대결자에게는_대결_기간에만_알린다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs"));

        // 권한 판정(ApprovalService.ProcessAsync)과 같은 조건을 쓴다.
        Assert.Contains("CURDATE() BETWEEN delegate_start AND delegate_end", code);

        // 기간이 유효할 때만 대결자를 대상에 넣는다.
        Assert.Contains("DelegateActive", code);
    }

    /// <summary>
    /// 알림 본문이 <b>영문 코드로 뜨면 안 된다</b>(고객 노출 영역 개발용어 금지).
    /// 문서유형을 새로 추가하면서 라벨을 빠뜨리면 "expense" 같은 글자가 그대로 보인다.
    /// </summary>
    [Fact]
    public void 알림_문서유형이_한글로_보인다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs"));

        // 결재 문서유형 10종이 전부 라벨을 갖는지 본다.
        string[] docTypes =
        [
            "quotation", "sales_order", "delivery", "purchase_order", "receipt",
            "sales_return", "purchase_return", "expense", "leave", "overtime"
        ];

        foreach (var t in docTypes)
        {
            Assert.Contains($"\"{t}\"", code);
        }
    }
}
