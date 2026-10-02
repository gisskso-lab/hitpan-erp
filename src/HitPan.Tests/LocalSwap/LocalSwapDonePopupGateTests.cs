using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N4 — 「끝났습니다」 팝업 한 번 순수 판정 게이트 G-ND1
/// (설계 §19-1 조각 C · §19-2 · 작업지시서 §18 · 18-5 X-3 보정 · X-4).
/// </summary>
/// <remarks>
/// <para>이 파일 = <b>계약 이름 확인</b> — <c>LocalSwapUiText.ShouldShowDonePopup</c>(설계 19-1 조각 C 「순수 판정 덧붙임」).
/// 갈래 N3 가 올리기 전에는 빨강이다(작업지시서 18-1). 인자 모양은 설계에 없다 ⇒ N3 의 실제 시그니처를 보고
/// 표 시험(success·24h 안·안 보임·#43 차례 아님 → true / #43 차례 · 이미 보임 · 저장 실패 · refused·reverted·broken → false
/// · 대조 = #43 조건 뺀 판정)을 같은 이름의 partial 파일(<c>LocalSwapDonePopupGateTests.Behavior.cs</c>)로 붙인다(개발명세서 N4 §3).</para>
/// </remarks>
public sealed partial class LocalSwapDonePopupGateTests
{
    [Fact(DisplayName = "N4 G-ND1 계약 — LocalSwapUiText.ShouldShowDonePopup 이 public static bool 이다")]
    public void Contract_should_show_done_popup()
    {
        var ms = typeof(LocalSwapUiText).GetMethods().Where(m => m.Name == "ShouldShowDonePopup").ToArray();
        Assert.True(ms.Length == 1, "ShouldShowDonePopup 이 " + ms.Length + "개 — 갈래 N3 대기(하나여야 한다)");
        Assert.True(ms[0].IsStatic && ms[0].ReturnType == typeof(bool), "public static bool 이어야 한다(순수 판정)");
    }
}
