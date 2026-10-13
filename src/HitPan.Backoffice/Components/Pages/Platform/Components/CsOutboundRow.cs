namespace HitPan.Backoffice.Components.Pages.Platform.Components;

/// <summary>
/// 🔴 20261013작1 갈래③ — 「히트판공지 전송」 화면이 읽는 <b>한 줄</b>의 모양.
///
/// <para><b>왜 따로 파일로 두나</b>: ⓐ 목록 화면·상세 패널·게이트 <b>셋이 같은 모양</b>을 봐야 한다.
/// 화면 안에 사사로이 선언해 두면 게이트는 베낀 모양을 보게 되고, 베낀 모양은 어긋난다 —
/// 2026-10-13 사고가 그 모양이었다(게이트는 사전으로 읽어 통과하고 화면은 터졌다).
/// ⓑ 이 파일은 Blazor 에 기대지 않는 순수 C# 이라 시험 프로젝트가 <b>소스로 링크</b>해
/// <b>화면이 쓰는 그 형</b>으로 역직렬화할 수 있다(레포 관례 — <c>ApiErrorText.cs</c> 선례).</para>
///
/// <para>🔴 <b>널 허용성이 이 파일의 본체다.</b> 서버 목록 질의는 <c>tenants</c> 를 <c>LEFT JOIN</c> 하고
/// (<c>CsAdminController.ListOutbound</c>), 폐기된 줄은 <c>body</c> 를 <b>NULL 로 비운다</b>
/// (<c>PurgeOutbound</c> — 본문만 지우고 사실은 남긴다). 제목은 처음부터 선택이다.
/// ⇒ <c>CompanyName</c>·<c>TenantCode</c>·<c>Title</c>·<c>Body</c> 는 <b>반드시 널 허용</b>이어야 한다.
/// 하나라도 비널러블로 적으면 JSON <c>null</c> 에서 역직렬화가 터지고, <b>한 건이 목록 전부를 가린다</b>
/// (2026-10-12 <c>attempt_count</c> 사고와 같은 모양). 게이트 G-W7-6 이 그 자리를 문다.</para>
///
/// <para>🔴 <c>PurgedAt</c>·<c>PurgeReason</c> 2칸은 <b>더하기</b>다(#1). 서버는 이 둘을 이미 돌려주는데
/// (<c>CsAdminController.cs</c> 목록 질의 <c>purgedAt</c>·<c>purgeReason</c>) 화면 모델에 칸이 없어
/// 「폐기됨」이 화면에 <b>한 번도 안 보였다</b>(PM 실측 2026-10-13).</para>
/// </summary>
public sealed class CsOutboundRow
{
    /// <summary>발신 한 건의 열쇠. 취소·폐기 때 이 값으로 부른다.</summary>
    public string OutboundId { get; set; } = "";

    /// <summary>받는 고객사 이름. 🔴 <b>널 허용</b> — 고객사 줄이 지워진 옛 발신은 LEFT JOIN 에서 NULL 이 온다.</summary>
    public string? CompanyName { get; set; }

    /// <summary>고객사 코드. 🔴 위와 같은 이유로 널 허용.</summary>
    public string? TenantCode { get; set; }

    /// <summary>발신 종류(<c>update</c>·<c>notice</c>·<c>promo</c>·<c>etc</c> + 옛 값 2종). 낱말은 <c>CsVocab.NoticeKindLabel</c>.</summary>
    public string? Purpose { get; set; }

    /// <summary>제목 — 처음부터 선택 칸이다(서버가 100자까지만 받는다).</summary>
    public string? Title { get; set; }

    /// <summary>본문. 🔴 <b>폐기된 줄은 NULL</b> 이다 — 본문만 비우고 지웠다는 사실은 남기기 때문이다.</summary>
    public string? Body { get; set; }

    /// <summary>쓴 때(UTC). 서버가 <c>UTC_TIMESTAMP(6)</c> 로 명시해 넣는다.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>고객사에 나간 때. NULL = 아직 전달 대기함에 있다.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>나가기 전에 사람이 취소한 때.</summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>🔴 폐기한 때 — 이 칸이 없어서 「폐기됨」이 안 보였다.</summary>
    public DateTime? PurgedAt { get; set; }

    /// <summary>🔴 폐기 사유(사람이 적는다 · 1~200자 필수). 감사기록에도 같이 남는다.</summary>
    public string? PurgeReason { get; set; }

    /// <summary>
    /// 이 줄의 상태 낱말 — <b>넷 고정</b>: 전달 대기 · 전달됨 · 취소됨 · 폐기됨.
    /// <para>🔴 판정 순서가 뜻이다. 폐기가 가장 세다(폐기된 줄은 전달·취소 여부와 무관하게 「폐기됨」이다 —
    /// 본문이 이미 비었으므로 그 줄을 「전달 대기」라고 적으면 거짓이 된다). 그 다음이 취소,
    /// 그 다음이 전달, 아무것도 아니면 아직 전달 대기함에 있다.</para>
    /// <para>이 판정을 화면 마크업에 적지 않고 여기에 둔 이유: 목록 칼럼·상세 패널·게이트가
    /// <b>같은 판정</b>을 봐야 하기 때문이다. 세 곳에 세 번 적으면 어긋난다.</para>
    /// </summary>
    public string StatusLabel =>
        PurgedAt is not null ? "폐기됨"
        : CanceledAt is not null ? "취소됨"
        : DeliveredAt is not null ? "전달됨"
        : "전달 대기";

    /// <summary>아직 전달 대기함에 있나 — 「보내기 취소」가 듣는 줄만 true(서버도 같은 조건으로 막는다).</summary>
    public bool IsWaiting => PurgedAt is null && CanceledAt is null && DeliveredAt is null;

    /// <summary>목록 칼럼에 쓰는 <b>한 줄</b> 요약. 🔴 전문은 오른쪽 상세에서 읽는다(행 높이를 가지런히 둔다).</summary>
    public string OneLine
    {
        get
        {
            if (PurgedAt is not null) return string.IsNullOrWhiteSpace(Title) ? "(폐기된 공지)" : Title!;
            var head = string.IsNullOrWhiteSpace(Title) ? "" : Title!.Trim();
            var tail = (Body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            var joined = head.Length == 0 ? tail : (tail.Length == 0 ? head : $"{head} — {tail}");
            return joined.Length == 0 ? "-" : joined;
        }
    }
}
