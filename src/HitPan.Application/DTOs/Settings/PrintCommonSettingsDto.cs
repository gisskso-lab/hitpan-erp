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

    /// <summary>워터마크 크기 — 종이 <b>짧은 변</b> 대비 %.</summary>
    /// <remarks>
    /// 🔴 [2026-10-26 [4] 반려 R-1 봉합] 종전에는 「종이 너비 대비」였다. 워터마크는 정사각이라
    ///   <b>가로 용지에서 71% 만 넘어도 종이 높이를 넘어 그림이 통째로 빠졌다</b>(예외·로그 0).
    ///   짧은 변 기준이면 100% 가 「종이에 꽉 차는 가장 큰 정사각」이다.
    /// </remarks>
    [Range(10, 100)]
    public int WatermarkScalePct { get; set; } = WatermarkDefaults.ScalePct;

    // 🔴🔴 [2026-10-26 [3-V] P1-4 · [4] R-7 봉합] `print_background_default` 칸을 **여기서 뺐다.**
    //   종전에는 이 칸이 *"새 양식을 만들 때 print_background 의 초기값"* 이라고 **약속**했는데
    //   그 값을 **쓰는 자리가 0건**이었다. 더 나쁜 것은 화면이 이 칸을 묶지 않아
    //   공통 설정을 한 번 저장할 때마다 **0 으로 조용히 덮었다**(#1 덮어쓰기 금지).
    //   ⇒ DB 칸은 **예약으로 남기고**(#37 — 칸은 지우지 않는다) 약속만 걷어냈다.
    //     쓰는 차수에 화면·`CreateAsync` 와 함께 배선한다.
}
