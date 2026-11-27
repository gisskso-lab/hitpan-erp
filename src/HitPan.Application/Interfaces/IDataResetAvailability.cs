namespace HitPan.Application.Interfaces;

/// <summary>
/// 20261127작 W-S-01 갈래 B — 「모든데이터 초기화」 가 이 판에서 열려 있는지(설계 §3-2 의 1 · K25 · §256 · §258).
/// </summary>
/// <remarks>
/// <para>
/// 닫힘은 <b>실행 파일 판</b>에 묶는다 — 설정 파일(<c>appsettings*</c> · #21) · DB 칸(#36)에 두지 않는다.
/// 고객 PC 에서 손으로 열 수 없어야 「최소 복구 뒤 대표 확인으로만 다시 연다」 가 지켜진다.
/// </para>
/// <para>
/// #12 구현체: <c>DataResetBetaAvailability</c>(운영 · 닫힘) · <c>DataResetAvailabilityForTest</c>(시험 · 열림/닫힘을 시험이 고름).
/// </para>
/// </remarks>
public interface IDataResetAvailability
{
    /// <summary>참 = 초기화를 부를 수 있다 / 거짓 = <c>DataResetService.ResetAllAsync</c> 첫 문장에서 「closed」 로 거절.</summary>
    bool IsOpen { get; }
}
