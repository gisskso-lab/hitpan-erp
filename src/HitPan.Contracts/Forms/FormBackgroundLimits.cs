namespace HitPan.Contracts.Forms;

/// <summary>
/// 🔴 양식지 그림이 <b>받아들여지는 범위</b> — 화면·서버·작성기가 **같은 자리**를 읽는다.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 Contracts 에 있나</b> — 2026-10-25 [4] 반려 R-1.
///   처음에는 이 값을 서버(`FormBackgroundPaths`)에만 두고 *"한 자리"* 라고 적었는데,
///   편집기 화면이 `HitPan.Application` 을 참조하지 않아 **razor 안에 두 번째 벌**
///   (`const long MaxBackgroundBytes` + `Accept=".png,..."`)이 생겼다.
///   그리고 게이트는 서버 두 자리만 대조했으므로 **Web 벌이 어긋나도 초록**이었다.
///   ⇒ 양쪽이 **함께 참조하는** 이 프로젝트로 값을 내렸다. 이제 벌이 하나다.
///
/// 🔴 <b>화면 검사는 편의일 뿐이다</b> — 서버가 다시 본다. 화면만 믿으면 안 된다.
/// </remarks>
public static class FormBackgroundLimits
{
    /// <summary>
    /// 받아 주는 그림 형식. 🔴 이 밖은 보관소에 들어가지도, 작성기로 가지도 않는다.
    /// </summary>
    public static readonly string[] AllowedExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    /// <summary>그림 한 장 크기 상한 — 20MB. 종이 한 장이 PC 메모리를 먹으면 안 된다.</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>
    /// 파일 고르는 창에 넣는 글자 — <c>".png,.jpg,…"</c>. 🔴 위 목록에서 **만들어 쓴다**
    /// (손으로 다시 적으면 그 순간 두 벌이 된다).
    /// </summary>
    public static string AcceptAttribute => string.Join(",", AllowedExts);
}
