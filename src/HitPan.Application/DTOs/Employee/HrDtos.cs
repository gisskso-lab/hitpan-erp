namespace HitPan.Application.DTOs.Employee;

// ── 출퇴근 ──
public class AttendanceDto
{
    public string AttendanceId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public decimal? WorkHours { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class CheckInOutRequest
{
    public string? Memo { get; set; }
}

// ── 초과근무 ──
public class OvertimeDto
{
    public string OvertimeId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public decimal Hours { get; set; }
    public string OvertimeType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateOvertimeRequest
{
    public DateTime WorkDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string OvertimeType { get; set; } = "weekday";
    public string? Reason { get; set; }
}

// ── HR 경비신청 ──
public class HrExpenseRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateHrExpenseRequest
{
    public DateTime RequestDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
