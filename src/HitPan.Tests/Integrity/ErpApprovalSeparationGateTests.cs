using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>E1</b> — ERP 결재를 그룹웨어 창구에서 분리했는가 (작20260823작1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 오더</b> — <i>"erp와 그룹웨어 결재를 묶으면 안됨. 분리해"</i>
/// <i>"erp 자료는 '확정' 이라는 단계가 이미 있음"</i>
/// <i>"이거 넣고 결재 돌리면 난리난다. 하루에 수십수백건씩 견적, 발주, 거래가 잡힐텐데"</i>
/// </para>
/// <para>
/// 🔴 <b>안 하면 무슨 일이 나나</b> — 거래명세서는 가장 많이 나오는 문서다.
/// 결재함이 그것으로 덮이면 <b>휴가·경비·보고서가 안 보인다.</b>
/// 상무님 경고: <i>"1번에 모으는 만큼 1번이 막히면 회사가 선다."</i>
/// </para>
/// <para>
/// 🔴 <b>이 시험이 지키는 두 방향</b> — 빼는 것과 <b>남기는 것</b>이 같이 걸려 있다.
/// ① ERP 는 목록에서 빠져야 하고 ② 라벨은 계속 찾아져야 하며
/// ③ 그룹웨어 종류는 그대로 있어야 한다. 하나만 지키면 나머지가 깨진다.
/// </para>
/// </remarks>
public class ErpApprovalSeparationGateTests
{
    /// <summary>
    /// 🔴 <b>E1-1</b> — ERP 7종이 전부 ERP 로 판정되는가.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>개수를 세지 않는다.</b> 견적·수주·발주는 상신 트리거가 <b>0건</b>이라
    /// "켜도 아무 일이 안 일어나는" <b>가짜 스위치</b>다 — 트리거가 있는 4종보다
    /// 오히려 더 위험하다(고객사가 결재를 걸었다고 믿는데 아무것도 안 걸린다).
    /// 사장님: <i>"4종이던 1종이던 erp 결재 도는것 자체가 이미 문제"</i>
    /// <para>[반증] <c>ErpDocTypes</c> 에서 한 줄이라도 빼면 그 종류가 FAIL 한다.</para>
    /// </remarks>
    [Theory]
    [InlineData("quotation")]       // 트리거 0건 — 가짜 스위치
    [InlineData("sales_order")]     // 트리거 0건 — 가짜 스위치
    [InlineData("purchase_order")]  // 트리거 0건 — 가짜 스위치
    [InlineData("delivery")]        // 트리거 있음 — 결재함을 덮는 주범
    [InlineData("receipt")]         // 트리거 있음
    [InlineData("sales_return")]    // 트리거 있음
    [InlineData("purchase_return")] // 트리거 있음
    public void ERP_문서종류는_결재창구에서_빠진다(string docType)
    {
        Assert.True(ApprovalService.IsErpDocType(docType),
            $"{docType} 이 ERP 로 안 잡힌다 — 결재함에 올라온다");
    }

    /// <summary>
    /// 🔴 <b>E1-2</b> — 그룹웨어 종류는 <b>그대로 남는다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <c>expense</c>(경비)가 여기 있는 것이 중요하다. 경비는 <c>FinanceService</c> 가
    /// 트리거하지만 <b>그룹웨어</b>다 — 사장님 지시대로 영수증 첨부·지출결의서 형태로
    /// 결재에 올라간다. ERP 로 잘못 분류하면 <b>경비 결재가 통째로 죽는다</b>
    /// (8/21 에 이미 P0 였던 자리다).
    /// <para>[반증] <c>ErpDocTypes</c> 에 이 중 하나라도 넣으면 FAIL 한다.</para>
    /// </remarks>
    [Theory]
    [InlineData("expense")]         // 🔴 FinanceService 가 트리거하지만 그룹웨어다
    [InlineData("leave")]
    [InlineData("absence")]
    [InlineData("overtime")]
    [InlineData("report_daily")]
    [InlineData("report_weekly")]
    [InlineData("report_monthly")]
    [InlineData("report_incident")]
    [InlineData("resignation")]     // 작20260823작1 [D] 신규 등재
    [InlineData("labor_contract")]  // 작20260823작1 [D] 신규 등재
    public void 그룹웨어_문서종류는_결재창구에_남는다(string docType)
    {
        Assert.False(ApprovalService.IsErpDocType(docType),
            $"{docType} 이 ERP 로 잡혔다 — 그룹웨어 결재가 죽는다");
    }

    /// <summary>
    /// 🔴 <b>E1-5</b> — ERP 를 뺐어도 <b>라벨은 계속 찾아진다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이것이 "사전에서 지우지 않은" 이유다.</b>
    /// <c>DocTypeLabels</c> 는 두 가지 일을 한다 — 설정 화면 목록을 만들고,
    /// <b>결재 문서의 라벨을 찾아준다</b>. 사전에서 지우면 이미 있는 ERP 결재 문서가
    /// <c>GetValueOrDefault(docType)</c> 폴백을 타서 화면에 <b><c>delivery</c> 같은
    /// 영문 코드</b>가 그대로 뜬다. 500 이 안 나고 <b>조용히</b> 영문이 보인다
    /// (고객 노출 개발용어 금지 — 헌법 #23 계열).
    /// <para>[반증] 사전에서 ERP 항목을 지우면 라벨이 영문 코드로 떨어져 FAIL 한다.</para>
    /// </remarks>
    [Theory]
    [InlineData("delivery", "거래명세서")]
    [InlineData("receipt", "매입명세서")]
    [InlineData("quotation", "견적서")]
    [InlineData("sales_return", "매출반품")]
    public void ERP_를_뺐어도_옛문서_라벨은_한글로_뜬다(string docType, string expected)
    {
        var label = ApprovalService.GetDocTypeLabel(docType);
        Assert.Equal(expected, label);
        Assert.NotEqual(docType, label);   // 영문 코드가 그대로 뜨면 FAIL
    }

    /// <summary>
    /// 🔴 <b>E1-8</b> — 창구에 채운 2종이 <b>사전에 실제로 있는가.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>8/21 휴직 P0 의 재발 방지다.</b> <c>AbsenceService</c> 는 처음부터
    /// <c>"absence"</c> 로 상신을 불렀는데 사전에 없어서 설정 화면에 행이 안 떴고
    /// ⇒ 켤 방법이 없고 ⇒ 상신이 조용히 죽었다.
    /// <b>화면은 결재상신 버튼을 보여주는데 눌러도 아무 일이 없었다.</b>
    /// <para>
    /// ⚠️ 이 둘은 아직 상신 트리거가 없다(등재만). 그래서 <b>등재를 먼저</b> 지킨다 —
    /// 트리거를 먼저 붙이면 같은 사고가 난다.
    /// </para>
    /// <para>[반증] 사전에서 두 줄을 빼면 라벨이 영문으로 떨어져 FAIL 한다.</para>
    /// </remarks>
    [Theory]
    [InlineData("resignation", "사직서")]
    [InlineData("labor_contract", "전자근로계약서")]
    public void 창구에_채운_2종이_사전에_있다(string docType, string expected)
    {
        var label = ApprovalService.GetDocTypeLabel(docType);
        Assert.Equal(expected, label);
        Assert.NotEqual(docType, label);
    }
}