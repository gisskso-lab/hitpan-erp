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

/// <summary>
/// 🔴 <b>대리 근태 요청</b> — 계정 없는 직원분을 대신 넣는다. 작(2026-08-21) 작10 A.
/// </summary>
/// <remarks>
/// 🚨 <c>EmployeeId</c> 는 <b>대상 직원</b>이다(요청자가 아니다). 요청자는 JWT 에서 온다.
/// <c>TenantId</c> 는 <b>여기 두지 않는다</b> — 파라미터로 받으면 헌법 #2 위반이다.
/// </remarks>
public class ProxyCheckInRequest
{
    /// <summary>대상 직원. 내 테넌트 소속인지 서비스가 검증한다.</summary>
    public string EmployeeId { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

/// <summary>🔴 대리 퇴근 요청. <see cref="ProxyCheckInRequest"/> 와 같은 원칙.</summary>
public class ProxyCheckOutRequest
{
    public string EmployeeId { get; set; } = string.Empty;
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
