using System.ComponentModel.DataAnnotations;
using HitPan.Contracts.Forms;

namespace HitPan.Application.DTOs.Settings;

/// <summary>
/// 🔴 인쇄 공통 설정 — 테넌트 <b>한 벌</b> (<c>print_common_settings</c> · DB-148).
/// </summary>
/// <remarks>
/// 🔴 사장님 결재 2026-10-25(결-I): <i>"공통 + 양식마다 — 단 고객 선택 옵션"</i>
///   여기 값은 <b>모든 양식의 기본</b>이고, 양식마다 수치를 비워 두면(<c>null</c>) 이 값을 따른다.
///   양식에 값을 넣으면 그 양식만 덮는다.
///
/// 🔴 사장님 결재 2026-10-26(결-J): <i>"응 당연히 미리보기에서 미리 보기반영되도록"</i>
///   ⇒ 이 값들도 고치는 즉시 미리보기로 확인할 수 있어야 한다.
///
/// 🔴 <b>기본값은 제안값이다</b> — 개발팀이 정하는 값이 아니다.
///   행이 없는 고객에게도 같은 숫자가 보여야 하므로 <see cref="WatermarkDefaults"/> 한 자리에서 온다.
/// </remarks>
public sealed class PrintCommonSettingsDto
{
    /// <summary>기본으로 로고 워터마크를 깔까 — 양식마다 끌 수 있다.</summary>
    public bool WatermarkEnabled { get; set; }

    /// <summary>워터마크 진하기 0.00~1.00. 🔴 0 에 가까울수록 연하다.</summary>
    [Range(0.0, 1.0)]
    public decimal WatermarkOpacity { get; set; } = WatermarkDefaults.Opacity;

    /// <summary>워터마크 색감 0.00~1.00 (0=흑백).</summary>
    [Range(0.0, 1.0)]
    public decimal WatermarkSaturate { get; set; } = WatermarkDefaults.Saturate;

    /// <summary>워터마크 크기 — 종이 너비 대비 %.</summary>
    [Range(10, 100)]
    public int WatermarkScalePct { get; set; } = WatermarkDefaults.ScalePct;

    /// <summary>
    /// 새 양식을 만들 때 「양식지 그림을 종이에도 인쇄」의 초기값.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이미 만들어 둔 양식은 바꾸지 않는다</b> — 이 값을 켜도 기존 양식의 종이는 그대로다.
    ///   한 칸을 켜서 고객의 모든 종이가 한꺼번에 바뀌는 길은 만들지 않는다(#1).
    /// </remarks>
    public bool PrintBackgroundDefault { get; set; }
}
