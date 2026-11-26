namespace HitPan.Application.Services;

/// <summary>
/// 🔴 양식의 <b>저장된 칸 좌표가 0개</b>인가 — W-G1-51 갈래 §59 「고침 1」 P2-1 · CTO §59 판정 ② 「저장된 칸 0」.
/// </summary>
/// <remarks>
/// 🔴 <b>판정 유일 자리</b> — 업데이트 옮기기(<c>FormTemplateService.CreateYTemplateCoreAsync</c> 기본 넘김) ·
///   렌더 K3 판정(<c>PdfRenderService.IsNoCoordsImageOnly</c>) · 인쇄 전 안내 문(<c>IsNoCoordsImageOnlyAsync</c> → 같은 판정)
///   셋이 이 함수만 부른다. SQL 로 따로 적지 않는다(CTO ② (d)).
/// 🔴 「칸 0」 = 저장값이 비었거나 빈 목록일 때만:
///   <c>null</c> · 빈 글자 · JSON 공백만 · <c>[]</c> · <c>[</c> 와 <c>]</c> 사이에 JSON 공백만.
///   그 밖(JSON 글자 <c>null</c> · 목록 아님 · 칸 모양이 틀림 · 종이 밖 칸 · 깨진 JSON)은 모두 「저장값 있음」 = 거짓
///   — 그 양식은 종전 기본 배치 + 데이터로 찍힌다(데이터 지킴 #20 · 안내 거짓 0 #25).
/// 🔴 옛 뜻(읽어 낸 칸 목록 수 0)과 다르다 — 모두 무효 칸인 양식은 읽어 낸 목록이 0개여도 여기서는 거짓이다.
/// 🔴 글자만 본다 — 예외 0 · 로그 0 · catch 0(#15 걸릴 자리 없음) · DB · 파일 접근 0.
/// 🔴 JSON 공백 = 스페이스 · 탭 · CR · LF 네 가지(RFC 8259). 그 밖의 유니코드 공백은 「저장값 있음」 으로 본다.
/// </remarks>
public static class FormCoordsStored
{
    private static readonly char[] JsonWhitespace = { ' ', '\t', '\r', '\n' };

    public static bool IsEmpty(string? fieldCoordsJson)
    {
        if (fieldCoordsJson is null) return true;
        var s = fieldCoordsJson.AsSpan().Trim(JsonWhitespace);
        if (s.IsEmpty) return true;
        if (s.Length < 2 || s[0] != '[' || s[^1] != ']') return false;
        return s[1..^1].Trim(JsonWhitespace).IsEmpty;
    }
}
