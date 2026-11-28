namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ D13 — 복원 걸음 ④ 앞에서 「업데이트 내려받기 · 교체가 도는 중인가」 를 <b>읽기만</b> 하는 문.
/// 구현 = API(<c>ManualUpdateService</c> · 자동 업데이트 잠금 · #43 업데이트 로직 손 0). 자료이관(D4)은 <c>MigrationJobStore.AnyRunning</c> 을 복원 쪽이 직접 읽는다.
/// </summary>
public interface IRestoreBusyProbe
{
    /// <summary>한가하면 <c>null</c> · 도는 중이면 <c>SafeRestoreDenyCodes</c> 글자(<c>update_running</c>).</summary>
    string? BusyCode(string tenantId);
}
