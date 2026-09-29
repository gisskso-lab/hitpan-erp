namespace HitPan.API.Services;

/// <summary>
/// 🔴 20260929작3 절A1 — 업데이트 「미완료」 판정. <b>순수 함수 한 곳</b>(설계 §4).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> (사장님 9/29 오더) — 업데이트가 실패·전원 종료·[나중에] 등 어떤 이유로든
/// 안 끝났으면 <b>「업데이트가 정상적으로 이루어지지 않았습니다」 안내가 계속 떠야 한다.</b>
/// 종전 판정은 「새 버전이 있나(I &lt; L)」 하나뿐이라, 무엇이 왜 안 됐는지를 화면이 알 길이 없었다.
/// </para>
/// <para>
/// 🟢 <b>입력</b>: 설치버전 I 와 최신 발견 L 의 비교 결과 · L 의 최신 동의 C · L 의 결과행 A · 경과 시간.
/// <b>출력</b>: 종류(kind) · 고객 문구(IssueText) · 팝업 필요 여부(NeedsPrompt).
/// 컨트롤러·DB·HttpContext 를 모른다 ⇒ 게이트가 표 전부를 컨트롤러 없이 돌린다(G-A1).
/// </para>
/// <para>
/// 🔴 <b>고객 문구에 워치독 detail 원문을 절대 싣지 않는다</b>(#23·#24 · 설계 §7).
/// detail 에는 「마이그 교차검증」·「CS 확인 필요」 같은 개발용어가 들어 있다 —
/// detail 은 <b>어느 고정 문구를 고를지</b>에만 쓰고, 화면에 가는 것은 아래 상수뿐이다(G-A3).
/// </para>
/// <para>
/// ⚠️ <b>메인PC 여부(CanRespond)는 여기서 정하지 않는다</b> — 판정 단일출처
/// <c>MainPcOnlyAttribute.IsMainPc</c> 를 컨트롤러가 <b>호출</b>한다(복붙 금지 · 설계 §4).
/// </para>
/// </remarks>
public static class UpdateIssueJudge
{
    // ══════════════════════════════════════════════════════════════
    // T1 · T2 — 한 곳에만 둔다 (P-2: 설계값 · [4] 실측 후 PM 확정)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// T1 — [예] 뒤 워치독이 in_progress 를 적을 때까지 기다려 주는 시간(설계값 10분).
    /// 이보다 오래 아무 흔적이 없으면 「시작되지 않았다」로 본다.
    /// ⚠️ 워치독 루프 60초 · 펜딩 재적재 ~2분 · 백업 소요 <b>미실측</b> ⇒ [4] 실측 후 확정(P-2).
    /// </summary>
    public static readonly TimeSpan RequestedGrace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// T2 — in_progress 를 「진행 중」으로 믿어 주는 시간(설계값 30분).
    /// 이보다 오래 in_progress 로 멈춰 있으면 「중간에 멈췄다」로 본다.
    /// </summary>
    public static readonly TimeSpan InProgressGrace = TimeSpan.FromMinutes(30);

    // ══════════════════════════════════════════════════════════════
    // 종류(kind) — 응답 계약 IssueKind 값 (설계 §4 · 갈래 F 와의 계약)
    // ══════════════════════════════════════════════════════════════

    public const string KindNone = "none";
    public const string KindFirst = "first";
    public const string KindLater = "later";
    public const string KindRequested = "requested";
    public const string KindNotStarted = "not_started";
    public const string KindInProgress = "in_progress";
    public const string KindInterrupted = "interrupted";
    public const string KindFailed = "failed";

    // ══════════════════════════════════════════════════════════════
    // 고객 문구 — 화면에 가는 글자는 이것뿐이다 (설계 §7 · §4 사이드바 칸)
    // ══════════════════════════════════════════════════════════════

    /// <summary>first — 설계 §4 사이드바 칸. <c>{0}</c> = L(SemVer 로 이미 걸러진 숫자 문자열).</summary>
    public const string TextFirstFormat = "새 버전 {0} 있음";
    /// <summary>requested — 설계 §4 사이드바 칸.</summary>
    public const string TextRequested = "업데이트 준비 중";
    /// <summary>in_progress — 설계 §4 사이드바 칸.</summary>
    public const string TextInProgress = "업데이트 진행 중";
    /// <summary>later — 설계 §7.</summary>
    public const string TextLater = "나중에 하기를 선택하셨습니다.";
    /// <summary>not_started · failed(그 밖) — 설계 §7.</summary>
    public const string TextNotStarted = "업데이트가 시작되지 않았습니다.";
    /// <summary>interrupted · failed「업데이트 중 중단」 — 설계 §7.</summary>
    public const string TextInterrupted = "업데이트가 중간에 멈췄습니다(전원 꺼짐 등).";
    /// <summary>blocked · 「디스크」 — 설계 §7 (P-1 · N-UPD1 흡수).</summary>
    public const string TextBlockedDisk = "컴퓨터 저장공간이 부족해 업데이트하지 못했습니다. 공간을 비운 뒤 다시 진행해 주세요.";
    /// <summary>blocked · 「적용 전 백업」 — 설계 §7.</summary>
    public const string TextBlockedBackup = "업데이트 전 자료 백업에 실패해 진행하지 않았습니다.";
    /// <summary>blocked · 그 밖(교차검증 차단 등) — 설계 §7.</summary>
    public const string TextBlockedOther = "업데이트를 진행할 수 없는 상태입니다. 고객센터로 문의해 주세요.";
    /// <summary>rolled_back — 설계 §7.</summary>
    public const string TextRolledBack = "업데이트 중 문제가 생겨 이전 버전으로 되돌렸습니다.";
    /// <summary>rollback_failed — 설계 §7.</summary>
    public const string TextRollbackFailed = "업데이트를 마치지 못했습니다. 고객센터로 문의해 주세요.";

    // detail 을 가르는 머리 글자 — 워치독이 쓰는 고정 문구(UpdateOrchestrator · 설계 §7).
    //   ⚠️ 비교에만 쓴다. 이 글자들은 화면으로 절대 나가지 않는다.
    private const string DetailDisk = "디스크";
    private const string DetailBackup = "적용 전 백업";
    private const string DetailInterrupted = "업데이트 중 중단";

    /// <summary>
    /// 🔴 C·A 를 <b>한 번에</b> 읽는 SQL(#16 — WhenAll 금지 · 쿼리 1회).
    /// </summary>
    /// <remarks>
    /// <para>
    /// C = <c>local_update_consents</c> 에서 L 의 최신 1건 — 🔴 <b><c>ORDER BY id DESC</c></b>(넣은 순서).
    /// ⬛ 설계 §3·§5-1 의 <c>consented_at DESC, id DESC</c> 는 [3-V] 병렬이슈 02 로 바뀌었다(PM 지시 9/29):
    /// <c>consented_at</c> 은 메인PC 윈도우 시계라 시계가 앞섰다 돌아오면 나중 행이 더 이른 시각을 갖고,
    /// 그러면 새 [예]가 「이미 쓴 [예]」로 읽혀 막다른 길(N-UPD2)이 재발한다.
    /// 워치독(갈래 W)도 <b>같은 정렬</b>로 바꾼다 — 둘이 다른 줄을 「최신」으로 보면 화면과 워치독이 갈라진다.
    /// A = <c>local_update_apply_status</c> 의 L 행(버전당 1행 · UNIQUE).
    /// </para>
    /// <para>
    /// 🟢 <b>늘 정확히 1행</b>을 돌려준다(<c>FROM (SELECT 1)</c> 에 LEFT JOIN 둘) — C·A 가 없으면 그 칸이 NULL.
    /// 경과(A)는 DB 시계끼리 잰다: <c>TIMESTAMPDIFF(SECOND, applied_at, NOW(3))</c> — 워치독이 DB <c>NOW(3)</c> 로 적는다(설계 §4).
    /// </para>
    /// <para>
    /// ⚠️ <c>consent_id</c> 칸은 DB-135(갈래 W)가 만든다. 칸이 없는 DB 에서는 이 조회가 실패하고,
    /// 호출자는 <b>종전 폴백</b>(first = 종전처럼 팝업)으로 간다(#15·#20).
    /// 🔴 운영 코드가 이 상수를 그대로 쓴다 — 게이트 G-A4 도 <b>이 상수</b>를 출하 DDL 위에서 돌린다(복사본 금지).
    /// </para>
    /// </remarks>
    public const string IssueQuerySql = """
        SELECT c.id                                       AS ConsentId,
               c.action                                   AS ConsentAction,
               c.consented_at                             AS ConsentedAt,
               a.consent_id                               AS ApplyConsentId,
               a.result                                   AS ApplyResult,
               a.detail                                   AS ApplyDetail,
               TIMESTAMPDIFF(SECOND, a.applied_at, NOW(3)) AS ApplyElapsedSeconds
        FROM (SELECT 1 AS one) AS k
        LEFT JOIN (
            SELECT id, action, consented_at
            FROM local_update_consents
            WHERE update_version = @Version
            ORDER BY id DESC
            LIMIT 1
        ) AS c ON 1 = 1
        LEFT JOIN local_update_apply_status AS a
               ON a.applied_version = @Version
        """;

    /// <summary><see cref="IssueQuerySql"/> 1행 매핑.</summary>
    public sealed class IssueRow
    {
        public long? ConsentId { get; set; }
        public string? ConsentAction { get; set; }
        public DateTime? ConsentedAt { get; set; }
        public long? ApplyConsentId { get; set; }
        public string? ApplyResult { get; set; }
        public string? ApplyDetail { get; set; }
        public long? ApplyElapsedSeconds { get; set; }
    }

    /// <summary>판정 입력(설계 §4 — I·L·C·A·경과).</summary>
    /// <param name="NewerExists">I &lt; L 인가 — 종전 <c>IsNewerVersion</c> 결과를 그대로 받는다(비교를 두 곳에 두지 않는다).</param>
    /// <param name="LatestVersion">L.</param>
    /// <param name="ConsentId">C.id — 없으면 null.</param>
    /// <param name="ConsentAction">C.action — approve/reject.</param>
    /// <param name="SinceConsent">C.consented_at 부터 지금까지(API 시계끼리).</param>
    /// <param name="ApplyConsentId">A.consent_id — A 없음·NULL(옛 행) = 0 으로 본다.</param>
    /// <param name="ApplyResult">A.result — A 없으면 null.</param>
    /// <param name="ApplyDetail">A.detail — 고정 문구를 고르는 데만 쓴다(화면 비노출).</param>
    /// <param name="SinceApplied">A.applied_at 부터 지금까지(DB 시계끼리).</param>
    public sealed record Input(
        bool NewerExists,
        string? LatestVersion,
        long? ConsentId,
        string? ConsentAction,
        TimeSpan? SinceConsent,
        long? ApplyConsentId,
        string? ApplyResult,
        string? ApplyDetail,
        TimeSpan? SinceApplied);

    /// <summary>판정 결과 — 응답 계약 4필드 중 3개(CanRespond 는 컨트롤러가 IsMainPc 로 채운다).</summary>
    public sealed record Verdict(string Kind, string? IssueText, bool NeedsPrompt);

    /// <summary>조회 행 → 판정 입력. <paramref name="now"/> 는 API 시계(<c>consented_at</c> 을 쓴 그 시계).</summary>
    public static Input FromRow(bool newerExists, string? latestVersion, IssueRow? row, DateTime now) =>
        new(
            newerExists,
            latestVersion,
            row?.ConsentId,
            row?.ConsentAction,
            row?.ConsentedAt is DateTime at ? now - at : null,
            row?.ApplyConsentId,
            row?.ApplyResult,
            row?.ApplyDetail,
            row?.ApplyElapsedSeconds is long s ? TimeSpan.FromSeconds(s) : null);

    /// <summary>
    /// 설계 §4 판정 표 그대로. 표 밖의 값(모르는 action·result)은 <b>더 묻는 쪽</b>으로 간다.
    /// </summary>
    public static Verdict Judge(Input i)
    {
        // I ≥ L 이거나 L 없음 → none (종전 UpdateAvailable=false 그대로).
        if (!i.NewerExists || string.IsNullOrWhiteSpace(i.LatestVersion))
            return new Verdict(KindNone, null, false);

        var action = i.ConsentAction?.Trim().ToLowerInvariant();

        // C 없음 → first. 모르는 action 도 여기로 — 종전처럼 묻는 것이 가장 안전하다.
        if (i.ConsentId is null || (action != "approve" && action != "reject"))
            return Prompt(KindFirst, string.Format(TextFirstFormat, i.LatestVersion.Trim()));

        // C = reject → later (사장님 문구로 다시 묻는다).
        if (action == "reject")
            return Prompt(KindLater, TextLater);

        // C = approve. A 없음·NULL(옛 행) = 0 (설계 §3 — 워치독과 같은 규칙).
        var used = i.ApplyResult is null ? 0 : (i.ApplyConsentId ?? 0);

        // 새 [예] — 워치독이 아직 이 [예]로 시도를 열지 않았다.
        //   (A.consent_id < C.id 인 경우 포함 — 앞 시도 실패 뒤 새 [예] · 설계 §4)
        if (i.ConsentId.Value > used)
        {
            var since = i.SinceConsent ?? TimeSpan.Zero;
            return since >= RequestedGrace
                ? Prompt(KindNotStarted, TextNotStarted)
                : Quiet(KindRequested, TextRequested);
        }

        // 이 [예]로 연 시도의 결과 (A.consent_id = C.id).
        //   🔴 [3-V] 병렬이슈 02 · PM 지시 — C.id < A.consent_id(최신 동의가 이미 쓴 것보다 옛것)도
        //      「이미 쓴 [예]」와 같게 판정한다(새 시도 아님 · 워치독 규칙 C 의 latest.id ≤ used 와 같은 판정).
        //      정렬을 id 로 바꿔 원리적으로는 안 생기지만, 생기면 A.result 로 떨어져 실패면 **다시 묻는다.**
        var result = i.ApplyResult?.Trim().ToLowerInvariant();
        var detail = i.ApplyDetail ?? string.Empty;

        switch (result)
        {
            case "in_progress":
                var running = i.SinceApplied ?? TimeSpan.Zero;
                return running >= InProgressGrace
                    ? Prompt(KindInterrupted, TextInterrupted)
                    : Quiet(KindInProgress, TextInProgress);

            case "blocked":
                if (detail.Contains(DetailDisk, StringComparison.Ordinal))
                    return Prompt(KindFailed, TextBlockedDisk);
                if (detail.Contains(DetailBackup, StringComparison.Ordinal))
                    return Prompt(KindFailed, TextBlockedBackup);
                return Prompt(KindFailed, TextBlockedOther);

            case "rolled_back":
                return Prompt(KindFailed, TextRolledBack);

            case "rollback_failed":
                return Prompt(KindFailed, TextRollbackFailed);

            case "failed":
                return detail.Contains(DetailInterrupted, StringComparison.Ordinal)
                    ? Prompt(KindFailed, TextInterrupted)
                    : Prompt(KindFailed, TextNotStarted);

            default:
                // success 인데 I < L(설계 §4 failed 행) · 모르는 result — 「failed · 그 밖」 문구.
                //   ⚠️ success(I<L) 전용 문구는 설계 §7 에 없다 — 명세서 §5 확인 요청.
                return Prompt(KindFailed, TextNotStarted);
        }
    }

    /// <summary>
    /// 조회 실패 때의 판정 — <b>종전 동작</b>(새 버전이 있으면 팝업) 그대로(#15·#20 · 설계 §4 조회 실패 행).
    /// </summary>
    public static Verdict Fallback(bool newerExists, string? latestVersion) =>
        Judge(new Input(newerExists, latestVersion, null, null, null, null, null, null, null));

    private static Verdict Prompt(string kind, string text) => new(kind, text, true);

    private static Verdict Quiet(string kind, string text) => new(kind, text, false);
}
