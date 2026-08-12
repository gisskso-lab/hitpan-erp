using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-13) 그룹웨어 단계3 — 업무보고서 4종 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시(2026-08-12): <i>"일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"</i>
/// </para>
/// <para>
/// 🔴 <b>이 작업에서 실제로 낸 사고 두 가지를 지킨다.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>이름 충돌로 기존 파일을 덮어썼다</b> — <c>IReportService</c>·<c>ReportService</c>·
/// <c>ReportController</c>·<c>ReportDtos</c> 네 개가 이미 있는 <b>현황 리포트</b>(견적·수주·
/// 매출수익성)였는데 신규라 여기고 Write 해 매출수익성 분석이 깨졌다(헌법 #1 위반).
/// 빌드가 잡아줘서 알았지, 참조가 없었다면 조용히 기능이 사라졌을 것이다.
/// </item>
/// <item>
/// <b>결재 승인이 원본에 반영되지 않을 뻔했다</b> — 결재함에서 승인해도 보고서가 "결재중" 에
/// 머물면 "되는 척" 이다. 승인 반영은 <c>leave</c> 만 배선돼 있었다.
/// </item>
/// </list>
/// </remarks>
public class WorkReportGuardTests
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
    // 🔴 사고 ① — 이름 충돌로 기존 기능을 덮어쓰지 않았는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>현황 리포트가 살아 있는가.</b>
    /// 업무보고서를 만들면서 같은 이름을 써서 이 파일들을 덮어썼던 적이 있다.
    /// 매출수익성 분석(<c>GetSalesProfitabilityAsync</c>)은 AI 도구가 부르는 기능이라
    /// 사라지면 AI 직원이 조용히 답을 못 한다.
    /// </summary>
    [Fact]
    public void 현황리포트_기능이_그대로_살아있다()
    {
        var iface = ReadSource("src", "HitPan.Application", "Interfaces", "IReportService.cs");

        // 현황 리포트가 원래 갖고 있던 것들.
        string[] required =
        [
            "GetQuotationReportAsync",
            "GetSalesProfitabilityAsync",
            "GetStockLedgerAsync",
            "GetStockStatusAsync"
        ];

        foreach (var m in required)
        {
            Assert.Contains(m, iface);
        }

        // DTO 도 마찬가지.
        var dtos = ReadSource("src", "HitPan.Application", "DTOs", "Report", "ReportDtos.cs");
        Assert.Contains("class ReportRow", dtos);
        Assert.Contains("class ProfitReportRow", dtos);
        Assert.Contains("class StockLedgerRow", dtos);
    }

    /// <summary>
    /// 업무보고서는 <b>현황 리포트와 다른 이름·다른 주소</b>를 쓴다.
    /// 같은 이름을 쓰면 다음 사람이 또 덮어쓴다.
    /// </summary>
    [Fact]
    public void 업무보고서는_현황리포트와_이름이_겹치지_않는다()
    {
        var controller = CodeLines(ReadSource("src", "HitPan.API", "Controllers", "WorkReportController.cs"));

        // 주소가 갈려 있어야 한다.
        Assert.Contains("api/work-reports", controller);

        // 현황 리포트 컨트롤러는 그대로 있어야 한다(지우지 않았다).
        var legacy = Path.Combine(FindRepoRoot(), "src", "HitPan.API", "Controllers", "ReportController.cs");
        Assert.True(File.Exists(legacy), "현황 리포트 컨트롤러가 남아 있어야 한다");
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 사고 ② — 결재 승인이 보고서에 반영되는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>결재함에서 승인·반려하면 보고서 상태가 바뀌어야 한다.</b>
    /// 이 배선이 없으면 승인해도 보고서는 영원히 "결재중" 이다 — 전형적인 "되는 척" 이고,
    /// 결재는 끝났는데 원본은 안 끝난 워크플로우 끊김이다(헌법 #20).
    /// </summary>
    [Fact]
    public void 결재_승인이_보고서_상태에_반영된다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs"));

        // 보고서 문서유형을 알아본다.
        Assert.Contains("ReportDocTypePrefix", code);

        // 승인·반려 양쪽 모두 원본을 갱신해야 한다.
        Assert.Contains("UPDATE hr_reports SET status='approved'", code);
        Assert.Contains("UPDATE hr_reports SET status='rejected'", code);
    }

    /// <summary>
    /// 결재함에 <b>영문 코드가 뜨면 안 된다</b>(고객 노출 영역 개발용어 금지).
    /// 라벨을 빠뜨리면 <c>MapLabels</c> 가 폴백해서 500 도 안 나고 조용히 "report_daily" 가 보인다.
    /// </summary>
    [Fact]
    public void 보고서_문서유형이_한글로_보인다()
    {
        // 결재함 라벨
        var approval = ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs");
        // 알림 본문 라벨 — 두 곳이 갈려 있으면 한쪽만 한글이 된다.
        var trigger = ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs");

        string[] docTypes = ["report_daily", "report_weekly", "report_monthly", "report_incident"];

        foreach (var t in docTypes)
        {
            Assert.Contains($"\"{t}\"", approval);
            Assert.Contains($"\"{t}\"", trigger);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 마이그레이션 — 고객에게 실제로 가는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 마이그레이션이 <b>고객에게 가는 자리</b>에 있어야 한다.
    /// 8/12 에 <c>installer/migrations/</c> 에 뒀다가 배포본에 안 실려 화면이 죽었다.
    /// </summary>
    [Fact]
    public void 보고서_마이그가_배포되는_자리에_있다()
    {
        var path = Path.Combine(FindRepoRoot(),
            "src", "HitPan.API", "Migrations", "SQL", "DB-92_hr_reports.sql");

        Assert.True(File.Exists(path), "DB-92 가 src/HitPan.API/Migrations/SQL/ 에 있어야 한다");

        var sql = File.ReadAllText(path);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `hr_reports`", sql);
        Assert.Contains("ENGINE=InnoDB", sql);          // 헌법 #17
        Assert.Contains("tenant_id", sql);              // 헌법 #2
    }

    /// <summary>
    /// 🔴 <b>출하 DDL 에도 같은 표가 있어야 한다</b>(헌법 #36 — 신규 설치의 단일 진실원).
    /// 마이그만 고치면 기존 고객사에는 표가 생기지만 <b>새로 설치하는 고객사에는 없다.</b>
    /// </summary>
    [Fact]
    public void 출하DDL에도_보고서_표가_있다()
    {
        var ddl = ReadSource("installer", "hitpan_db_clean.sql");

        Assert.Contains("CREATE TABLE `hr_reports`", ddl);

        // seed 도 함께 올라가야 한다. 표만 있고 seed 가 없으면
        // 신규 설치 고객사가 DB-92 를 다시 적용하려 든다.
        Assert.Contains("('DB-92','clean-ddl',1)", ddl);
    }

    // ───────────────────────────────────────────────────────────────
    // 화면 배선
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 화면이 <b>메뉴에 올라와 있고 라우트가 실재</b>해야 한다.
    /// 만들어 놓고 메뉴에 없으면 고객이 못 찾는다 — 단계1 에서 7화면이 그 상태였다.
    /// </summary>
    [Fact]
    public void 보고서_화면이_메뉴에_있고_라우트가_실재한다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.Contains("Href=\"/hr/reports\"", sidebar);

        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "WorkReportPage.razor");
        Assert.Contains("@page \"/hr/reports\"", page);
    }

    /// <summary>
    /// 🔴 화면이 <b>서버가 준 실패 사유를 보여주는가.</b>
    /// "결재 중이라 수정할 수 없다" 는 서버만 아는 사실인데,
    /// 그냥 "실패" 라고만 하면 고객이 계속 다시 눌러 본다.
    /// </summary>
    [Fact]
    public void 화면이_실패_사유를_그대로_보여준다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "HR", "WorkReportPage.razor"));

        // 서버 메시지를 스낵바에 그대로 싣는다.
        Assert.Contains("message ??", page);

        // 반려 사유도 보여준다 — 없으면 왜 반려됐는지 몰라 같은 내용을 다시 올린다.
        Assert.Contains("RejectReason", page);
    }

    /// <summary>
    /// 🔴 반자동 원칙(사장님 2026-08-12) — <b>저장과 결재 상신을 가른다.</b>
    /// 저장하자마자 결재가 올라가면 쓰다 만 보고서가 결재자에게 간다.
    /// 월간보고서는 한 번에 다 못 쓴다.
    /// </summary>
    [Fact]
    public void 임시저장과_결재상신이_갈려있다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "HR", "WorkReportPage.razor"));

        Assert.Contains("submit: false", page);
        Assert.Contains("submit: true", page);
    }

    // ───────────────────────────────────────────────────────────────
    // 권한 — 남의 보고서를 보거나 고칠 수 없다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 사원 ID 를 <b>JWT 클레임에서만</b> 받아야 한다(헌법 #2).
    /// 요청 본문의 사원 ID 를 믿으면 남의 이름으로 보고서를 쓸 수 있다 —
    /// 연차에서 실제로 있었던 사고다(2026-06-23 LV-W-03).
    /// </summary>
    [Fact]
    public void 사원ID를_JWT에서만_받는다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.API", "Controllers", "WorkReportController.cs"));

        Assert.Contains("User.FindFirst(\"employee_id\")", code);

        // 요청 본문에 사원 ID 를 두지 않는다(DTO 에 없어야 한다).
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "WorkReport", "WorkReportDtos.cs");
        var saveBlock = dto[dto.IndexOf("class SaveWorkReportRequest", StringComparison.Ordinal)..];
        Assert.DoesNotContain("EmployeeId", saveBlock);
    }

    /// <summary>
    /// 🔴 수정·삭제 쿼리가 <b>본인 것으로 한정</b>돼야 한다.
    /// <c>employee_id</c> 조건이 빠지면 남의 보고서를 고칠 수 있다.
    /// </summary>
    [Fact]
    public void 남의_보고서를_고치거나_지울_수_없다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "WorkReportService.cs");

        foreach (var marker in new[] { "UPDATE hr_reports", "DELETE FROM hr_reports" })
        {
            var idx = 0;
            var found = false;

            while ((idx = code.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
            {
                found = true;
                var end = code.IndexOf("\"\"\"", idx, StringComparison.Ordinal);
                Assert.True(end > idx, $"{marker} SQL 블록의 끝을 찾아야 한다");

                var sql = code[idx..end];
                Assert.Contains("@TenantId", sql);      // 헌법 #2
                Assert.Contains("@EmployeeId", sql);    // 본인 것만

                idx = end;
            }

            Assert.True(found, $"{marker} 가 있어야 한다");
        }
    }

    /// <summary>
    /// 🔴 <b>결재에 올라간 보고서는 고치거나 지울 수 없어야 한다.</b>
    /// 결재중인 보고서를 고치면 결재자가 본 것과 다른 내용이 승인된다.
    /// 승인 완료분은 기록이다.
    /// </summary>
    [Fact]
    public void 결재중인_보고서는_고치거나_지울_수_없다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "WorkReportService.cs");

        // 수정·상신은 작성중·반려에서만.
        Assert.Contains("status IN ('draft', 'rejected')", code);

        // 삭제는 작성중에서만 — 결재에 올라간 것은 기록이라 더 좁다.
        Assert.Matches(new Regex(@"DELETE FROM hr_reports.*?status = 'draft'", RegexOptions.Singleline), code);
    }

    // ───────────────────────────────────────────────────────────────
    // 검증 [3-V] 지적분 봉합 게이트 (2026-08-13)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>P0-1 봉합 — "결재에 올렸습니다" 가 거짓말이면 안 된다.</b>
    /// 결재 설정이 꺼져 있거나 결재선이 없으면 결재문서가 안 만들어지는데(실측 0건)
    /// 종전엔 화면이 무조건 성공을 띄웠다. 상신하는 <b>세 경로 전부</b>가 결과를 돌려줘야 한다.
    /// </summary>
    [Fact]
    public void 상신_결과를_사실대로_돌려준다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "WorkReportService.cs");

        // 세 경로(신규·수정·상신) 모두 결과 객체를 돌려준다 — 하나라도 bool 이면 그 자리에 되는 척이 남는다.
        Assert.Contains("Task<CreateWorkReportResult> CreateAsync", code);
        Assert.Contains("Task<SubmitWorkReportResult> UpdateAsync", code);
        Assert.Contains("Task<SubmitWorkReportResult> SubmitAsync", code);

        // 트리거는 "만들었을 것" 이 아니라 "있는지" 를 본다.
        Assert.Contains("DescribeApprovalBlockerAsync", code);
        Assert.Contains("SELECT COUNT(*) FROM approval_documents", code);

        // 화면이 사실을 보고 말을 가른다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "WorkReportPage.razor");
        Assert.Contains("outcome.ApprovalCreated", page);
        Assert.Contains("ApprovalSkipReason", page);
    }

    /// <summary>
    /// 🔴 <b>P0-1 봉합 — 판정 조건이 갈리면 안 된다.</b>
    /// 미리 물어보는 <c>DescribeApprovalBlockerAsync</c> 와 실제로 만드는 <c>TryCreateApprovalAsync</c> 가
    /// 다른 조건을 보면, "된다고 했는데 안 되는"(또는 그 반대) 자리가 생긴다.
    /// 둘 다 <b>설정 ON · 결재선 1행 이상</b> 두 조건만 본다.
    /// </summary>
    [Fact]
    public void 결재_가능_판정이_실제_생성_조건과_같다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "ApprovalTriggerHelper.cs");

        // ⚠️ 이름은 XML 주석(<see cref=...>)에도 나온다. 구간은 <b>선언부</b> 로 잡아야 한다 —
        //    처음엔 IndexOf 로 잡았다가 주석의 cref 에 걸려 엉뚱한 구간을 봤다.
        var describeIdx = code.IndexOf("Task<string?> DescribeApprovalBlockerAsync", StringComparison.Ordinal);
        Assert.True(describeIdx > 0, "판정 메서드 선언이 있어야 한다");

        var tryIdx = code.IndexOf("Task TryCreateApprovalAsync", StringComparison.Ordinal);
        Assert.True(tryIdx > describeIdx, "판정 메서드가 생성 메서드보다 앞에 있어야 한다(이 시험의 구간 가정)");

        var describeBlock = code[describeIdx..tryIdx];

        // 두 조건을 같은 표에서 본다.
        Assert.Contains("approval_settings", describeBlock);
        Assert.Contains("approval_doc_lines", describeBlock);
        Assert.Contains("is_active = 1", describeBlock);

        // 이유를 사용자 말로 돌려준다 — 개발용어 금지.
        foreach (var jargon in new[] { "null", "false", "exception", "Exception" })
        {
            var msgs = Regex.Matches(describeBlock, @"return ""([^""]+)""");
            foreach (Match m in msgs)
            {
                Assert.DoesNotContain(jargon, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// 🔴 <b>P0-2 봉합 — 반려 사유가 길다고 반려가 실패하면 안 된다.</b>
    /// 결재 의견은 <c>approval_history.comment</c> varchar(500), 원본은 <c>reject_reason</c> varchar(200).
    /// 201자면 STRICT 모드에서 <c>ERROR 1406</c> → 결재 트랜잭션 <b>전체가 롤백</b>된다(실측 재현).
    /// 원본 표에 사유를 넣는 <b>모든 자리</b>가 잘라서 넣어야 한다.
    /// </summary>
    [Fact]
    public void 반려_사유는_컬럼_폭에_맞게_잘라_넣는다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs");

        Assert.Contains("TruncateRejectReason", code);
        Assert.Contains("RejectReasonMaxLength = 200", code);

        // reject_reason=@Reason 을 쓰는 자리는 전부 자른 값을 넘겨야 한다.
        // (연차·업무보고서 두 자리 — 둘 다 같은 폭탄을 갖고 있었다)
        var hits = Regex.Matches(code, @"reject_reason\s*=\s*@Reason");
        Assert.True(hits.Count >= 2, $"reject_reason=@Reason 자리가 2곳 이상이어야 한다 (실제 {hits.Count})");

        var raw = Regex.Matches(code, @"Reason\s*=\s*request\.Comment\b");
        Assert.True(raw.Count == 0,
            $"자르지 않은 request.Comment 를 reject_reason 에 넣는 자리가 남아 있다 ({raw.Count}곳). "
            + "ERROR 1406 으로 반려 자체가 실패한다.");
    }

    /// <summary>
    /// 🔴 <b>P1-4 봉합 — 결재자가 본문을 볼 수 있어야 한다.</b>
    /// 안 읽고 승인하는 결재는 결재가 아니다. 단, 결재선 <b>밖</b> 사람은 여전히 못 본다.
    /// 위임 판정은 <c>ApprovalService</c> 와 같은 시간원(<c>CURDATE()</c>)을 써야 한다 —
    /// 갈리면 "볼 수는 있는데 승인은 안 되는" 자리가 생긴다.
    /// </summary>
    [Fact]
    public void 결재자는_보고서_본문을_볼_수_있다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "WorkReportService.cs");

        Assert.Contains("IsApproverAsync", svc);
        Assert.Contains("approval_doc_lines", svc);

        // 위임은 유효기간 안일 때만 — 만료된 위임자가 남의 보고서를 계속 보면 정보 유출이다.
        Assert.Contains("CURDATE() BETWEEN", svc);
        Assert.Contains("delegate_start IS NOT NULL", svc);

        // 컨트롤러가 이 판정을 실제로 쓴다(만들어만 두고 안 부르면 아무 소용 없다).
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "WorkReportController.cs");
        Assert.Contains("IsApproverAsync", ctrl);
        Assert.Contains("return Forbid();", ctrl);
    }
}
