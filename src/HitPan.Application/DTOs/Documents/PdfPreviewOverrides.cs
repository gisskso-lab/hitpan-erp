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

    /// <summary>
    /// 🔴🔴 <b>양식 설계 미리보기인가</b> — 양식지 그림(배경)을 깔지 말지 가른다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 따로 두나</b> — 종전에는 「overrides 가 왔으면 미리보기」로 <b>대리 판정</b>했다.
    ///   그런데 <b>전표 인쇄 미리보기</b>도 같은 overrides 를 탄다. 거기에 배경이 깔리면
    ///   <b>미리보기에는 그림이 있고 실제 인쇄물에는 없다</b> ⇒ 사장님 합격선
    ///   <i>"미리보기 화면하고 동일하게 인쇄"</i> 가 <b>거짓</b>이 된다(검증팀 재검증 C-3).
    /// <para>
    /// 🟢 <c>true</c>  = 양식정보설정의 미리보기. 스캔한 양식지 위에 필드를 mm 로 맞춘다(§13-8).
    /// 🟢 <c>false</c> = <b>전표 인쇄 미리보기</b>. 그 종이가 곧 인쇄물이다 — 배경 <b>0건</b>.
    /// </para>
    /// </remarks>
    public bool IsFormDesignPreview { get; set; }

    /// <summary>
    /// 🔴 이번 인쇄에만 적용할 옵션 (결-2 · 7종 공통). <b>null 이면 저장된 양식 그대로</b>.
    /// </summary>
    public PrintOptionOverrides? Options { get; set; }

    /// <summary>
    /// 🔴🔴 <b>DB 에 없는 전표면 그리지 말고 막는다</b> (3차검증 R-11 봉합).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 생겼나</b> — 저장 안 한 전표의 <c>_draft.Id</c> 는 <b>화면이 만든 GUID</b> 다.
    ///   DB 에 없다. 창문은 <c>IsNullOrWhiteSpace(docId)</c> 하나뿐이라 GUID 는 통과하고
    ///   <c>WHERE return_id='&lt;GUID&gt;'</c> 가 0행 ⇒ <b>미리보기도 종이도 백지</b>였다.
    ///   로더를 붙여 막으려던 사고가 <b>다른 문으로 그대로 들어왔다</b>(검증팀 3차 R-11).
    /// <para>
    /// 🔴 <b>왜 화면이 아니라 여기서 막나</b> — 문이 하나가 아니다. 단추 8곳 말고
    ///   <b>API 직타</b>도 있고, 로더가 없는 종(면세계산서·입금표)도 같은 백지로 떨어진다.
    ///   <b>한 자리에서 막아야</b> 다음 종이 늘 때 또 안 빈다.
    /// </para>
    /// <para>
    /// 🔴 <c>true</c> 는 <b>미리보기 문만</b> 세운다. 「인쇄」·「이메일」·「팩스」는 종전처럼
    ///   <b>안내문 종이</b>가 나간다 — 그 동작은 한 글자도 안 바꿨다(#1 · #20 P0).
    /// </para>
    /// </remarks>
    public bool RequireExisting { get; set; }
}

/// <summary>
/// 🔴 전표 인쇄 미리보기 옵션 — <b>7종 공통 한 벌</b> (사장님 결재 2026-10-24 결-2).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 양식 DTO 를 통째로 안 받나</b> — 전표 인쇄는 <b>저장된 양식을 그대로 쓰고</b>
///   그 위에 <b>이번 한 번만</b> 바꿀 값을 얹는다. 양식 전체를 받으면 화면이 양식을
///   들고 다녀야 하고, 고객이 미리보기에서 만진 값이 <b>저장된 것처럼</b> 보인다.
/// 🔴 <b>전부 null 이면 저장된 양식 그대로</b> — 즉 지금 인쇄와 같은 종이가 나온다(#1).
/// 🔴 종별로 갈라 만들지 않는다. 갈라 만들면 <b>일곱 벌</b>이 되고 칸이 늘 때마다
///   일곱 군데를 고쳐야 한다(결-2 — 사장님 <i>"공통"</i>).
/// </remarks>
public sealed class PrintOptionOverrides
{
    /// <summary>인쇄 매수 — both·recipient·supplier. 세금계산서·계산서 축(DB-90).</summary>
    public string? PrintCopyMode { get; set; }

    /// <summary>한 장에 두 벌 + 가운데 자르는 선 (DB-147). <c>PrintCopyMode=both</c> 일 때만 쓰인다.</summary>
    public bool? ShowTearLine { get; set; }

    /// <summary>
    /// 🔴 양식지 그림을 종이에도 찍나 (DB-148) — 🔴 <b>미리보기로 확인하고 고르게</b> 한다.
    /// </summary>
    /// <remarks>
    /// 사장님 결재 2026-10-26(결-J): <i>"응 당연히 미리보기에서 미리 보기반영되도록"</i>.
    /// 🔴 이 칸이 없으면 고객은 <b>저장한 뒤 실제로 뽑아 봐야</b> 결과를 알 수 있다.
    /// </remarks>
    public bool? PrintBackground { get; set; }

    /// <summary>로고 워터마크를 깔까 (DB-148) — 미리보기에서 켜 보고 고른다.</summary>
    public bool? ShowWatermark { get; set; }

    /// <summary>
    /// 워터마크 명도 0.00~1.00 (DB-148).
    /// 🔴 <b>저장하지 않은 값으로 미리보기가 나와야 한다</b> — 그래야 고객이 숫자를 고를 수 있다.
    /// </summary>
    public decimal? WatermarkOpacity { get; set; }

    /// <summary>워터마크 채도 0.00~1.00 (DB-148).</summary>
    public decimal? WatermarkSaturate { get; set; }

    /// <summary>워터마크 크기 — 용지 폭 대비 % (DB-148).</summary>
    public int? WatermarkScalePct { get; set; }
}
