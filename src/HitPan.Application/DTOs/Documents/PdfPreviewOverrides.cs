using HitPan.Application.DTOs.Settings;

namespace HitPan.Application.DTOs.Documents;

/// <summary>
/// 미리보기 주입 인자 — <b>저장하지 않은 양식값</b>과 <b>예시 자료</b>를 정본 통로에 먹인다.
/// 근거: 20261021작 §11-3·§11-6 · §19-2 ㉯ (사장님 결재 2026-10-21) · 사장님 오더 2026-10-24
///   <i>"미리보기-&gt; 인쇄까지 모두 양식지 적용이 되도록 해야지"</i>
/// </summary>
/// <remarks>
/// 🔴 <b>왜 「인자」인가 — 두 벌을 만들지 않기 위해서다.</b>
///   미리보기 전용으로 따로 그리는 코드를 만들면 인쇄물과 조용히 어긋난다.
///   그 사고를 20261021 차수가 닫는 중이었다. 그래서 미리보기는 <b>같은 작성기</b>를 타고,
///   다른 것은 <b>먹이는 자료</b>뿐이다.
///
/// 🔴 <b>조건 1 (결재 ㉯)</b> — 이 인자를 주지 않으면 <b>지금과 100% 같게</b> 동작해야 한다.
///   기존 호출부는 한 글자도 고치지 않았다(#1). 4인자 호출은 그대로 살아 있고
///   내부에서 <c>overrides: null</c> 로 내려간다.
///
/// 🔴 <b>조건 3 (결재 ㉯) 음성 대조군</b> — 서비스에서 override 를 먹이는 줄을 빼면
///   「저장 전 값으로 그린다」가 <b>실패해야</b> 한다. 조용히 저장값으로 떨어지면 안 된다.
/// </remarks>
public sealed class PdfPreviewOverrides
{
    /// <summary>
    /// 저장하지 않은 양식값. 주면 DB 의 <c>form_templates</c> 행 <b>대신</b> 이것을 쓴다.
    /// 사장님 지시 <i>"실제 출력물 양식을 편집하는 대로 보여주는 게 중요함"</i> (§11).
    /// </summary>
    public FormTemplateDto? Template { get; set; }

    /// <summary>
    /// 전표 없이 <b>예시 자료</b>로 그린다 — 양식정보설정은 6단계 중 1단계인데
    /// 전표는 3~4단계라, 신규 고객이 양식을 맞추는 그 시점에 전표가 0건이다(§11-2 ②).
    /// </summary>
    public bool UseSampleData { get; set; }

    /// <summary>
    /// 예시 자료의 품목 줄 수. 🔴 <b>이 화면의 목적이 여기다</b> —
    /// 「몇 줄부터 두 쪽이 되나」·「몇 줄부터 외 N건이 되나」를 <b>눈으로</b> 본다(§11-3).
    /// </summary>
    public int SampleLineCount { get; set; } = 3;

    /// <summary>
    /// 종이에 「예시」 표시를 찍는다. 🔴 예시를 <b>진짜 계산서로 오해하면 안 된다</b>(§11-3).
    /// 예시 자료를 쓸 때는 끄지 못한다(서비스가 강제한다).
    /// </summary>
    public bool MarkAsSample { get; set; } = true;
}
