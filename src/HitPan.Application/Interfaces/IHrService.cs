using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>인사·근태 통합 서비스</summary>
public interface IHrService
{
    // 출퇴근
    Task<List<AttendanceDto>> GetAttendanceAsync(string tenantId, DateTime? from, DateTime? to, string? employeeId, CancellationToken ct = default);
    Task<string> CheckInAsync(string tenantId, string employeeId, CheckInOutRequest req, CancellationToken ct = default);
    Task CheckOutAsync(string tenantId, string employeeId, CancellationToken ct = default);

    // 🔴 대리 근태 — 계정 없는 직원분. 작(2026-08-21) 작10 A.
    //    권한 HR_PROXY(5축 밖 별도 항목·기본 OFF)로만 통제된다.
    //    🚨 구현체는 대상 사원의 테넌트 소속을 반드시 검증한다(헌법 #2).
    Task<string> CheckInProxyAsync(string tenantId, string targetEmployeeId, string actorEmployeeId, CheckInOutRequest req, CancellationToken ct = default);
    Task CheckOutProxyAsync(string tenantId, string targetEmployeeId, string actorEmployeeId, CancellationToken ct = default);

    // 초과근무
    Task<List<OvertimeDto>> GetOvertimeAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<string> CreateOvertimeAsync(CreateOvertimeRequest req, string tenantId, string employeeId, CancellationToken ct = default);
    Task<bool> ApproveOvertimeAsync(string overtimeId, string tenantId, string action, CancellationToken ct = default);

    // HR 경비신청
    Task<List<HrExpenseRequestDto>> GetHrExpensesAsync(string tenantId, string? employeeId, CancellationToken ct = default);
    Task<string> CreateHrExpenseAsync(CreateHrExpenseRequest req, string tenantId, string employeeId, CancellationToken ct = default);

    /// <summary>
    /// 이 경비 신청이 <b>실제로 결재에 올라갔는지</b> 본다. 작(2026-08-13) 단계7.
    /// </summary>
    /// <remarks>
    /// 🔴 단계3 P0-1 교훈 — 결재 설정이 꺼져 있으면 결재 생성이 <b>조용히 건너뛴다</b>.
    /// 화면이 "신청했습니다" 만 띄우면 직원은 올라간 줄 알고 문서는 갇힌다.
    /// </remarks>
    Task<(bool Created, string? SkipReason)> CheckHrExpenseApprovalAsync(
        string tenantId, string requestId, CancellationToken ct = default);
}
