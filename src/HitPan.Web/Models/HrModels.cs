namespace HitPan.Web.Models;

public class AttendanceModel
{
    public string AttendanceId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public decimal? WorkHours { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class OvertimeModel
{
    public string OvertimeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public decimal Hours { get; set; }
    public string OvertimeType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateOvertimeModel
{
    public DateTime WorkDate { get; set; } = DateTime.Today;
    public TimeSpan StartTime { get; set; } = new(18, 0, 0);
    public TimeSpan EndTime { get; set; } = new(21, 0, 0);
    public string OvertimeType { get; set; } = "weekday";
    public string? Reason { get; set; }
}

public class HrExpenseModel
{
    public string RequestId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateHrExpenseModel
{
    public DateTime RequestDate { get; set; } = DateTime.Today;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>
/// 경비 신청 결과. 작(2026-08-13) 단계7.
/// </summary>
/// <remarks>
/// 🔴 <b>결재가 실제로 올라갔는지를 사실대로 담는다</b>(단계3 P0-1 교훈).
/// 종전엔 성공 여부만 봐서 "신청 완료" 를 무조건 띄웠고, 결재 설정이 꺼져 있으면
/// 직원은 올라간 줄 아는데 결재함엔 안 떴다.
/// </remarks>
public class CreateHrExpenseResultModel
{
    public string? Id { get; set; }

    /// <summary>결재 문서가 실제로 만들어졌나.</summary>
    public bool ApprovalCreated { get; set; }

    /// <summary>안 올라갔으면 그 이유(사용자 말로). 올라갔으면 null.</summary>
    public string? ApprovalSkipReason { get; set; }

    /// <summary>실패했을 때 서버가 보낸 이유. 예: "마감된 기간입니다".</summary>
    public string? Message { get; set; }
}
