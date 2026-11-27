using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 20261127작 W-S-01 갈래 B — 운영 판의 초기화 열림 여부(설계 §3-2 의 1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>다시 여는 길 = 아래 한 줄(false → true)뿐.</b> 순서 = W-G1-52 최소 복구 → 대표 확인(결재 기록) →
/// 이 한 줄 PR(루틴 [2] ~ [5]) → ERP 게시(#43 Major 팝업 Y/N 사슬 그대로). 대표 확인 없이 바꾸지 않는다.
/// </para>
/// <para>
/// 화면(사이드바 · 초기화 화면 · 자료이관 단추)은 서버 글자 「closed」 를 따르므로 다시 열 때 화면 손은 0 이다.
/// </para>
/// </remarks>
public sealed class DataResetBetaAvailability : IDataResetAvailability
{
    /// <inheritdoc />
    public bool IsOpen => false;
}
