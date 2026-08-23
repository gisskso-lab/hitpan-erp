namespace HitPan.Application.DTOs.WorkReport;

/// <summary>
/// 업무보고서 종류. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// 사장님 지시(2026-08-12): <i>"일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"</i>
/// <para>
/// ⚠️ <b>이름 주의</b> — 이 레포에는 이미 <c>IReportService</c>(견적·수주·매입 <b>현황 리포트</b>)와
/// <c>DTOs/Report/ReportDtos.cs</c>(<c>ReportRow</c>·<c>ProfitReportRow</c>)가 있다.
/// 처음에 같은 이름으로 만들다가 <b>기존 파일을 덮어써 매출수익성 분석이 깨졌다</b>(헌법 #1 위반).
/// 그래서 업무보고서 계열은 전부 <c>WorkReport</c> 로 가른다. 현황 리포트와는 다른 도메인이다.
/// </para>
/// <para>
/// 🔴 종류를 문자열 상수로 둔다. enum 으로 만들면 DB 값과 어긋날 때 <c>Enum.Parse</c> 가 예외를
/// 던지는데, 그 사고가 이 레포에 이미 있었다(<c>EmployeeConfiguration.ParseEmpType</c> 주석 —
/// 파싱 실패가 로그인 클레임을 비웠다).
/// </para>
/// </remarks>
public static class WorkReportTypes
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Incident = "incident";

    /// <summary>
    /// 출장/외근 보고서. 작(2026-08-24) 작3 — 사장님: <i>"보고서관리 : 출장/외근보고서 추가"</i>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>일일보고서로 대신할 수 없다.</b> 출장은 <b>어디에·왜 갔는지</b>가 핵심이고,
    /// 일일보고서는 <b>오늘 무엇을 했는지</b>다. 현장에서 둘을 같은 칸에 적으면
    /// 나중에 "누가 언제 어디 갔나" 를 찾을 수 없다.
    /// ⚠️ 경비(교통비·숙박비) 청구와 짝이 되는 문서이기도 하다.
    /// </remarks>
    public const string Business = "business";

    /// <summary>결재 문서유형 접두. <c>approval_settings.doc_type</c> 과 짝이다.</summary>
    public const string DocTypePrefix = "report_";

    /// <summary>알 수 없는 값이 오면 일일보고서로 본다(예외 대신 폴백).</summary>
    public static string Normalize(string? value) => value switch
    {
        Weekly => Weekly,
        Monthly => Monthly,
        Incident => Incident,
        Business => Business,
        _ => Daily
    };

    /// <summary>화면·알림에 쓰는 이름. 🔴 고객 노출 영역에 영문 코드가 뜨면 안 된다.</summary>
    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Weekly => "주간보고서",
        Monthly => "월간보고서",
        Incident => "경위서",
        Business => "출장·외근보고서",
        _ => "일일보고서"
    };

    /// <summary>결재 문서유형(<c>report_daily</c> 등).</summary>
    public static string ToDocType(string? value) => DocTypePrefix + Normalize(value);

    public static readonly string[] All = [Daily, Weekly, Monthly, Incident, Business];
}

/// <summary>업무보고서 상태.</summary>
/// <remarks>
/// 🔴 <c>draft</c> 를 두는 이유 — 월간보고서는 한 번에 다 못 쓴다. 저장해 두고 이어 쓴다.
/// 결재는 <c>pending</c> 부터 돈다(헌법 #6 — 확정은 사람이).
/// </remarks>
public static class WorkReportStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static string DisplayName(string? value) => value switch
    {
        Pending => "결재중",
        Approved => "완료",
        Rejected => "반려",
        _ => "작성중"
    };
}

/// <summary>업무보고서 목록 한 줄.</summary>
public sealed class WorkReportListDto
{
    public string ReportId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string ReportType { get; set; } = WorkReportTypes.Daily;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = WorkReportStatuses.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>반려 사유. 🔴 이걸 안 내려주면 반려된 사람이 이유를 모른다.</summary>
    public string? RejectReason { get; set; }

    public string ReportTypeLabel => WorkReportTypes.DisplayName(ReportType);
    public string StatusLabel => WorkReportStatuses.DisplayName(Status);
}

/// <summary>업무보고서 상세.</summary>
public sealed class WorkReportDetailDto
{
    public string ReportId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string ReportType { get; set; } = WorkReportTypes.Daily;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>경위서 전용 — 원인.</summary>
    public string? Cause { get; set; }

    /// <summary>경위서 전용 — 재발방지 대책.</summary>
    public string? ActionPlan { get; set; }

    public string Status { get; set; } = WorkReportStatuses.Draft;
    public DateTime? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string ReportTypeLabel => WorkReportTypes.DisplayName(ReportType);
    public string StatusLabel => WorkReportStatuses.DisplayName(Status);
}

/// <summary>업무보고서 작성·수정 요청.</summary>
public sealed class SaveWorkReportRequest
{
    public string ReportType { get; set; } = WorkReportTypes.Daily;
    public DateTime PeriodStart { get; set; } = DateTime.Today;
    public DateTime PeriodEnd { get; set; } = DateTime.Today;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public string? ActionPlan { get; set; }

    /// <summary>true 면 저장 후 바로 결재에 올린다. false 면 작성중으로만 저장한다.</summary>
    /// <remarks>
    /// 🔴 반자동 원칙(사장님 2026-08-12) — 저장과 상신을 가른다.
    /// 저장하자마자 결재가 올라가면 쓰다 만 보고서가 결재자에게 간다.
    /// </remarks>
    public bool Submit { get; set; }
}

/// <summary>
/// 상신 결과. <b>저장됐는지</b>와 <b>결재가 실제로 올라갔는지</b>를 따로 알려준다.
/// </summary>
/// <remarks>
/// 🔴 봉합 (2026-08-13, 단계3 검증 P0-1): 종전엔 <c>bool</c> 하나만 돌려줘서 화면이
/// 서버 200 만 보고 무조건 <i>"결재에 올렸습니다"</i> 를 띄웠다. 그런데 결재 설정이 꺼져 있거나
/// (<c>approval_settings</c> 실측 <b>0행</b>) 결재선이 없으면(<c>approval_doc_lines</c> 실측 <b>0행</b>)
/// <c>ApprovalTriggerHelper</c> 가 <b>조용히 지나간다</b> ⇒ 보고서는 <c>pending</c> 에 남고
/// 결재함엔 안 뜬다. <b>아무도 승인할 수 없는 영구 대기</b>인데 직원은 올렸다고 믿는다.
/// (실측 재현: 상신 후 <c>hr_reports.status='pending'</c>, <c>approval_documents</c> <b>0건</b>)
///
/// 되는 척을 없애려면 <b>화면이 사실을 알아야</b> 한다. 그래서 결과를 갈라 돌려준다.
/// </remarks>
public class SubmitWorkReportResult
{
    /// <summary>보고서 저장·상태 전이가 됐는가.</summary>
    public bool Saved { get; set; }

    /// <summary>결재 문서가 실제로 만들어졌는가.</summary>
    public bool ApprovalCreated { get; set; }

    /// <summary>
    /// 결재가 안 만들어진 이유. 화면이 그대로 보여준다(사용자 말로 쓴다).
    /// 결재가 만들어졌으면 null.
    /// </summary>
    public string? ApprovalSkipReason { get; set; }
}

/// <summary>새 보고서 작성 결과. 상신까지 한 경우 결재 성사 여부가 함께 담긴다.</summary>
public sealed class CreateWorkReportResult : SubmitWorkReportResult
{
    /// <summary>만들어진 보고서 ID.</summary>
    public string ReportId { get; set; } = string.Empty;
}
