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

    // ══════════════════════════════════════════════════════════════
    // 평가 낱말 — 고객이 매긴 뒤에만 값이 생긴다 (20261013작1 봉합 · [4] 조건-3)
    //   🔴 왜 여기로 모으나: 같은 식이 `AdminCsListPage.razor` 와 `CsTicketDetailPanel.razor`
    //      **두 곳에 똑같이** 적혀 있었다. 한쪽만 고치면 목록 ★ 과 상세 ★ 이 서로 다른 말을
    //      하는데 빌드·게이트는 전건 초록이다 ⇒ 사람이 눈으로만 잡아야 하는 사고다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>평가 낱말. 모르는 값·없는 값은 <b>아는 척하지 않는다</b>(<c>_ =&gt; "-"</c>).</summary>
    public static string RatingLabel(int? r) => r switch
    {
        3 => "좋아요", 2 => "보통", 1 => "아쉬워요", _ => "-",
    };

    // ══════════════════════════════════════════════════════════════
    // 답이 고객사에 닿는지 — 「전달됨」 거짓말 봉합 (20261013작1 봉합 · [3-V] 미봉합 1건)
    //
    //   🔴 사고: 상세 패널이 **수동 접수(전화·이메일·SNS·기타) 건에도** 「전달됨」이라 적었다.
    //      그런데 같은 화면 아래쪽은 「적은 답은 고객사 화면으로 가지 않습니다」라고 적는다 ⇒
    //      **한 화면이 두 말**을 했다. [3-V] 가 양성·음성 쌍으로 쟀다: 고객사 쪽지 건은 앉고(1건),
    //      수동 접수 건은 **0건** 앉는다. 즉 「전달됨」 쪽이 거짓말이었다.
    //
    //   🔴 뿌리는 선재다 — 서버가 Pull 1회마다 테넌트 전체 `delivered_at` 을 찍는다.
    //      그 축은 이 차수가 **건드리지 않는다**(별 트랙). 이 차수는 **화면이 거짓말을 멈추는 것만** 한다.
    //      ⇒ 경로가 `erp_message` 가 아니면 「전달」이라는 말을 **아예 쓰지 않는다.**
    //      경로가 `erp_message` 인 건은 **지금 거동 그대로**다(그건 실제로 간다).
    // ══════════════════════════════════════════════════════════════

    /// <summary>고객사에서 올라온 쪽지 — 이 경로만 본사 답이 고객사 화면에 앉는다.</summary>
    public const string ErpMessageChannel = "erp_message";

    /// <summary>
    /// 본사 답 한 줄이 「어디까지 갔나」를 사람 말로 적는다.
    /// <para>🔴 <paramref name="channel"/> 이 <see cref="ErpMessageChannel"/> 이 아니면
    /// 전달 상태·전달 시각을 <b>말하지 않는다</b> — 갈 길이 없는 건에 「전달됨」이라 적는 것이 거짓말이다.</para>
    /// </summary>
    public static string ReplyDeliveryText(string? channel, DateTime? deliveredAt)
    {
        if (!string.Equals(channel, ErpMessageChannel, StringComparison.Ordinal))
        {
            return "본사에 적은 기록 — 고객사 화면으로 가지 않습니다";
        }

        return deliveredAt is null ? "전달 대기(5분 주기)" : "전달됨";
    }

    // ══════════════════════════════════════════════════════════════
    // 목록 머리글의 분모 — 「없는 분모」 봉합 (20261013작1 봉합 · [4] 조건-2)
    //
    //   🔴 사고: 「히트판공지 전송」 머리글이 서버가 12건만 줘도 「최근 200건 중 12건」이라 적었다.
    //      처음 보는 사람은 「200건에서 12건만 걸러졌다」로 읽는다 — 실제로는 보낸 공지가 통틀어 12건이다.
    //   ⇒ 서버가 **상한에 닿았을 때만** 창을 말한다. 안 닿았으면 그게 전부라서 「총」이라 말해도 참이다.
    //
    //   🔴 왜 화면 안이 아니라 여기인가: `.razor` 안의 private 식은 **아무 게이트도 부를 수 없다**
    //      ⇒ 글자를 세는 가짜 게이트밖에 못 만든다(이 프로젝트가 30번 데인 자리). 여기 두면
    //      게이트가 서버 응답 건수를 **실제로 넣어** 낱말을 받아 볼 수 있다(`CsVocab` 선례 · G-W7-2).
    // ══════════════════════════════════════════════════════════════

    /// <summary>서버 발신목록의 상한 — <c>CsAdminController.cs:602</c> 의 <c>LIMIT 200</c>. 그쪽이 바뀌면 여기도 같이 바꾼다.</summary>
    public const int ServerOutboundListLimit = 200;

    /// <summary>
    /// 발신목록 머리글의 앞말. <paramref name="receivedCount"/> 는 <b>서버가 실제로 준 건수</b>다
    /// (화면이 거른 뒤 숫자가 아니다 — 거른 숫자로 가르면 필터 한 번에 분모가 거짓이 된다).
    /// </summary>
    public static string OutboundCountLabel(int receivedCount) =>
        receivedCount >= ServerOutboundListLimit ? $"최근 {ServerOutboundListLimit:N0}건 중" : "총";

    // ══════════════════════════════════════════════════════════════
    // 응대 유형 — 20261013작2 (사장님 실측 반려 2)·3) · 결재 2026-10-13)
    //
    //   🔴 「들어온 길」(`received_channel`)과 **다른 축**이다. 전화로 들어와 원격지원으로 끝날 수 있다.
    //   🔴 사장님 결재로 두 갈래가 됐다:
    //      · 쪽지로 들어온 건  → **「히트판 A/S센터」 고정** (사람이 안 고른다)
    //      · 손으로 적은 건    → 전화·원격지원·이메일·SNS·기타 중에서 **사람이 고른다**
    //   🔴 옛 값 `message` 는 **지우지 않는다** — 이미 그 값으로 앉은 행을 읽어야 한다(#1).
    // ══════════════════════════════════════════════════════════════

    /// <summary>사람이 고를 수 있는 응대 유형 — 서버 <c>AllowedHandledViaManual</c> 과 같은 값.
    /// <para>🔴 <c>as_center</c> 는 여기 <b>없다</b>. 서버만 앉히는 값이다(수기건을 「히트판 A/S센터가
    /// 받은 것」으로 꾸미는 길을 막는다 — 작1 S-1 과 같은 선).</para></summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> HandledViasManual = new List<KeyValuePair<string, string>>
    {
        new("phone", "전화"), new("remote", "원격지원"),
        new("email", "이메일"), new("sns", "SNS"), new("etc", "기타"),
    };

    /// <summary>쪽지로 들어온 건의 고정 응대 유형 값.</summary>
    public const string AsCenterHandledVia = "as_center";

    /// <summary>응대 유형 낱말. 모르는 값은 <b>아는 척하지 않고</b> 받은 값을 그대로 돌려준다.</summary>
    public static string HandledViaLabel(string? v) => v switch
    {
        "as_center" => "히트판 A/S센터",
        "phone" => "전화", "remote" => "원격지원",
        "email" => "이메일", "sns" => "SNS", "etc" => "기타",
        "message" => "쪽지",   // 옛 값 — 읽기 전용으로 산다
        null or "" => "아직 없음",
        _ => v,
    };

    /// <summary>
    /// 이 건의 응대 유형을 화면이 적을 말.
    /// <para>🔴 쪽지로 들어온 건은 <b>DB 칸이 비어 있어도</b> 「히트판 A/S센터」다 — 그 건의 창구가
    /// 하나뿐이라 참이다(서버가 완료 시점에 같은 값을 칸에도 앉힌다 · 설계 S-3).</para>
    /// </summary>
    public static string HandledViaTextFor(string? channel, string? handledVia) =>
        string.Equals(channel, ErpMessageChannel, StringComparison.Ordinal)
            ? HandledViaLabel(AsCenterHandledVia)
            : HandledViaLabel(handledVia);

    // ══════════════════════════════════════════════════════════════
    // 상태 — 손으로 적은 A/S 는 접수·완료만 (사장님 지시 2026-10-13)
    //   🔴 「처리상태를 고객사에게 보여주지 않으므로 접수와 완료만 기록한다」
    //   🔴 운영에 이미 다른 값으로 앉은 수기건이 있을 수 있다 ⇒ **지금 값은 지우지 않고 보여준다.**
    //      막는 것은 **새로 고르는 것**뿐이다(서버 S-4 와 같은 선).
    // ══════════════════════════════════════════════════════════════

    /// <summary>쪽지로 들어온 건의 상태 5종 — 서버 <c>AllowedStatus</c> 와 같은 값.</summary>
    public static readonly IReadOnlyList<string> StatusesErpMessage = new List<string>
    {
        "접수", "처리중", "보류", "고객답대기", "완료",
    };

    /// <summary>손으로 적은 건의 상태 2종 — 서버 <c>AllowedManualStatus</c> 와 같은 값.</summary>
    public static readonly IReadOnlyList<string> StatusesManual = new List<string> { "접수", "완료" };

    /// <summary>이 건에서 <b>새로 고를 수 있는</b> 상태 목록.</summary>
    public static IReadOnlyList<string> StatusesFor(string? channel) =>
        string.Equals(channel, ErpMessageChannel, StringComparison.Ordinal)
            ? StatusesErpMessage
            : StatusesManual;

    /// <summary>
    /// 지금 값이 <b>고를 수 있는 목록 밖</b>인가 — 운영에 예전부터 앉아 있던 수기건(예: 「처리중」).
    /// <para>🔴 참이면 화면은 그 값을 <b>그대로 보여주고 고르지 못하게</b> 한다. 지우거나 다른 값으로
    /// 바꿔 적으면 사실이 사라진다.</para>
    /// </summary>
    public static bool IsLegacyStatus(string? channel, string? status) =>
        !string.IsNullOrEmpty(status) && !StatusesFor(channel).Contains(status);

    // ══════════════════════════════════════════════════════════════
    // 처리 과정 한 줄 — `bo_cs_ticket_logs` 를 사람 말로 (20261013작2 S-6)
    //   이 표는 상태 전이만 담지 않는다. 5가지 모양이 섞여 있다:
    //     ① 상태 전이   from=접수      to=처리중
    //     ② 응대 저장   from='응대'    to=phone
    //     ③ 분류 바꿈   from='분류:use' to='분류:net'
    //     ④ 담당 배정   from='배정'    to=본사2차
    //     ⑤ 답 약속     from='약속'    to='약속설정'
    //   🔴 모르는 모양은 **아는 척하지 않고** 받은 값을 그대로 보인다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>상태 낱말 — 저장값은 붙여 쓰고(<c>고객답대기</c>) 화면은 띄어 읽는다.</summary>
    public static string StatusLabel(string? s) => s switch
    {
        "고객답대기" => "고객 답 대기",
        null or "" => "-",
        _ => s,
    };

    /// <summary>상태 띠 색. 🔴 목록·상세·목록칸 **세 곳이 같은 식을 쓴다**(두 번째 사전 금지 · G-W7-10 선례).</summary>
    public static string StatusChipClass(string? s) => s switch
    {
        "접수" => "bo-status-chip--suspended",
        "처리중" => "bo-status-chip--trial",
        "보류" or "고객답대기" => "bo-status-chip--neutral",
        "완료" => "bo-status-chip--active",
        _ => "bo-status-chip--neutral",
    };

    /// <summary>처리 과정 한 줄을 사람 말로 적는다(개발 낱말 금지 · #23).</summary>
    public static string ProgressText(string? fromStatus, string? toStatus)
    {
        var f = fromStatus ?? "";
        var t = toStatus ?? "";

        if (f == "응대") return $"응대 유형을 「{HandledViaLabel(t)}」로 적음";
        if (f == "배정") return $"담당을 「{t}」로 배정";
        if (f == "약속") return "답 약속 시각을 정함";
        if (f.StartsWith("분류:", StringComparison.Ordinal))
            return $"분류를 「{CategoryLabel(f.Substring(3))}」 → 「{CategoryLabel(t.StartsWith("분류:", StringComparison.Ordinal) ? t.Substring(3) : t)}」로 바꿈";

        if (string.IsNullOrEmpty(f) || string.IsNullOrEmpty(t)) return $"{f}{t}";
        return $"상태를 「{f}」 → 「{t}」로 바꿈";
    }
}
