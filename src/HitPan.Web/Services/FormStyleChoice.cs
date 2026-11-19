namespace HitPan.Web.Services;

// 🔴 [2026-11-20 W-G1-51 갈래 E⑷ 「스타일 고르기」 · 작업지시서 §2 · §3 · §5] 양식 편집창 「인쇄 모양」 고르기의 판정 · 글자.
//   Blazor 비의존 순수 C# — 시험이 이 파일을 링크해 **실제로 불러** 잰다(RollbackWatch.cs 선례).
//   편집창(FormTemplateEditorDialog.razor)은 그리기만 하고, 쓰이나/안 쓰이나 · 불러온 값 고르기는 전부 여기서 정한다.

/// <summary>
/// 🔴 양식 편집창 「인쇄 모양」(기본 · 격식 · 간결 · 큰글씨) 고르기의 판정과 화면 글자.
/// </summary>
/// <remarks>
/// 🔴 조건은 서버 인쇄의 스타일 판정(<c>PdfRenderService.ResolvePrintStyle</c>)과 <b>같아야</b> 한다 —
///   다르면 고객이 거짓 안내를 받는다(편집창 <c>PaperChoiceIgnored</c> 와 같은 원칙). 게이트가 서버 판정을 반사로 불러 대조한다.
/// 🔴 법정 서식 목록은 <b>받기만</b> 한다 — 이 파일에 종 이름을 적지 않는다(목록 두 벌 금지 · 작업지시서 §10).
/// 🔴 화면 글자는 작업지시서 §2 · §3 글자 그대로다(처음 보는 직원 기준 · 영문 키는 화면에 안 보인다).
/// </remarks>
public static class FormStyleChoice
{
    /// <summary>모르는 값 · 빈 값이 떨어지는 자리 — 종이에도 이 모양으로 나간다.</summary>
    public const string DefaultKey = "basic";

    /// <summary>고르기 칸 제목.</summary>
    public const string Label = "인쇄 모양";

    /// <summary>쓰일 때 칸 밑 도움말.</summary>
    public const string HelperText = "고르면 아래 미리보기가 바로 바뀝니다.";

    /// <summary>안 쓰이는 까닭 ① — 법정 서식.</summary>
    public const string WhyLegal = "법으로 모양이 정해진 서식이라 인쇄 모양을 바꿀 수 없습니다.";

    /// <summary>안 쓰이는 까닭 ② — 미리 인쇄된 양식용지.</summary>
    public const string WhyPreprint = "미리 인쇄된 양식용지에는 인쇄 모양이 쓰이지 않습니다. 내용만 채워 인쇄합니다.";

    /// <summary>안 쓰이는 까닭 ③ — 양식 그림을 깐 양식.</summary>
    public const string WhyPicture = "양식 그림을 깐 양식에는 인쇄 모양이 쓰이지 않습니다. 올린 그림 모양대로 인쇄합니다.";

    /// <summary>
    /// 고르기 항목 — (저장 값, 화면 글자). 차례 = 화면 차례.
    /// 🔴 저장 값 넷 = 서버 허용 목록(<c>KnownStyleKeys</c>)과 같은 집합 — 게이트가 반사로 대조한다.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Options = new[]
    {
        new KeyValuePair<string, string>("basic", "기본 — 지금 쓰는 모양"),
        new KeyValuePair<string, string>("formal", "격식 — 관공서 · 처음 거래처에 낼 때"),
        new KeyValuePair<string, string>("simple", "간결 — 팩스 · 잉크를 아낄 때"),
        new KeyValuePair<string, string>("large", "큰글씨 — 창고 · 현장에서 멀리서 볼 때"),
    };

    /// <summary>
    /// 🔴 불러온 저장 값을 고르기 항목 값으로 고른다 — 앞뒤 공백 자름 · 대소문자 무시 · 빈 값 · null · 모르는 값 = <c>basic</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 [작업지시서 §5 · PM 결재 E4-4] 모르는 값은 지금도 종이에 basic 으로 나간다 → 화면에 「기본」 으로 보이는 것이 사실과 같다.
    ///   그대로 보내면 저장 문 허용 목록(E⑵ 고침 1 · 글자 그대로 비교)에 걸려 <b>양식 저장 자체가 막힌다</b>(결재 기록 §75 ②).
    ///   알림 0 — 종이가 안 바뀌어 고객이 알 거리가 없다.
    /// </remarks>
    public static string Normalize(string? raw)
    {
        var key = (raw ?? string.Empty).Trim();
        foreach (var option in Options)
        {
            if (string.Equals(key, option.Key, StringComparison.OrdinalIgnoreCase)) return option.Key;
        }
        return DefaultKey;
    }

    /// <summary>
    /// 🔴 이 양식에 인쇄 모양이 <b>안 쓰이면</b> 그 까닭 한 줄 · 쓰이면 <c>null</c>.
    /// </summary>
    /// <param name="formType">양식 유형(편집창 값).</param>
    /// <param name="paperMode">종이(<c>plain</c> = 빈 A4 · <c>preprint</c> = 양식용지).</param>
    /// <param name="backgroundImageUrl">양식 그림 경로.</param>
    /// <param name="legalFormTypes">법정 서식 목록 — 편집창이 가진 목록을 그대로 넘긴다(두 벌 금지).</param>
    /// <remarks>
    /// 🔴 위에서부터 먼저 걸리는 줄 하나만 돌려준다(작업지시서 §3 차례). 서버 짝:
    ///   ① 법정 서식 = <c>LegalFormDocTypes</c> 막음(E⑵ 고침 1 · E-1) ② <c>paper_mode</c> ≠ <c>plain</c>(글자 그대로)
    ///   ③ 양식 그림 경로가 공백 아님. 셋 다 아니면 서버가 스타일을 그린다.
    /// </remarks>
    public static string? WhyNotUsed(
        string? formType, string? paperMode, string? backgroundImageUrl, IEnumerable<string> legalFormTypes)
    {
        if (legalFormTypes.Contains(formType ?? string.Empty, StringComparer.Ordinal)) return WhyLegal;
        if (!string.Equals(paperMode, "plain", StringComparison.Ordinal)) return WhyPreprint;
        if (!string.IsNullOrWhiteSpace(backgroundImageUrl)) return WhyPicture;
        return null;
    }
}
