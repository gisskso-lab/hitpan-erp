namespace HitPan.Application.DTOs.Settings;

/// <summary>
/// 🆕 2026-11-28 쿨타임1b(설계 쿨타임_설정자리옮김 §3) — 자동 로그아웃 설정 세 칸 저장(<c>PUT api/settings/cooltime</c>).
/// 🔴 세 칸 모두 「<c>null</c> = 그대로 둔다」(<see cref="UpdateTenantSettingsDto"/> 의 같은 칸과 같은 약속).
///    시각 두 칸은 빈 문자열 = 비운다.
/// 🔴 tenant · user 칸 없음(#2 · 회사는 JWT 에서만).
/// </summary>
public sealed class UpdateCooltimeSettingsDto
{
    /// <summary>자동 로그아웃 켬/끔. <c>null</c> = 그대로.</summary>
    public bool? CooltimeEnabled { get; set; }

    /// <summary>자동 로그아웃 시각 <c>"HH:mm"</c>. <c>null</c> = 그대로 · <c>""</c> = 비움.</summary>
    public string? CooltimeAt { get; set; }

    /// <summary>정시 퇴근 시각 <c>"HH:mm"</c>. <c>null</c> = 그대로 · <c>""</c> = 비움.</summary>
    public string? RegularLeaveTime { get; set; }
}
