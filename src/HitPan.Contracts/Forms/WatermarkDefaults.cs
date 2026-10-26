namespace HitPan.Contracts.Forms;

/// <summary>
/// 🔴 워터마크 <b>제안값</b>과 <b>받아들이는 범위</b> — 화면·서버·작성기가 <b>같은 자리</b>를 읽는다.
/// </summary>
/// <remarks>
/// 🔴 <b>「제안값」이라고 적는 까닭</b> — 사장님 결재 2026-10-26(결-J):
///   *"응 당연히 미리보기에서 미리 보기반영되도록"*.
///   이 숫자는 <b>개발팀이 정한 값이 아니다.</b> 고객이 화면에서 바꾸고, 바꾸는 즉시
///   미리보기로 확인한다. 여기 있는 값은 <b>처음 보여 주는 자리</b>일 뿐이다
///   (100% 자동화 없음 — 제안값 → 수정 → 확정).
///
/// 🔴 <b>숫자의 근거는 실측이다</b> — 2026-10-26, QuestPDF 2026.2.4 로 PDF 를 뽑아
///   PNG 로 래스터한 뒤 <b>픽셀값을 읽어</b> 정했다:
///   명도 0.12 에서 순수 빨강이 <c>R=255 G=223 B=223</c> 로 떨어졌고(글자가 읽힌다),
///   채도 0.15 를 함께 걸면 <c>R=242 G=237 B=237</c> 로 거의 회색이 되어 본문과 안 다툰다.
///
/// 🔴 <b>DDL 의 DEFAULT 와 같은 값이어야 한다</b> — <c>print_common_settings</c>(DB-148).
///   두 자리가 어긋나면 「공통 설정 행이 없는 고객」과 「있는 고객」의 종이가 달라진다.
///   ⇒ 그 일치를 <b>게이트가 잰다</b>(G-W14). 사람의 기억에 맡기지 않는다.
/// </remarks>
public static class WatermarkDefaults
{
    /// <summary>명도 제안값 — 0.12. 🔴 <c>print_common_settings.watermark_opacity</c> DEFAULT 와 같다.</summary>
    public const decimal Opacity = 0.12m;

    /// <summary>채도 제안값 — 0.15 (0=흑백). 🔴 DDL DEFAULT 와 같다.</summary>
    public const decimal Saturate = 0.15m;

    /// <summary>크기 제안값 — 용지 폭의 60%. 🔴 DDL DEFAULT 와 같다. 사장님 오더 <i>"정중앙 크게"</i>.</summary>
    public const int ScalePct = 60;

    /// <summary>명도·채도 하한 — 0.00 (완전히 투명 / 완전히 흑백).</summary>
    public const decimal MinRatio = 0.00m;

    /// <summary>명도·채도 상한 — 1.00.</summary>
    public const decimal MaxRatio = 1.00m;

    /// <summary>크기 하한 — 10%. 이보다 작으면 보이지 않는다.</summary>
    public const int MinScalePct = 10;

    /// <summary>크기 상한 — 100%. 🔴 넘으면 종이를 벗어난다.</summary>
    public const int MaxScalePct = 100;

    /// <summary>
    /// 🔴 받은 값을 <b>범위 안으로 조인다</b> — 거절하지 않고 조인다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 거절하지 않나</b> — 이 값이 쓰이는 자리는 <b>인쇄 한복판</b>이다.
    ///   범위 밖이라고 종이를 안 내보내면 전표가 멈춘다(#20). 화면·API 가 먼저 막고,
    ///   여기까지 온 값은 <b>조여서 그린다</b>. 이미 범위 밖으로 저장해 둔 고객의 값도
    ///   지우지 않는다 — 그릴 때 조인다(10/21 여백 상한과 같은 방식).
    /// </remarks>
    public static decimal ClampRatio(decimal value) =>
        value < MinRatio ? MinRatio : value > MaxRatio ? MaxRatio : value;

    /// <summary>크기를 범위 안으로 조인다 — 같은 까닭으로 거절하지 않는다.</summary>
    public static int ClampScalePct(int value) =>
        value < MinScalePct ? MinScalePct : value > MaxScalePct ? MaxScalePct : value;
}
