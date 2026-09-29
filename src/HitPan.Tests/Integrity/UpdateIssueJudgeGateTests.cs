using HitPan.API.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-A1 · G-A3</b> — 업데이트 「미완료」 판정 표(설계 §4)와 고객 문구 매핑(설계 §7). 20260929작3 절A1.
/// </summary>
/// <remarks>
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 생산코드 <see cref="UpdateIssueJudge.Judge"/> 를 <b>불러서</b> 판정값을 읽는다.
/// 기대값은 설계 §4 표·§7 표에서 온다. 이 파일에 판정 로직의 사본은 없다.
/// </para>
/// <para>
/// 🔴 <b>음성 대조군</b>(개발명세서 §4) — 판정 표를 한 줄씩 뒤집으면(예: T1 비교를 <c>&gt;</c> 로,
/// reject 분기를 first 로, blocked 매핑을 detail 원문 전달로) 이 파일의 해당 게이트가 FAIL 한다.
/// </para>
/// <para>⚠️ DB·HttpContext 무접촉 — 순수 함수만. <c>build</c> 잡에서 돈다.</para>
/// </remarks>
public sealed class UpdateIssueJudgeGateTests
{
    private const string L = "1.3.48";

    private static UpdateIssueJudge.Verdict J(
        bool newer = true,
        long? consentId = null,
        string? action = null,
        TimeSpan? sinceConsent = null,
        long? applyConsentId = null,
        string? result = null,
        string? detail = null,
        TimeSpan? sinceApplied = null,
        string? latest = L) =>
        UpdateIssueJudge.Judge(new UpdateIssueJudge.Input(
            newer, latest, consentId, action, sinceConsent, applyConsentId, result, detail, sinceApplied));

    private static void Is(UpdateIssueJudge.Verdict v, string kind, bool prompt)
    {
        Assert.Equal(kind, v.Kind);
        Assert.Equal(prompt, v.NeedsPrompt);
    }

    // ══════════════════════════════════════════════════════════════
    // G-A1 — 판정 표 (설계 §4) 7종 + none
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-A1 none 🔴 설치버전이 최신이면(I≥L) 아무것도 안 띄운다")]
    public void GA1_none()
    {
        Is(J(newer: false), UpdateIssueJudge.KindNone, false);
        Assert.Null(J(newer: false).IssueText);
        // 동의·결과가 남아 있어도 I≥L 이면 none — 끝난 업데이트를 다시 묻지 않는다.
        Is(J(newer: false, consentId: 5, action: "approve", applyConsentId: 5, result: "success"), UpdateIssueJudge.KindNone, false);
        // L 없음도 none.
        Is(J(latest: null), UpdateIssueJudge.KindNone, false);
    }

    [Fact(DisplayName = "G-A1 first 🔴 동의가 한 번도 없으면 종전처럼 묻는다")]
    public void GA1_first()
    {
        var v = J();
        Is(v, UpdateIssueJudge.KindFirst, true);
        Assert.Equal("새 버전 1.3.48 있음", v.IssueText);
    }

    [Fact(DisplayName = "G-A1 later 🔴 최신 동의가 [나중에]면 사장님 문구로 다시 묻는다")]
    public void GA1_later()
    {
        var v = J(consentId: 6, action: "reject", sinceConsent: TimeSpan.FromDays(3));
        Is(v, UpdateIssueJudge.KindLater, true);
        Assert.Equal(UpdateIssueJudge.TextLater, v.IssueText);

        // [나중에] 이전에 실패한 시도가 있어도 최신이 [나중에]면 later.
        Is(J(consentId: 6, action: "reject", applyConsentId: 5, result: "rolled_back"), UpdateIssueJudge.KindLater, true);
    }

    [Fact(DisplayName = "G-A1 requested·not_started 🔴 [예] 뒤 T1(10분) 경계 — 9:59 는 기다리고 10:00 은 묻는다")]
    public void GA1_requested_not_started_T1_boundary()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), UpdateIssueJudge.RequestedGrace);

        var waiting = J(consentId: 7, action: "approve", sinceConsent: new TimeSpan(0, 9, 59));
        Is(waiting, UpdateIssueJudge.KindRequested, false);
        Assert.Equal(UpdateIssueJudge.TextRequested, waiting.IssueText);

        var stale = J(consentId: 7, action: "approve", sinceConsent: new TimeSpan(0, 10, 0));
        Is(stale, UpdateIssueJudge.KindNotStarted, true);
        Assert.Equal(UpdateIssueJudge.TextNotStarted, stale.IssueText);
    }

    [Fact(DisplayName = "G-A1 requested 🔴 앞 시도 실패 뒤 새 [예](A.consent_id < C.id)는 새 시도다")]
    public void GA1_new_approve_after_failed_attempt()
    {
        // 앞 [예](5)가 rolled_back · 새 [예](6) — 결과행은 아직 5 를 가리킨다.
        Is(J(consentId: 6, action: "approve", sinceConsent: TimeSpan.FromMinutes(1),
              applyConsentId: 5, result: "rolled_back"), UpdateIssueJudge.KindRequested, false);
        Is(J(consentId: 6, action: "approve", sinceConsent: TimeSpan.FromMinutes(11),
              applyConsentId: 5, result: "rolled_back"), UpdateIssueJudge.KindNotStarted, true);

        // 옛 행(consent_id NULL = 0) — 새 [예]로 본다(설계 §3 NULL=0).
        Is(J(consentId: 1, action: "approve", sinceConsent: TimeSpan.FromMinutes(1),
              applyConsentId: null, result: "rolled_back"), UpdateIssueJudge.KindRequested, false);
    }

    /// <summary>
    /// 🔴 [3-V] 병렬이슈 02 · PM 지시 — <c>C.id &lt; A.consent_id</c>(최신 동의가 이미 쓴 것보다 옛것)는
    /// 「이미 쓴 [예]」와 같게 판정한다. 새 시도(requested)가 아니다.
    /// </summary>
    /// <remarks>음성 대조군 — 판정의 <c>ConsentId &gt; used</c> 를 <c>ConsentId != used</c> 로 바꾸면 requested 가 되어 FAIL.</remarks>
    [Fact(DisplayName = "G-A1 병렬이슈02 🔴 C.id < A.consent_id 는 이미 쓴 [예] — 결과행으로 판정한다(새 시도 아님)")]
    public void GA1_older_consent_than_used_is_already_used()
    {
        // 결과행이 더 큰 동의(7)로 rolled_back · 최신 동의로 뽑힌 것은 5 → 실패 → 다시 묻는다.
        var failed = J(consentId: 5, action: "approve", sinceConsent: TimeSpan.FromMinutes(1),
                       applyConsentId: 7, result: "rolled_back");
        Is(failed, UpdateIssueJudge.KindFailed, true);
        Assert.Equal(UpdateIssueJudge.TextRolledBack, failed.IssueText);

        // 결과행이 in_progress 면 진행 중 — requested 로 떨어지지 않는다.
        Is(J(consentId: 5, action: "approve", sinceConsent: TimeSpan.FromHours(1),
              applyConsentId: 7, result: "in_progress", sinceApplied: TimeSpan.FromMinutes(1)),
           UpdateIssueJudge.KindInProgress, false);
    }

    /// <summary>
    /// 🔴 [3-V] 병렬이슈 02 — 판정 조회의 「최신 1건」은 <b>id 순</b>이다(시계 순 아님).
    /// </summary>
    /// <remarks>
    /// 운영 SQL 상수를 읽어 정렬 절을 본다. ⚠️ 글자 검사다 — 동작 확인은 G-A4(출하 DDL 위 실행)에
    /// 「id 는 크고 consented_at 은 이른 [예]」 행을 넣어 잰다. 음성 대조군 = 정렬을 시각 먼저로 되돌리면 두 게이트 모두 FAIL.
    /// </remarks>
    [Fact(DisplayName = "G-A1 병렬이슈02 🔴 최신 동의는 id 순으로 고른다 (consented_at 순 아님)")]
    public void GA1_latest_consent_is_by_id()
    {
        var sql = UpdateIssueJudge.IssueQuerySql;
        Assert.Contains("ORDER BY id DESC", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY consented_at", sql, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G-A1 in_progress·interrupted 🔴 T2(30분) 경계 — 29:59 는 진행 중 · 30:00 은 멈춤")]
    public void GA1_in_progress_interrupted_T2_boundary()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), UpdateIssueJudge.InProgressGrace);

        var running = J(consentId: 8, action: "approve", sinceConsent: TimeSpan.FromHours(2),
                        applyConsentId: 8, result: "in_progress", sinceApplied: new TimeSpan(0, 29, 59));
        Is(running, UpdateIssueJudge.KindInProgress, false);
        Assert.Equal(UpdateIssueJudge.TextInProgress, running.IssueText);

        var stuck = J(consentId: 8, action: "approve", sinceConsent: TimeSpan.FromHours(2),
                      applyConsentId: 8, result: "in_progress", sinceApplied: new TimeSpan(0, 30, 0));
        Is(stuck, UpdateIssueJudge.KindInterrupted, true);
        Assert.Equal(UpdateIssueJudge.TextInterrupted, stuck.IssueText);
    }

    [Fact(DisplayName = "G-A1 failed 🔴 이 [예]로 연 시도가 실패하면 사유 문구로 다시 묻는다")]
    public void GA1_failed_mapping()
    {
        UpdateIssueJudge.Verdict F(string result, string? detail) =>
            J(consentId: 9, action: "approve", sinceConsent: TimeSpan.FromMinutes(1),
              applyConsentId: 9, result: result, detail: detail, sinceApplied: TimeSpan.FromMinutes(1));

        // 워치독이 실제로 쓰는 detail (UpdateOrchestrator.cs:156·177·217·222·982·986).
        var cases = new (string result, string? detail, string text)[]
        {
            ("blocked", "디스크 여유공간 부족 — 업데이트 시작 차단", UpdateIssueJudge.TextBlockedDisk),
            ("blocked", "적용 전 백업 실패 — 업데이트 차단(데이터 무결성 우선)", UpdateIssueJudge.TextBlockedBackup),
            ("blocked", "마이그 교차검증 게이트 차단(CS 확인 필요)", UpdateIssueJudge.TextBlockedOther),
            ("rolled_back", "파일 교체 실패 — 구버전 유지", UpdateIssueJudge.TextRolledBack),
            ("rolled_back", "업데이트 중 중단 — 이전 버전으로 되돌림", UpdateIssueJudge.TextRolledBack),
            ("rollback_failed", "헬스 실패 → 롤백까지 실패(CS 개입 필요)", UpdateIssueJudge.TextRollbackFailed),
            ("failed", "업데이트 중 중단", UpdateIssueJudge.TextInterrupted),
            ("failed", "다운로드 실패", UpdateIssueJudge.TextNotStarted),
            ("failed", null, UpdateIssueJudge.TextNotStarted),
            ("success", null, UpdateIssueJudge.TextNotStarted), // success 인데 I<L (설계 §4 failed 행)
        };

        foreach (var (result, detail, text) in cases)
        {
            var v = F(result, detail);
            Assert.True(v.Kind == UpdateIssueJudge.KindFailed && v.NeedsPrompt,
                $"result={result} detail={detail} → kind={v.Kind} prompt={v.NeedsPrompt} (기대 failed · 팝업)");
            Assert.Equal(text, v.IssueText);
        }
    }

    [Fact(DisplayName = "G-A1 NeedsPrompt 🔴 팝업은 first·later·not_started·interrupted·failed 다섯에만")]
    public void GA1_needs_prompt_set()
    {
        var prompted = new[]
        {
            J(),
            J(consentId: 1, action: "reject"),
            J(consentId: 1, action: "approve", sinceConsent: TimeSpan.FromHours(1)),
            J(consentId: 1, action: "approve", applyConsentId: 1, result: "in_progress", sinceApplied: TimeSpan.FromHours(1)),
            J(consentId: 1, action: "approve", applyConsentId: 1, result: "rolled_back"),
        };
        Assert.All(prompted, v => Assert.True(v.NeedsPrompt, v.Kind));
        Assert.Equal(
            new[] { "first", "later", "not_started", "interrupted", "failed" },
            prompted.Select(v => v.Kind).ToArray());

        var quiet = new[]
        {
            J(newer: false),
            J(consentId: 1, action: "approve", sinceConsent: TimeSpan.FromMinutes(1)),
            J(consentId: 1, action: "approve", applyConsentId: 1, result: "in_progress", sinceApplied: TimeSpan.FromMinutes(1)),
        };
        Assert.All(quiet, v => Assert.False(v.NeedsPrompt, v.Kind));
        Assert.Equal(new[] { "none", "requested", "in_progress" }, quiet.Select(v => v.Kind).ToArray());
    }

    [Fact(DisplayName = "G-A1 폴백 🔴 C·A 조회 실패는 종전 동작(새 버전 있으면 팝업) 그대로")]
    public void GA1_fallback_is_previous_behavior()
    {
        Is(UpdateIssueJudge.Fallback(true, L), UpdateIssueJudge.KindFirst, true);
        Is(UpdateIssueJudge.Fallback(false, L), UpdateIssueJudge.KindNone, false);
    }

    // ══════════════════════════════════════════════════════════════
    // G-A3 — 고객 문구에 detail 원문·개발용어 0
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-A3</b> — 어떤 결과·detail 조합에서도 IssueText 에 detail 원문·「CS」·「마이그」·「교차검증」이 없다(#23·#24).
    /// </summary>
    /// <remarks>
    /// 🔴 음성 대조군 — blocked 분기가 <c>detail</c> 을 그대로 IssueText 로 돌려주면 「마이그 교차검증 … CS」 줄에서 FAIL.
    /// </remarks>
    [Fact(DisplayName = "G-A3 🔴 고객 문구에 워치독 detail 원문·개발용어가 없다")]
    public void GA3_no_detail_or_jargon_in_issue_text()
    {
        var details = new[]
        {
            "디스크 여유공간 부족 — 업데이트 시작 차단",
            "적용 전 백업 실패 — 업데이트 차단(데이터 무결성 우선)",
            "마이그 교차검증 게이트 차단(CS 확인 필요)",
            "교체 준비(정지) 실패 — 구버전 유지",
            "파일 교체 실패 — 구버전 유지",
            "헬스 실패 → 롤백까지 실패(CS 개입 필요)",
            "업데이트 중 중단",
            "Sha256 mismatch: expected=abc actual=def",
            null,
        };
        var results = new[] { "blocked", "rolled_back", "rollback_failed", "failed", "success", "in_progress", "zzz" };
        string[] banned = { "CS", "마이그", "교차검증", "Sha256", "롤백", "게이트", "헬스" };

        var texts = new List<string>();
        foreach (var r in results)
        foreach (var d in details)
        foreach (var since in new[] { TimeSpan.Zero, TimeSpan.FromHours(1) })
        {
            var v = J(consentId: 3, action: "approve", sinceConsent: since,
                      applyConsentId: 3, result: r, detail: d, sinceApplied: since);
            if (v.IssueText is null) continue;
            texts.Add(v.IssueText);

            if (d is not null)
                Assert.False(v.IssueText.Contains(d, StringComparison.Ordinal),
                    $"detail 원문이 고객 문구로 새었다: result={r} detail={d} → {v.IssueText}");
        }

        // 나머지 종류도 함께 검사한다.
        texts.Add(J().IssueText!);
        texts.Add(J(consentId: 1, action: "reject").IssueText!);
        texts.Add(J(consentId: 1, action: "approve", sinceConsent: TimeSpan.FromMinutes(1)).IssueText!);
        texts.Add(J(consentId: 1, action: "approve", sinceConsent: TimeSpan.FromHours(1)).IssueText!);

        foreach (var t in texts.Distinct())
        foreach (var b in banned)
            Assert.False(t.Contains(b, StringComparison.Ordinal), $"고객 문구에 「{b}」: {t}");
    }
}
