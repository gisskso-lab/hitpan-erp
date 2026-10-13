namespace HitPan.Backoffice.Components.Pages.Platform.Components;

/// <summary>
/// 🔴 20261013작1 갈래① — 고객지원센터 화면들이 쓰는 <b>낱말 한 벌</b>.
///
/// <para><b>왜 한 벌로 모으나</b>: 분류 8종이 <c>AdminCsListPage.razor</c> 와 <c>AdminKbPage.razor</c>
/// <b>두 곳에 복제</b>돼 있다(설계 §7-9). 한쪽만 고치면 같은 값이 두 이름으로 보인다 —
/// 처음 보는 사람이 「다른 것」으로 읽는 순간 화면을 믿지 않는다. 값↔라벨의 단일 출처를 여기에 둔다.</para>
///
/// <para>🔴 <b>기본가지는 「받은 값 그대로」</b>다(<c>_ =&gt; c ?? "-"</c>). <c>_ =&gt; "기타"</c> 로 떨어뜨리면
/// 모르는 값이 <b>아는 값으로 꾸며진다</b> — 서버가 새 값을 하나 더해도 화면은 「기타」라고 거짓말을 한다.
/// 정본은 <c>AdminCsListPage.razor:1137-1143</c> <c>CategoryLabel</c> 이 쓰는 모양이다.</para>
///
/// <para>🔴 <b>옛 값은 지우지 않는다</b>(#1). 이미 그 값으로 저장된 줄이 있고, 이미 보낸 글의 종류를
/// 다른 이름으로 덮어 적으면 거짓말이 된다 ⇒ 라벨에 「(옛 값)」을 붙여 그대로 보여 준다
/// (작업지시서 §2-2 결-D).</para>
///
/// <para>⛔ 이 차수에서는 <b>새 파일만</b> 만든다. 위 두 화면의 복제는 갈래②가 화면을 손볼 때
/// 그쪽에서 갈아 끼운다(같은 파일 동시 수정 충돌 방지).</para>
/// </summary>
public static class CsVocab
{
    // ══════════════════════════════════════════════════════════════
    // 분류 8종 — CS 누리집 8폴더와 같은 어휘(서버 AllowedCategory 와 같은 값·같은 순서)
    // ══════════════════════════════════════════════════════════════

    /// <summary>분류 콤보의 단일 출처 — 서버 <c>AllowedCategory</c> 8종과 같은 값·같은 순서.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Categories = new List<KeyValuePair<string, string>>
    {
        new("use", "사용"), new("set", "설정"), new("net", "통신"), new("dat", "데이터"),
        new("upd", "업데이트"), new("bug", "기능오류"), new("ins", "설치"), new("etc", "기타"),
    };

    /// <summary>분류 낱말. 모르는 값은 <b>받은 값 그대로</b> 보여 준다.</summary>
    public static string CategoryLabel(string? c) => c switch
    {
        "use" => "사용", "set" => "설정", "net" => "통신", "dat" => "데이터",
        "upd" => "업데이트", "bug" => "기능오류", "ins" => "설치", "etc" => "기타",
        _ => c ?? "-",
    };

    // ══════════════════════════════════════════════════════════════
    // 업무 영역 8종 — 사장님 낱말 「업무영역」 = 서버 AllowedSubTags (사장님 결재 2026-10-13)
    // ══════════════════════════════════════════════════════════════

    /// <summary>업무 영역 콤보 — 서버 <c>AllowedSubTags</c> 와 같은 값·같은 순서(자유입력 불가).</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> SubTags = new List<KeyValuePair<string, string>>
    {
        new("estimate_sales", "견적·수주·판매"), new("purchase_order", "발주·매입"),
        new("stock", "재고·창고"), new("invoice", "계산서·명세서"),
        new("accounting", "회계·경비"), new("hr_payroll", "사원·급여"),
        new("settings_perm", "설정·권한"), new("etc", "기타"),
    };

    /// <summary>업무 영역 낱말. 모르는 값은 <b>받은 값 그대로</b>.</summary>
    public static string SubTagLabel(string? s) => s switch
    {
        "estimate_sales" => "견적·수주·판매", "purchase_order" => "발주·매입",
        "stock" => "재고·창고", "invoice" => "계산서·명세서",
        "accounting" => "회계·경비", "hr_payroll" => "사원·급여",
        "settings_perm" => "설정·권한", "etc" => "기타",
        _ => s ?? "-",
    };

    // ══════════════════════════════════════════════════════════════
    // 발신 종류 — 본사가 고객사에 먼저 보내는 글의 종류 (설계 §5-1)
    //   🔴 쓰기 콤보는 4종만. 옛 값 2종은 **조회에서만** 보인다 — 허용값에서는 안 지운다(#1).
    // ══════════════════════════════════════════════════════════════

    /// <summary>공지 쓰기 콤보 4종 — 사장님 낱말 그대로.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> NoticeKinds = new List<KeyValuePair<string, string>>
    {
        new("update", "업데이트공지사항"), new("notice", "히트판 공지사항"),
        new("promo", "프로모션 안내"), new("etc", "기타 공지사항"),
    };

    /// <summary>옛 값 2종 — <b>쓰기 콤보에는 안 넣는다.</b> 조회·표시에서만 쓴다(이미 보낸 글의 종류다).</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> NoticeKindsLegacy = new List<KeyValuePair<string, string>>
    {
        new("event", "이벤트(옛 값)"), new("billing", "요금 안내(옛 값)"),
    };

    /// <summary>발신 종류 낱말. 옛 값은 「(옛 값)」을 달아 그대로 보여 주고, 모르는 값은 받은 값 그대로.</summary>
    public static string NoticeKindLabel(string? p) => p switch
    {
        "update" => "업데이트공지사항", "notice" => "히트판 공지사항",
        "promo" => "프로모션 안내", "etc" => "기타 공지사항",
        "event" => "이벤트(옛 값)", "billing" => "요금 안내(옛 값)",
        _ => p ?? "-",
    };

    // ══════════════════════════════════════════════════════════════
    // 접수경로 — A/S 가 들어온 길 (설계 §6)
    //   🔴 `erp_message` 는 **고객사에서 올라온 쪽지**다 ⇒ 손으로 고르는 콤보에는 넣지 않는다.
    //      서버 화이트리스트(AllowedIntakeChannel)도 4종만 받는다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>손으로 적는 접수의 경로 콤보 4종 — 서버 <c>AllowedIntakeChannel</c> 과 같은 값.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> IntakeChannels = new List<KeyValuePair<string, string>>
    {
        new("phone", "전화"), new("email", "이메일"), new("sns", "SNS"), new("etc", "기타"),
    };

    /// <summary>접수경로 낱말.
    /// <para>🔴 오늘 화면은 두 자리에서 <c>== "erp_message" ? "쪽지" : "전화"</c> 로 적고 있어
    /// <c>email</c>·<c>sns</c>·<c>etc</c> 가 전부 「전화」로 보인다(설계 §6). 그 식을 이 함수 하나로 모은다.</para></summary>
    public static string ChannelLabel(string? c) => c switch
    {
        "phone" => "전화", "email" => "이메일", "sns" => "SNS", "etc" => "기타",
        "erp_message" => "고객사 쪽지",
        _ => c ?? "-",
    };
}
