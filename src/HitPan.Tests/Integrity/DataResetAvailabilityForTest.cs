using HitPan.Application.Interfaces;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20261127작 W-S-01 갈래 B — 시험용 초기화 열림 여부(#12 구현체 2 중 시험 쪽 · 운영 = <c>DataResetBetaAvailability</c>).
/// </summary>
/// <remarks>
/// #978 대표 판정 시험(<c>DataResetParentOnlyGateDbTests</c> · <c>LegacyUnpostedDdlGateTests</c> · <c>EmployeeAccountLinkGateDbTests.Pipeline</c>)은
/// <see cref="OpenForTest"/> 를 넘겨 「다시 열면 대표만」 뜻을 그대로 잰다. 닫힘 시험은 운영 구현을 그대로 쓴다(설계 §3-5 · §4 D-B3).
/// </remarks>
internal sealed class DataResetAvailabilityForTest : IDataResetAvailability
{
    /// <summary>열린 판 — 대표 판정 · 비번 · 백업 · 삭제가 그대로 돈다.</summary>
    public static readonly IDataResetAvailability OpenForTest = new DataResetAvailabilityForTest(isOpen: true);

    private DataResetAvailabilityForTest(bool isOpen) => IsOpen = isOpen;

    public bool IsOpen { get; }
}
