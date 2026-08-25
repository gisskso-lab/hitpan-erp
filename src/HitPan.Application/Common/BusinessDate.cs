namespace HitPan.Application.Common;

/// <summary>
/// 전표 일자의 단일 진실원 (20260825작18).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 만들었나</b> — 매입 전표들이 <c>DateTime.UtcNow.Date</c> 로 일자를 잡고 있었다.
/// 한국은 UTC+9 라, <b>한국시각 오전 9시 이전에 만든 전표는 전부 "어제" 날짜로 기록</b>된다.
/// 일자별 집계·월말 마감·문서번호 채번(<c>매반-yyyyMMdd</c>)이 하루씩 어긋난다.
///
/// <para>
/// ⚠️ <b>한 자리씩 고치면 안 된다.</b> 같은 화면에서 신규작성은 UTC, 전환은 KST 가 되면
/// 채번 prefix 가 경로마다 갈려 <c>COUNT(*)+1</c> 이 같은 번호를 재발급할 수 있다.
/// 그래서 매입 경로 전체가 이 한 곳을 본다. <c>AddHours(9)</c> 를 여기저기 흩뿌리면
/// 다음 사고가 예약된다.
/// </para>
///
/// <para>
/// 🚫 <b>과거 데이터는 소급 보정하지 않는다</b> — 원장 무결성(헌법 #3). 이미 UTC 로 적힌
/// 행은 그대로 두고, 앞으로 만들어지는 것만 바로잡는다.
/// </para>
///
/// <para>
/// 📌 현재는 한국 고정이다. 해외 테넌트가 생기면 테넌트 설정에서 표준시를 읽도록
/// 이 클래스만 바꾸면 된다 — 호출부는 손대지 않는다.
/// </para>
/// </remarks>
public static class BusinessDate
{
    /// <summary>한국 표준시 기준 오프셋 (UTC+9). 한국은 서머타임이 없어 고정값이 정확하다.</summary>
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);

    /// <summary>업무 기준 '오늘' — 전표 일자·문서번호 채번에 쓴다.</summary>
    public static DateTime Today => (DateTime.UtcNow + KoreaOffset).Date;

    /// <summary>업무 기준 현재 시각.</summary>
    public static DateTime Now => DateTime.UtcNow + KoreaOffset;
}
