using HitPan.Application.DTOs.Payroll;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 급여·퇴직금. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i>
///
/// 이 서비스는 <b>4대보험·소득세를 계산하지 않는다.</b> 금액을 받아 담고, 명세로 뽑는다.
/// 서버가 하는 계산은 <b>줄의 합계</b>뿐이다(그것도 화면 값을 안 믿기 위해서다).
/// </remarks>
public interface IPayrollService
{
    // ── 급여 명세 ──

    /// <summary>그 달 명세 목록.</summary>
    Task<List<PayrollSlipDto>> GetSlipsAsync(string tenantId, int year, int month,
        string? employeeId, CancellationToken ct = default);

    /// <summary>명세 한 건(항목 포함).</summary>
    Task<PayrollSlipDto?> GetSlipAsync(string tenantId, string slipId, CancellationToken ct = default);

    /// <summary>
    /// 그 달 급여를 만들 때 <b>참고할 것들</b>. 🔴 자동으로 채우지 않고 보여만 준다.
    /// 휴직자의 경우 단계6 에서 사람이 정해 둔 금액을 함께 준다.
    /// </summary>
    Task<List<PayrollContextDto>> GetContextAsync(string tenantId, int year, int month,
        CancellationToken ct = default);

    /// <summary>명세 저장(신규·수정). 합계는 서버가 줄을 더해 낸다.</summary>
    Task<string> SaveSlipAsync(string tenantId, string actorId, SavePayrollSlipRequest request,
        CancellationToken ct = default);

    /// <summary>확정. 확정하면 못 고친다 — 확정한 급여가 뒤에서 바뀌면 명세서가 거짓이 된다.</summary>
    Task ConfirmSlipAsync(string tenantId, string actorId, string slipId, CancellationToken ct = default);

    /// <summary>지급 완료 표시. 지급일을 사람이 넣는다.</summary>
    Task MarkPaidAsync(string tenantId, string actorId, string slipId, DateTime payDate,
        CancellationToken ct = default);

    /// <summary>취소.</summary>
    Task CancelSlipAsync(string tenantId, string actorId, string slipId, CancellationToken ct = default);

    // ── 퇴직금 ──

    /// <summary>퇴직금 목록.</summary>
    Task<List<SeverancePaymentDto>> GetSeveranceListAsync(string tenantId, string? employeeId,
        CancellationToken ct = default);

    /// <summary>퇴직금 저장(신규·수정). 🔴 금액을 사람이 넣는다.</summary>
    Task<string> SaveSeveranceAsync(string tenantId, string actorId, SaveSeveranceRequest request,
        CancellationToken ct = default);

    /// <summary>퇴직금 확정.</summary>
    Task ConfirmSeveranceAsync(string tenantId, string actorId, string severanceId,
        CancellationToken ct = default);
}
