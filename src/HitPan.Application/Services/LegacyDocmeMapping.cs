using System.Globalization;
using System.Text;
using HitPan.Application.DTOs.WorkReport;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>레거시 메모(POTHER.DOCME) → 일일보고서(hr_reports) 매핑 판정 — 20260909작22 (D)</b>
///
/// <para>
/// 사장님 9/8 ③: <i>"상담이력, 메모이력은 일일보고서에 작성자, 내용만 살려서 이관하고, 마이그레이션 하는 상담, 메모이력은
/// 결재 완료된 건으로 보관"</i> · <i>"표를 따로 만들면 메뉴도 생겨야 하고 구도가 바뀐다"</i> → 9/9 확인: 구분(상담 외 기타·재고변경·수금 등)도 전부 포함.
/// 그래서 새 표·메뉴 없이 기존 <c>hr_reports</c>(일일보고서) 에 <b>(사원, 날짜) 한 묶음 = 한 장</b>으로 넣는다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 순수함수로 뺐나</b> — <see cref="LegacyMdbMapping"/>(작21 A0) 과 같은 이유다. 날짜 유효성·줄 모양·묶음 키·제목 규칙이
/// 서비스 안에 묻히면 게이트가 값을 넣어 볼 수 없고 글자검사만 남는다. 이 클래스는 <b>DB 를 읽지도 쓰지도 않는다.</b>
/// 게이트 <c>MdbMigrationV2DocmeGateTests</c>(G10) 가 이 함수들을 실제로 부른다.
/// </para>
///
/// <para>
/// 실측 근거(선행검증서 §2-3, 2026-09-09): DOCME 242,106행 · 구분 13종 · (사원,날짜) 묶음 9,170(연도 <c>0000</c> 3행 제외 9,167) ·
/// 사원 빈값 135행 · 시각 6자 아님 302행 · 공지(<c>ME_NOTICE≠0</c>) 346행 · 묶음 최대 94행 · 설명 5칸 합 최대 84자.
/// </para>
/// </summary>
public static class LegacyDocmeMapping
{
    /// <summary>시각을 읽을 수 없을 때 줄 머리에 두는 자리표시 — 6자 숫자(HHMMSS)가 아니면 이 값이다(실측 302행).</summary>
    public const string UnknownTime = "--:--";

    /// <summary><c>source_id</c> 접두 — <c>docme-{yyyyMMdd}-{sha256(사원명) 앞 8자}</c>.</summary>
    public const string SourceIdPrefix = "docme-";

    /// <summary>
    /// <c>ME_DATE</c>(Text 8) → 날짜. <b>8자리 숫자 · 연도 ≠ <c>0000</c> · 실제 존재하는 날짜</b>여야 참이다.
    /// 연도 <c>0000</c> 3행(실측 <c>00000000</c>·<c>00000109</c>·<c>00000929</c>)은 날짜가 없는 메모라 이관하지 않고 카운트만 한다.
    /// </summary>
    public static bool TryParseDate(string? meDate, out DateTime date)
    {
        date = default;
        var s = (meDate ?? string.Empty).Trim();
        if (s.Length != 8) return false;
        foreach (var ch in s)
        {
            if (ch < '0' || ch > '9') return false;
        }
        if (s.StartsWith("0000", StringComparison.Ordinal)) return false;
        return DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary><see cref="TryParseDate"/> 의 참/거짓만 — 게이트 G10-1 이 값으로 부른다.</summary>
    public static bool IsValidDate(string? meDate) => TryParseDate(meDate, out _);

    /// <summary>
    /// 묶음 키 = (<c>TRIM(ME_SAWON)</c>, <c>ME_DATE</c>). 사원 빈값(NULL·공백)은 <c>""</c> 로 모아 한 묶음이 된다.
    /// Access 쪽 <c>DISTINCT TRIM(ME_SAWON), ME_DATE</c> 와 같은 셈이라 실측 9,167 과 맞아야 한다.
    /// </summary>
    public static (string Sawon, string Date) ReportKey(string? sawon, string meDate)
        => ((sawon ?? string.Empty).Trim(), (meDate ?? string.Empty).Trim());

    /// <summary>
    /// 멱등 키 <c>source_id</c> = <c>docme-{yyyyMMdd}-{sha256Hex(TRIM(사원명)) 앞 8자}</c> (≤ 80자 · <c>hr_reports.source_id varchar(80)</c>).
    /// 사원명 앞뒤 공백 차이는 같은 값이다 — 묶음 키와 같은 TRIM 을 쓴다.
    /// 해시 함수는 호출자가 넘긴다(서비스의 <c>ComputeSourceHash</c> 를 그대로 쓰기 위해 — 이 클래스는 암호 라이브러리를 들지 않는다).
    /// </summary>
    public static string SourceId(string meDate, string sawon, Func<string, string> sha256Hex)
    {
        ArgumentNullException.ThrowIfNull(sha256Hex);
        var date = (meDate ?? string.Empty).Trim();
        var name = (sawon ?? string.Empty).Trim();
        var hex = sha256Hex(name) ?? string.Empty;
        var head = hex.Length > 8 ? hex[..8] : hex;
        var id = SourceIdPrefix + date + "-" + head;
        return id.Length > 80 ? id[..80] : id;
    }

    /// <summary>
    /// <c>ME_TIME</c>(Text 6, 실측 전부 <c>HHMMSS</c> 숫자) → <c>HH:MM</c>. 6자 숫자가 아니면(<c>""</c>·<c>"0000"</c>·한 칸 공백 등 302행) <see cref="UnknownTime"/>.
    /// </summary>
    public static string FormatTime(string? meTime)
    {
        var t = (meTime ?? string.Empty).Trim();
        if (t.Length != 6) return UnknownTime;
        foreach (var ch in t)
        {
            if (ch < '0' || ch > '9') return UnknownTime;
        }
        return string.Concat(t.AsSpan(0, 2), ":", t.AsSpan(2, 2));
    }

    /// <summary>
    /// 본문 한 줄 = <c>HH:MM [구분] DESC1 DESC2 DESC3 DESC4 DESC5</c>.
    /// <list type="bullet">
    ///   <item>빈 칸은 건너뛰고 칸 사이는 공백 하나(레거시 Text 40 은 뒤가 공백으로 채워져 있다 — 실측 102행 — TRIM 한다)</item>
    ///   <item>구분이 빈값이면 <c>[…]</c> 자체를 생략한다(<c>[기타]</c> 로 지어내지 않는다 — 원문에 없던 말을 넣지 않는다)</item>
    ///   <item>시각을 못 읽으면 <see cref="UnknownTime"/> 으로 시작한다</item>
    ///   <item><c>ME_NOTICE≠0</c>(공지, 실측 346행)이면 줄 끝에 <c> (공지)</c></item>
    /// </list>
    /// </summary>
    public static string Line(string? meTime, string? gubun, string? d1, string? d2, string? d3, string? d4, string? d5, int notice)
    {
        var sb = new StringBuilder(128);
        sb.Append(FormatTime(meTime));

        var g = (gubun ?? string.Empty).Trim();
        if (g.Length > 0)
        {
            sb.Append(" [").Append(g).Append(']');
        }

        AppendPart(sb, d1);
        AppendPart(sb, d2);
        AppendPart(sb, d3);
        AppendPart(sb, d4);
        AppendPart(sb, d5);

        if (notice != 0)
        {
            sb.Append(" (공지)");
        }
        return sb.ToString();
    }

    private static void AppendPart(StringBuilder sb, string? part)
    {
        var t = (part ?? string.Empty).Trim();
        if (t.Length == 0) return;
        sb.Append(' ').Append(t);
    }

    /// <summary>
    /// 사원을 못 찾아 <c>LEGACY_FALLBACK</c> 사원 앞으로 넣을 때 본문 <b>첫 줄</b> — <c>원작성자: {ME_SAWON}</c>. 빈값이면 <c>원작성자: (없음)</c>.
    /// 사장님 ③ "작성자, 내용만 살려서" — 사원 마스터에 없는 이름(전화번호·고객명이 사원 칸에 적힌 358행)이라도 작성자 글자는 잃지 않는다.
    /// </summary>
    public static string OriginalAuthorLine(string? sawon)
    {
        var name = (sawon ?? string.Empty).Trim();
        return name.Length == 0 ? "원작성자: (없음)" : "원작성자: " + name;
    }

    /// <summary>
    /// 제목 — <c>WorkReportService.BuildTitle</c> 의 제목 없는 경우 규칙과 <b>같은 모양</b>:
    /// <c>{종류표시명} {yyyy-MM-dd}</c>, 사원명이 있으면 뒤에 <c> {사원명}</c> · 200자 절단(<c>hr_reports.title varchar(200)</c>).
    /// 종류표시명은 <see cref="WorkReportTypes.DisplayName"/> 에서 읽는다 — 화면이 부르는 이름과 어긋나지 않게(복붙하지 않는다).
    /// 게이트 G10-4 가 <c>BuildTitle</c> 을 리플렉션으로 실제 호출해 두 값을 대조한다.
    /// </summary>
    public static string Title(DateTime date, string? employeeName)
    {
        var label = WorkReportTypes.DisplayName(WorkReportTypes.Daily);
        var period = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var title = string.IsNullOrWhiteSpace(employeeName)
            ? label + " " + period
            : label + " " + period + " " + employeeName;
        return title.Length > 200 ? title[..200] : title;
    }
}
