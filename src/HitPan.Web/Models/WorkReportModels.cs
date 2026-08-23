namespace HitPan.Web.Models;

/// <summary>
/// 업무보고서 종류. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// 서버 <c>WorkReportTypes</c> 와 <b>같은 값</b>이어야 한다.
/// 🔴 값이 어긋나면 화면에서 고른 종류가 서버에서 다른 것으로 저장된다.
/// </remarks>
public static class WorkReportTypes
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Incident = "incident";

    /// <summary>출장·외근보고서. 작(2026-08-24) 작3 — 사장님: "보고서관리 : 출장/외근보고서 추가"</summary>
    /// <remarks>🔴 서버 <c>WorkReportTypes</c> 와 <b>같은 값</b>이어야 한다. 다르면 라벨이 영문으로 뜬다.</remarks>
    public const string Business = "business";

    public static string DisplayName(string? value) => value switch
    {
        Weekly => "주간보고서",
        Monthly => "월간보고서",
        Incident => "경위서",
        Business => "출장·외근보고서",
        _ => "일일보고서"
    };
}

/// <summary>업무보고서 상태.</summary>
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
/// <remarks>
/// 🔴 <b>속성명이 서버 DTO 와 같아야 한다.</b> 다르면 값이 비어 보인다 —
/// 8/12 에 계약서 급여가 항상 0원이던 원인이 정확히 이것이었다(API 는 보내는데 화면이
/// 다른 이름으로 받았다). 500 이 안 나서 더 위험하다.
/// </remarks>
public sealed class WorkReportListModel
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

    /// <summary>반려 사유. 🔴 이걸 안 보여주면 반려된 사람이 이유를 모른다.</summary>
    public string? RejectReason { get; set; }

    public string ReportTypeLabel => WorkReportTypes.DisplayName(ReportType);
    public string StatusLabel => WorkReportStatuses.DisplayName(Status);

    /// <summary>고칠 수 있는 상태인가. 결재중·완료는 못 고친다.</summary>
    public bool CanEdit => Status is WorkReportStatuses.Draft or WorkReportStatuses.Rejected;
}

/// <summary>업무보고서 상세.</summary>
public sealed class WorkReportDetailModel
{
    public string ReportId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string ReportType { get; set; } = WorkReportTypes.Daily;
    public DateTime PeriodStart { get; set; } = DateTime.Today;
    public DateTime PeriodEnd { get; set; } = DateTime.Today;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Cause { get; set; }
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
    public bool CanEdit => Status is WorkReportStatuses.Draft or WorkReportStatuses.Rejected;
}

/// <summary>업무보고서 저장 요청.</summary>
public sealed class SaveWorkReportModel
{
    public string ReportType { get; set; } = WorkReportTypes.Daily;
    public DateTime PeriodStart { get; set; } = DateTime.Today;
    public DateTime PeriodEnd { get; set; } = DateTime.Today;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public string? ActionPlan { get; set; }

    /// <summary>true 면 저장 후 바로 결재에 올린다.</summary>
    public bool Submit { get; set; }
}

/// <summary>
/// 저장·상신 결과. <b>저장됐는지</b>와 <b>결재가 실제로 올라갔는지</b>를 가른다.
/// </summary>
/// <remarks>
/// 🔴 봉합 (2026-08-13, 검증 P0-1): 종전엔 성공 여부 하나만 받아, 결재 설정이 꺼져 있어도
/// 화면이 <i>"결재에 올렸습니다"</i> 를 띄웠다. 실제로는 결재문서가 안 만들어져
/// <b>아무도 승인할 수 없는 상태</b>였다(실측: <c>approval_documents</c> 0건).
/// 화면은 이제 <see cref="ApprovalCreated"/> 를 보고 말을 가른다.
/// </remarks>
public sealed class WorkReportSaveOutcome
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public string? ReportId { get; set; }

    /// <summary>결재 문서가 실제로 만들어졌는가.</summary>
    public bool ApprovalCreated { get; set; }

    /// <summary>결재가 안 올라간 이유(사용자에게 그대로 보여준다).</summary>
    public string? ApprovalSkipReason { get; set; }
}
