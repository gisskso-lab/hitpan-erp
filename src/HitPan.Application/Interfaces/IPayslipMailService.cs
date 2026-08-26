using HitPan.Application.DTOs.Payroll;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 급여명세서 일괄 메일발송. 20260826작6 W4.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 별도 서비스인가</b> — <c>PayrollService</c> 는 DB 만 잡고 있다(메일·PDF 의존이 없다).
/// 거기에 SMTP·PDF 를 끌어들이면 급여 조회·저장까지 그 의존을 지게 된다. 발송은 성격이 다르므로
/// 따로 세운다.
/// </para>
/// <para>
/// 🔴 <b>되돌릴 수 없는 기능이다</b> — 잘못 나가면 전 직원에게 남의 연봉이 간다. 회수 불가다.
/// 그래서 <b>미리보기 → 사람이 확인 → 발송</b> 2단으로 나눈다(사장님 반자동 원칙).
/// </para>
/// </remarks>
public interface IPayslipMailService
{
    /// <summary>
    /// 발송 전 확인용 명단. <b>보낼 수 있는 사람과 못 보내는 사람을 사유까지</b> 준다.
    /// </summary>
    /// <remarks>
    /// 🔴 숫자만 주면 안 된다 — <i>"20명 중 18명 발송 가능 / 2명 이메일 없음"</i> 에서
    /// <b>그 2명이 누구인지</b> 를 경리가 알아야 고칠 수 있다.
    /// </remarks>
    Task<PayslipSendPreviewDto> GetSendPreviewAsync(string tenantId, int year, int month,
        CancellationToken ct = default);

    /// <summary>
    /// 고른 명세서를 <b>건별로</b> 발송하고 <b>건별로</b> 결과를 돌려준다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 요청에 담긴 id 를 <b>그대로 믿지 않는다</b> — 서버가 다시 판정해서 못 보낼 것이면
    /// 보내지 않는다. 화면을 우회한 요청으로 미결재 명세서가 나가면 안 된다(⑤결재).
    /// </remarks>
    Task<SendPayslipMailResponse> SendAsync(string tenantId, string? actorUserId,
        SendPayslipMailRequest request, CancellationToken ct = default);
}
