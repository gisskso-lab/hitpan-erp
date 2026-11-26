namespace HitPan.Web.Services;

/// <summary>
/// 🔴 2026-11-26 W-G1-51 R1 회송 F-2(CTO 전결 §226 · PM 결재 §227) — 반품처리 화면 [이메일] 이 서버에 넘길 PDF 문서 키를 고른다.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 매입 반품 [이메일] 이 키 <c>"purchase_receipt"</c> 를 넘겨, 서버가 매입명세서 로더로 반품 id 를 찾았다.
/// 못 찾으니 머리 「매입명세서」 · 거래처 칸 「(원본 문서를 찾을 수 없습니다)」 · 품목 0 인 안내문 종이가 거래처 메일로 나갔다.
/// 키가 <c>"purchase_return"</c> 이면 서버 <c>PdfRenderService.LoadDocumentAsync</c> 가 매입반품 로더를 타고 제목 표
/// <c>KoLabel</c> 이 「매입반품」 을 찍는다([인쇄] 와 같은 렌더 · 같은 종이).
/// </para>
/// <para>
/// 🔴 판정 재료 = 「불러온 전표가 지금 화면의 전표인가」 · 「그 전표가 판매반품인가」(ReturnPage 의 <c>IsLoadedReturnOnScreen</c> ·
/// <c>_loadedReturnIsSales</c>). 불러오지 않은(저장 안 한 새) 전표는 <b>종전 키 그대로</b>다(CTO §226-2 「안 불러옴 → 지금 동작 그대로」).
/// </para>
/// <para>
/// Blazor 의존 0 순수 C# — 시험 프로젝트가 소스를 링크해 직접 부른다(<see cref="DataResetMenuRule"/> 선례).
/// </para>
/// </remarks>
public static class ReturnEmailKeyRule
{
    /// <summary>매입반품 렌더 키 — 서버 로더 · 제목 표의 키(밑줄 꼴).</summary>
    public const string PurchaseReturn = "purchase_return";

    /// <summary>판매반품 렌더 키 — 서버 로더 · 제목 표의 키(밑줄 꼴).</summary>
    public const string SalesReturn = "sales_return";

    /// <summary>불러오지 않은 전표의 키 — 종전 값 그대로(#1 · 이번 고침 범위 밖).</summary>
    public const string NotLoaded = "purchase_receipt";

    /// <summary>
    /// 불러온 판매반품 = <see cref="SalesReturn"/> · 불러온 매입반품 = <see cref="PurchaseReturn"/> · 불러오지 않음 = <see cref="NotLoaded"/>.
    /// </summary>
    public static string Pick(bool loadedReturnOnScreen, bool loadedReturnIsSales) =>
        !loadedReturnOnScreen ? NotLoaded
        : loadedReturnIsSales ? SalesReturn
        : PurchaseReturn;
}
