namespace HitPan.Contracts.Forms;

/// <summary>
/// 🔴 회사 그림(로고 · 직인 · 출력 머리)이 <b>받아들여지는 범위</b> — 화면·서버·작성기가 <b>같은 자리</b>를 읽는다.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 양식지(<see cref="FormBackgroundLimits"/>)와 따로 두나</b> — 상한이 달라야 한다.
///   양식지 그림은 종이 한 장에 <b>한 번</b> 깔리고 끝이다. 로고는 <b>워터마크의 재료</b>라
///   그릴 때마다 <c>base64</c> 글자로 바뀌어 SVG 안에 들어간다 — 바이트가 <b>1.33배</b>로 늘고,
///   그 일이 <b>모든 양식 · 모든 장</b>에서 일어난다.
///   🔴 20MB 로고를 그대로 허용하면 전표 한 장 뽑을 때마다 27MB 짜리 문자열이 생긴다.
///   고객 PC 는 사무실 PC 다(#30 — 본사 의존 0). 그래서 <b>2MB</b> 로 조인다.
///
/// 🔴 <b>형식 목록은 양식지와 같은 값을 쓴다</b> — 그림은 그림이다. 두 벌로 적어 두면
///   한쪽만 <c>.webp</c> 를 받는 날 고객은 「올라갔는데 안 나온다」를 본다
///   (2026-10-25 [4] 반려 R-1 이 그 사고였다).
///
/// 🔴 <b>화면 검사는 편의일 뿐이다</b> — 서버가 다시 본다.
/// </remarks>
public static class CompanyImageLimits
{
    /// <summary>받아 주는 그림 형식 — 🔴 양식지와 <b>같은 값</b>을 쓴다(두 벌을 안 만든다).</summary>
    public static string[] AllowedExts => FormBackgroundLimits.AllowedExts;

    /// <summary>
    /// 그림 한 장 크기 상한 — <b>2MB</b>.
    /// 🔴 양식지(20MB)보다 작은 까닭은 클래스 설명에 적었다(워터마크가 base64 로 늘린다).
    /// </summary>
    public const long MaxBytes = 2L * 1024 * 1024;

    /// <summary>파일 고르는 창에 넣는 글자 — 위 목록에서 <b>만들어 쓴다</b>.</summary>
    public static string AcceptAttribute => string.Join(",", AllowedExts);

    /// <summary>고객에게 보여 주는 상한 안내 — 🔴 숫자를 손으로 다시 적지 않는다.</summary>
    public static string MaxBytesLabel => $"{MaxBytes / (1024 * 1024)}MB";
}
