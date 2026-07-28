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
