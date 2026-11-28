namespace HitPan.Application.DTOs.Settings;

/// <summary>
/// 🆕 2026-11-28 쿨타임1b(설계 쿨타임_설정자리옮김 §3) — 자동 로그아웃 설정 세 칸 읽기(<c>GET api/settings/cooltime</c>).
/// 🔴 tenant · user 칸 없음(#2 · 회사는 JWT 에서만).
/// </summary>
public sealed class CooltimeSettingsDto
{
    /// <summary>자동 로그아웃 켬/끔. 행 없음 = 끔.</summary>
    public bool CooltimeEnabled { get; set; }

    /// <summary>자동 로그아웃 시각 <c>"HH:mm"</c>. 정하지 않음 = <c>null</c>.</summary>
    public string? CooltimeAt { get; set; }

    /// <summary>정시 퇴근 시각 <c>"HH:mm"</c>. 정하지 않음 = <c>null</c>.</summary>
    public string? RegularLeaveTime { get; set; }
}
