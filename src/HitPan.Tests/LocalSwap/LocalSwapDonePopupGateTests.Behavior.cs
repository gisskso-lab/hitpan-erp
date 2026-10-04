using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// G-ND1 표 시험(N3 <c>ShouldShowDonePopup</c> 실제 인자 · 설계 §19-2 · 개발명세서 N3 §1-1) — 순수 함수라 화면·저장소·DB 0.
/// </summary>
/// <remarks>
/// 대조(개발명세서 N4 「N4b」 절): 제품 첫 줄 <c>if (updatePromptFirst) return false;</c> 를 Edit 도구로 뺀 사본에서
/// 「#43 차례」 줄이 true 가 되어 FAIL 하는지 돌려 봤고, 원복 뒤 제품 diff 0.
/// </remarks>
public sealed partial class LocalSwapDonePopupGateTests
{
    private static readonly DateTime At = new(2026, 10, 2, 5, 0, 0, DateTimeKind.Utc);
    private const string From = "1.3.49";
    private const string To = "1.3.50";

    private static string Seen(string mode) => LocalSwapUiText.DonePopupStamp(mode, To, At);

    /// <summary>
    /// 줄 = (이름, mode, state, 지금 판, 지금 시각까지 흐른 시간(분), 저장소 읽힘, 봤음 표, #43 차례, 기대값).
    /// true 4줄(success · 24h 안 · 안 보임 · #43 차례 아님) / false = #43 차례 · 이미 보임 · 저장 실패 · refused·reverted·broken · 24h 밖 · 판 다름.
    /// </summary>
    public static TheoryData<string, string, string, string, int, bool, string?, bool, bool> Table() => new()
    {
        { "되돌리기 성공 · 막 끝남 · 안 보임 · #43 아님 → 뜬다", "rollback", "success", To, 1, true, null, false, true },
        { "수동 업데이트 성공 · 23시간 · 다른 결과를 봤음 → 뜬다", "update", "success", To, 23 * 60, true, "rollback|1.3.48|2026-09-30T00:00:00.0000000Z", false, true },
        { "대소문자·공백 섞인 상태 글자 → 뜬다", "rollback", " Success ", To, 5, true, null, false, true },
        { "봤음 표가 다른 모드 → 뜬다", "update", "success", To, 5, true, Seen("rollback"), false, true },
        { "#43 안내 차례 → 안 뜬다", "rollback", "success", To, 1, true, null, true, false },
        { "#43 차례 + 다른 조건 전부 참 → 안 뜬다(수동 업데이트)", "update", "success", To, 1, true, null, true, false },
        { "이미 보임(같은 표) → 안 뜬다", "rollback", "success", To, 1, true, Seen("rollback"), false, false },
        { "저장소 읽기 실패 → 안 뜬다", "rollback", "success", To, 1, false, null, false, false },
        { "refused → 안 뜬다", "rollback", "refused", From, 1, true, null, false, false },
        { "reverted → 안 뜬다", "rollback", "reverted", From, 1, true, null, false, false },
        { "broken → 안 뜬다", "rollback", "broken", From, 1, true, null, false, false },
        { "24시간 넘음 → 안 뜬다", "rollback", "success", To, 24 * 60 + 1, true, null, false, false },
        { "지금 판 ≠ to → 안 뜬다", "rollback", "success", From, 1, true, null, false, false },
    };

    [Theory(DisplayName = "N4 G-ND1 🚨 완료 팝업 판정 표 — success·24h 안·안 보임·#43 차례 아님만 true")]
    [MemberData(nameof(Table))]
    public void Nd1_table(string name, string mode, string state, string current, int minutes, bool storageOk, string? seen, bool updatePromptFirst, bool expected)
    {
        var now = At.AddMinutes(minutes);
        var got = LocalSwapUiText.ShouldShowDonePopup(mode, state, From, To, At, current, now, storageOk, seen, updatePromptFirst);
        Assert.True(got == expected, name + " — 기대 " + expected + " · 실제 " + got);
    }
}
