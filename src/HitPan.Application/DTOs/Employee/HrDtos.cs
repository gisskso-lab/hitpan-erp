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

    /// <summary>
    /// 🆕 20260928작4 쿨타임 §6 · DB-134 — 퇴근 기록이 어디서 왔나(추가 · #1).
    /// <c>NULL</c> = 사람 · <c>cooltime</c> = 자동 로그아웃(정시 퇴근) · <c>cooltime_blank</c> = 자동 로그아웃 · 퇴근 빈칸 ·
    /// <c>edited</c> = 인사담당이 고침. 화면은 이 값으로 「자동」 · 「고침」 표시를 단다.
    /// </summary>
    public string? CheckOutSource { get; set; }
}

// ── 🆕 20260928작4 쿨타임 §6 · C-5 — 퇴근 고치기 · 고치기 이력 (추가 · #1) ──

/// <summary>퇴근 고치기 요청. 대상 행은 주소(<c>{attendanceId}</c>)로 · 회사는 JWT 로만 정한다(#2).</summary>
public class EditCheckOutRequest
{
    /// <summary>고칠 퇴근 시각(서버 PC 로컬 · 출근 시각과 같은 기준).</summary>
    public DateTime? CheckOut { get; set; }

    /// <summary>고친 까닭 — 필수.</summary>
    public string? Reason { get; set; }
}

/// <summary>퇴근 고치기 결과. 컨트롤러가 응답 코드로 바꾼다.</summary>
public enum EditCheckOutOutcome
{
    /// <summary>고쳤다 — 행 1 · 이력 1.</summary>
    Saved,
    /// <summary>우리 회사에 그런 근태 행이 없다 → 404(다른 회사 행도 여기로 · 있는지조차 알리지 않는다).</summary>
    NotFound,
    /// <summary>자동 로그아웃으로 적힌 퇴근이 아니다(C-5 (가)) → 409.</summary>
    NotEditable,
    /// <summary>시각 · 사유가 틀렸다 → 400.</summary>
    Invalid
}

/// <summary>퇴근 고치기 결과와 화면에 보여 줄 문구.</summary>
public sealed record EditCheckOutResult(EditCheckOutOutcome Outcome, string Message);

/// <summary>퇴근 고치기 이력 한 줄 — <c>audit_trail</c>(entity_type='attendance') 을 화면 말로 풀어 둔 것.</summary>
public class AttendanceEditHistoryDto
{
    public DateTime ChangedAt { get; set; }
    /// <summary>고친 사람 이름(계정 이름). 계정이 지워졌으면 빈 값.</summary>
    public string? ChangedByName { get; set; }
    public DateTime? BeforeCheckOut { get; set; }
    public DateTime? AfterCheckOut { get; set; }
    /// <summary>고치기 전 출처(<c>cooltime</c> · <c>cooltime_blank</c>).</summary>
    public string? BeforeSource { get; set; }
    public string? Reason { get; set; }
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
