using System.Data;
using Dapper;
using HitPan.Application.Common;
using MySqlConnector;

namespace HitPan.API.Services.Cooltime;

/// <summary>
/// 쿨타임(설정 시각 자동 로그아웃 · PC 만) 판단 본체 — 20260928작4 · 갈래 ①(B · C · D · F).
/// </summary>
/// <remarks>
/// <para>
/// 사장님 오더: <i>"쿨타임기능 = 전체퇴근, 로그아웃 기능. 혹여, 야근으로 접속하고있는 사용자가 있다면, 자동
/// "로그아웃 설정시간입니다. " 로그아웃을 취소하고 작업을 계속하시겠습니까? y/n 팝업을 띄우고 제어하면 됨."</i> ·
/// <i>"그리고 팝업에 y/N 선택 안할시 자동로그아웃"</i> · <i>"= 60초"</i> · Q1 <i>"쿨타임으로 로그아웃된 퇴근기록은 무조건
/// 정시퇴근으로 기록할것."</i>
/// </para>
/// <para>
/// 🔴 <b>끝내는 함수는 하나다</b>(설계 §4) — 서버 작업(§3 ①②) · 팝업 [지금 로그아웃] · 카운트다운 0 이 모두
/// <see cref="CooltimeLogoutAsync"/> 를 부른다. 조건이 갈리지 않게 여기 말고 다른 곳에 삭제 문장을 두지 않는다.
/// </para>
/// <para>
/// 🔴 <b>경주 조건은 한 SQL 문장의 WHERE 에 둔다</b>(설계 §3) — [계속]의 UPDATE 와 끝내기의 DELETE 가 같은
/// <c>cooltime_asked_at</c> 경계(65초)를 서로 반대로 건다 ⇒ 동시에 와도 한쪽만 1행을 얻는다.
/// </para>
/// <para>
/// 🔴 <b>시각</b>(설계 §D-5) — <c>user_sessions</c> 쪽 비교·기록은 전부 SQL 안 <c>UTC_TIMESTAMP(6)</c>. 앱이 「지금」 을
/// SQL 인자로 넘기지 않는다. 주입 <c>nowLocal</c> 은 설정 시각 <c>C</c> 계산과 <c>attendance</c>(로컬)에만 쓴다.
/// </para>
/// <para>
/// 테넌트·세션 번호는 서버 작업이면 <c>tenant_settings</c>·<c>user_sessions</c> 행에서, API 면 JWT 에서만 온다(#2).
/// 본사 호출 0 — 고객 PC 로컬 API 안에서만 돈다(#18 · #22 · #30).
/// </para>
/// </remarks>
public sealed class CooltimeEngine
{
    private readonly IDbConnection _db;
    private readonly ICooltimePusher _pusher;
    private readonly ILogger<CooltimeEngine> _logger;

    public CooltimeEngine(IDbConnection db, ICooltimePusher pusher, ILogger<CooltimeEngine> logger)
    {
        _db = db;
        _pusher = pusher;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // 순수 규칙 — DB 없이 시험할 수 있다(갈래 ③ 비DB 시험 대상)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 설정 시각 순간 <c>C</c>(로컬) — 오늘 그 시각이 지났으면(같거나 지나면) 오늘, 아니면 어제의 그 시각(설계 §2 끝).
    /// </summary>
    public static DateTime MostRecentCooltimeLocal(DateTime nowLocal, TimeSpan cooltimeAt)
    {
        var today = nowLocal.Date + cooltimeAt;
        return nowLocal >= today ? today : today.AddDays(-1);
    }

    /// <summary>
    /// 출근 뒤 <b>처음 오는</b> 정시 퇴근 시각(로컬 · 설계 §6). 출근과 같은 순간이면 다음 날의 그 시각.
    /// 예: 출근 09:00 · 정시 18:00 → 같은 날 18:00 / 출근 22:00 · 정시 06:00 → 다음 날 06:00.
    /// </summary>
    public static DateTime FirstRegularLeaveAfter(DateTime checkInLocal, TimeSpan regularLeave)
    {
        var sameDay = checkInLocal.Date + regularLeave;
        return sameDay > checkInLocal ? sameDay : sameDay.AddDays(1);
    }

    /// <summary>서버 PC 벽시계(로컬) → UTC. <c>user_sessions</c> 와 비교할 때만 쓴다(설계 §2 · §D-5).</summary>
    public static DateTime ServerLocalToUtc(DateTime local)
        => DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(), DateTimeKind.Unspecified);

    /// <summary>UTC(DB 의 <c>datetime(6)</c> 값) → 서버 PC 벽시계(로컬). <c>attendance</c> 와 비교할 때만 쓴다(설계 §6 · §D-5).</summary>
    public static DateTime UtcToServerLocal(DateTime utc)
        => DateTime.SpecifyKind(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);

    /// <summary>
    /// 🆕 11/27 설계 §E E-2 — 설정 시각 순간 <c>C</c>(UTC)가 마지막 켬·시각 바꿈 순간(<c>cooltime_changed_at</c> · UTC)
    /// 이전(같음 포함)이면 <c>true</c> = 그 <c>C</c> 에서는 ②③ 을 건너뛴다(저장 뒤 첫 <c>C</c> 부터 적용).
    /// 기록 없음(<c>null</c> · 옛 설정)은 건너뛰지 않는다.
    /// </summary>
    public static bool IsBeforeSettingChange(DateTime cooltimeUtc, DateTime? changedAtUtc)
        => changedAtUtc is DateTime changed && cooltimeUtc <= changed;

    /// <summary>근무시간 — 기존 손 퇴근과 같은 식(<c>HrService.CheckOutAsync</c> · 소수 1자리).</summary>
    public static double WorkHours(DateTime checkIn, DateTime checkOut)
        => Math.Round((checkOut - checkIn).TotalHours, 1);

    // ═══════════════════════════════════════════════════════════════════
    // 한 바퀴 — 설계 §3 규칙 ①②③④
    //   ⬛ [낡은 줄 · 11/28 이전] 「④ 는 개발명세서 「못 한 것」 — 결재 대기」
    //   🆕 11/28 §287 대표 결재 E-6 (가) W = 4 → ④ 를 ③ 의 묻기 문장(askWhere)에 OR 한 갈래로 넣었다(설계 §E :297).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 서버 작업 한 바퀴. 켜진 회사마다 ① 무응답 끝내기 → ② 놓친 끝내기 → ③④ 묻기 순서로 돈다.
    /// </summary>
    /// <param name="nowLocal">서버 PC 벽시계(운영 = <c>DateTime.Now</c>) — <c>C</c> 계산 · 퇴근 기록에만 쓴다. 시험이 주입한다.</param>
    public async Task<CooltimeTickResult> RunOnceAsync(DateTime nowLocal, CancellationToken ct = default)
    {
        // ⬛ [낡은 줄 · 11/28 HQA 반려 P1 이전 · 커밋 ce77209f 의 이 파일 :103–202]
        //   이 자리에 「켜진 회사 읽기」(:105–111) 와 「회사마다 ① → E-2 건너뛰기 → ② → ③④」(:113–202) 본문이 있었다.
        //   문장 · 순서를 그대로 아래 두 공개 메서드로 옮겼다(지우지 않고 이동 · #1).
        //   ⬛ [낡은 줄 · 11/28 고침 2 이전] 옮기며 바뀐 글자는 둘뿐: foreach 안의 `continue;`(E-2 건너뛰기) → 회사 한 곳 함수의 `return Tally();` · 합계 → 회사마다 더함.
        //   🆕 11/28 고침 2([4] P3-5 · 명세서와 같은 표기) — 옮기며 **글자가 바뀐 문장은 하나뿐**: foreach 안의 `continue;`(E-2 건너뛰기)
        //   → 회사 한 곳 함수의 `return Tally();`. 그 밖은 모양만 — 합계 = 회사마다 더함(같은 값) · 덧붙임 둘 =
        //   회사 한 곳 함수 머리의 인자 null 검사 · 회사마다 연결 열림 확인(열려 있으면 아무것도 안 함).
        //   옛 본문을 여기 주석으로 다시 적지 않는 이유 = 변이 런(scripts/gates/cooltime-mutation-run.sh)이
        //   제품 파일에서 바꿀 글자를 「정확히 1번」 으로 센다 — 주석 사본이 그 셈을 깨뜨린다.
        // 🆕 11/28 설계 쿨타임_설정끄기경주_고침 §6 — 같은 문장 · 같은 순서. 제품 길 = 이 함수 하나(CooltimeService 그대로).
        var tenants = await ReadTenantsAsync(ct).ConfigureAwait(false);

        int asked = 0, endedNoAnswer = 0, endedMissed = 0, deadlocks = 0;

        foreach (var t in tenants)
        {
            var one = await RunTenantAsync(t, nowLocal, ct).ConfigureAwait(false);
            asked += one.Asked;
            endedNoAnswer += one.EndedNoAnswer;
            endedMissed += one.EndedMissed;
            deadlocks += one.Deadlocks;
        }

        return new CooltimeTickResult(tenants.Count, asked, endedNoAnswer, endedMissed, deadlocks);
    }

    /// <summary>
    /// 🆕 11/28 — 켜진 회사 목록 읽기(<see cref="RunOnceAsync"/> 의 첫 문장을 그대로 옮김). 잠금 없는 읽기 한 번이다 ⇒
    /// 이 값으로 쓰기 전에 <see cref="RunTenantAsync"/> 의 쓰기 거래가 첫 문장에서 다시 확인한다.
    /// 시험(G-C25 · G-C26)이 「읽기 → 설정 저장 → 회사 한 곳 돌기」 순서를 시간 기다림 없이 세우려고 공개한다.
    /// </summary>
    public async Task<IReadOnlyList<CooltimeTenantSnapshot>> ReadTenantsAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 켜진 회사가 없으면 한 바퀴 = 이 가벼운 조회 1회(설계 §2). 행 없는 회사 = 꺼짐.
        var tenants = (await _db.QueryAsync<TenantCooltimeRow>(new CommandDefinition(
            // 🆕 11/27 §E E-2 — 마지막 켬·시각 바꿈 순간(cooltime_changed_at · UTC)을 같이 읽는다(②③ 건너뛰기 판정).
            @"SELECT tenant_id AS TenantId, cooltime_at AS CooltimeAt, cooltime_changed_at AS ChangedAtUtc
                FROM tenant_settings
               WHERE cooltime_enabled = 1 AND cooltime_at IS NOT NULL",
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        return tenants.Select(r => new CooltimeTenantSnapshot(r.TenantId, r.CooltimeAt, r.ChangedAtUtc)).ToList();
    }

    /// <summary>
    /// 🆕 11/28 — 켜진 회사 한 곳의 ① 무응답 끝내기 → ② 놓친 끝내기 → ③④ 묻기(<see cref="RunOnceAsync"/> 의 회사마다 본문을 그대로 옮김).
    /// </summary>
    /// <param name="t"><see cref="ReadTenantsAsync"/> 가 읽은 그 회사 값.</param>
    /// <param name="nowLocal">서버 PC 벽시계 — <c>C</c> 계산 · 퇴근 기록에만 쓴다.</param>
    /// <param name="ct">취소.</param>
    /// <returns>그 회사 한 곳의 결과(<see cref="CooltimeTickResult.EnabledTenants"/> = 1).</returns>
    public async Task<CooltimeTickResult> RunTenantAsync(CooltimeTenantSnapshot t, DateTime nowLocal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        int asked = 0, endedNoAnswer = 0, endedMissed = 0, deadlocks = 0;
        CooltimeTickResult Tally() => new(1, asked, endedNoAnswer, endedMissed, deadlocks);

        // 옛 foreach 몸통 — 들여쓰기 · 글자를 그대로 두려고 블록 하나로 남겼다([4] 문장 대조 · 설계 §7 R4).
        {
            var cLocal = MostRecentCooltimeLocal(nowLocal, t.CooltimeAt);
            var cUtc = ServerLocalToUtc(cLocal);

            // ① 물은 뒤 65초 — 무응답 끝내기
            var noAnswer = await _db.QueryAsync<string>(new CommandDefinition(
                @"SELECT session_id FROM user_sessions
                   WHERE tenant_id = @TenantId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
                     AND cooltime_asked_at IS NOT NULL
                     AND cooltime_asked_at <= UTC_TIMESTAMP(6) - INTERVAL 65 SECOND",
                new { t.TenantId }, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var sid in noAnswer.ToList())
            {
                var r = await EndQuietlyAsync(t.TenantId, sid, nowLocal, CooltimeEndCause.NoAnswer, null, t, ct).ConfigureAwait(false);
                if (r == CooltimeEndOutcome.Ended) endedNoAnswer++;
                else if (r == CooltimeEndOutcome.Deadlock) deadlocks++;
            }

            // 🆕 11/27 §E E-2 — 저장 뒤 첫 C 부터 적용. C 가 마지막 켬·시각 바꿈 순간 이전(같음 포함)이면
            //   이 회사는 이번 C 에서 ②③ 을 건너뛴다(① 은 위에서 이미 돌았다 — 떠 있던 물음의 무응답은 끝낸다).
            //   ⚠️ 이 건너뛰기를 빼면 G-C19 가 FAIL 한다(음성 대조군 ⓕ — 14:00 에 「13:00」 저장 → 첫 바퀴 끝냄 2).
            //   🆕 11/28 — ④(E-6)도 아래 묻기 문장 안에 있으므로 같이 건너뛴다(설계 §3 ② 칸 「②③④ 공통」).
            //   ⬛ [낡은 줄 · 11/28 나누기 이전 — foreach 안] if (IsBeforeSettingChange(cUtc, t.ChangedAtUtc)) continue;
            //   🆕 11/28 — 회사 한 곳 함수로 옮겨 `continue` 가 `return Tally()` 가 되었다(같은 뜻 · 이 회사의 ②③④ 를 건너뜀).
            if (IsBeforeSettingChange(cUtc, t.ChangedAtUtc)) return Tally();

            // ② 한 번도 안 물었는데 설정 시각 + 5분이 지났다(서버가 꺼져 있다 켜진 경우 등) — 묻지 않고 끝낸다(Q5)
            var missed = await _db.QueryAsync<string>(new CommandDefinition(
                @"SELECT session_id FROM user_sessions
                   WHERE tenant_id = @TenantId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
                     AND cooltime_asked_at IS NULL AND cooltime_snooze_until IS NULL
                     AND login_at < @CUtc
                     AND UTC_TIMESTAMP(6) >= @CUtc + INTERVAL 5 MINUTE",
                new { t.TenantId, CUtc = cUtc }, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var sid in missed.ToList())
            {
                var r = await EndQuietlyAsync(t.TenantId, sid, nowLocal, CooltimeEndCause.Missed, cUtc, t, ct).ConfigureAwait(false);
                if (r == CooltimeEndOutcome.Ended) endedMissed++;
                else if (r == CooltimeEndOutcome.Deadlock) deadlocks++;
            }

            // ③ 묻는다 — 설정 시각 전 로그인 · 안 물음 · (미룸 없음이면 C+5분 전 / 미룸이 있으면 그 순간이 지남)
            //   「②가 아님」 = 미룸이 있거나, 없으면 아직 C+5분 전. 같은 조건을 UPDATE 문장 WHERE 에 다시 건다(심판).
            // 🆕 11/28 ④ 묻는다(설계 §E E-6 · 대표 결재 §287 (가) W = 4) — 설정 시각 뒤 W 시간 안 로그인
            //   (C ≤ login_at < C + W)만 · 묻는 때 = 로그인 + 1시간(미룸이 있으면 그 순간). ③ 과 같은 문장에 OR 한 갈래 ⇒
            //   SELECT · UPDATE 가 같은 WHERE(심판 하나). ② 는 그대로 login_at < C — ④ 로그인을 묻지 않고 끝내지 않는다.
            //   E-2 건너뛰기(위 continue)는 ②③④ 공통 — ④ 도 저장 뒤 첫 C 부터.
            //   ⚠️ 「login_at < C + W」 를 빼면 G-C22 가 FAIL 한다(음성 대조군 ⓙ — 09:00 로그인에 물음).
            //   ⬛ [낡은 문장 · 11/28 이전 ③ 만]
            //     tenant_id = @TenantId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
            //     AND cooltime_asked_at IS NULL
            //     AND login_at < @CUtc
            //     AND ( (cooltime_snooze_until IS NULL AND UTC_TIMESTAMP(6) < @CUtc + INTERVAL 5 MINUTE)
            //        OR (cooltime_snooze_until IS NOT NULL AND cooltime_snooze_until <= UTC_TIMESTAMP(6)) )
            const string askWhere = @"
                     tenant_id = @TenantId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
                     AND cooltime_asked_at IS NULL
                     AND ( ( login_at < @CUtc
                             AND ( (cooltime_snooze_until IS NULL AND UTC_TIMESTAMP(6) < @CUtc + INTERVAL 5 MINUTE)
                                OR (cooltime_snooze_until IS NOT NULL AND cooltime_snooze_until <= UTC_TIMESTAMP(6)) ) )
                        OR ( login_at >= @CUtc
                             AND login_at < @CUtc + INTERVAL @W HOUR
                             AND ( (cooltime_snooze_until IS NULL AND UTC_TIMESTAMP(6) >= login_at + INTERVAL 1 HOUR)
                                OR (cooltime_snooze_until IS NOT NULL AND cooltime_snooze_until <= UTC_TIMESTAMP(6)) ) ) )";
            var toAsk = await _db.QueryAsync<string>(new CommandDefinition(
                "SELECT session_id FROM user_sessions WHERE " + askWhere,
                new { t.TenantId, CUtc = cUtc, W = CooltimeWire.RuleFourWindowHours }, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var sid in toAsk.ToList())
            {
                // 🆕 11/28 HQA 반려 P1(설계 쿨타임_설정끄기경주_고침 §2 · §3 (가) · §4) — 묻기도 한 거래.
                //   ⬛ [낡은 줄 · 11/28 이전] 아래 UPDATE 는 거래 밖 자동 커밋 한 문장이었다(WHERE 에 설정 칸 없음 ⇒ 끈 뒤 오래된 바퀴가 물음을 씀).
                //   첫 문장 = 그 회사 설정 행 공유 잠금 재확인 → 다르면 되돌리고 이 로그인은 묻지 않음 → 같으면 UPDATE → 커밋 → 밀어 보내기(1행일 때만).
                //   잠금 순서 = tenant_settings(공유) → user_sessions(X) = 설정 저장과 같은 순서.
                //   ⚠️ 이 재확인을 빼면 G-C25 ⓑ · ⓓ 가 FAIL 한다(변이 M2).
                IDbTransaction? askTx = null;
                var askTxDone = false;   // 커밋 또는 되돌림을 마쳤다 — 잡기에서 다시 되돌리지 않는다
                try
                {
                    askTx = _db.BeginTransaction();
                    if (!await StillSameSettingAsync(t, askTx, ct).ConfigureAwait(false))
                    {
                        askTx.Rollback();
                        askTxDone = true;
                        _logger.LogInformation("[쿨타임] 설정이 바뀌어 묻기 물러남 — tenant={TenantId} sid={Sid}", t.TenantId, sid);
                        continue;
                    }
                    // ⬛ [낡은 줄 · 11/28 이전] new { t.TenantId, CUtc = cUtc, Sid = sid, W = CooltimeWire.RuleFourWindowHours }, cancellationToken: ct)).ConfigureAwait(false);
                    var n = await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) WHERE session_id = @Sid AND " + askWhere,
                        new { t.TenantId, CUtc = cUtc, Sid = sid, W = CooltimeWire.RuleFourWindowHours },
                        transaction: askTx, cancellationToken: ct)).ConfigureAwait(false);
                    askTx.Commit();
                    askTxDone = true;
                    if (n == 1)
                    {
                        asked++;
                        await _pusher.PromptAsync(t.TenantId, sid, CooltimeWire.PromptSeconds, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 🆕 11/28 — 교착(1213) · 잠금 기다림 초과 등으로 거래가 남았으면 되돌린다(설계 §4 · 지금 이 잡기 안에 되돌림 추가).
                    if (askTx is not null && !askTxDone) TryRollback(askTx, t.TenantId, sid);
                    // #15 — 한 로그인의 실패가 다른 로그인 판정을 막지 않는다. 다음 바퀴가 다시 판정한다.
                    _logger.LogWarning(ex, "[쿨타임] 묻기 실패 — tenant={TenantId} sid={Sid}", t.TenantId, sid);
                }
                finally
                {
                    askTx?.Dispose();   // 취소(OperationCanceledException)로 빠져도 거래가 남지 않는다(커밋 안 된 거래 = 되돌림)
                }
            }
        }

        return Tally();
    }

    // ⬛ [낡은 줄 · 11/28 HQA 반려 P1 이전] 서명 = (tenantId, sid, nowLocal, cause, cUtc, ct) · 본문 = 공개 CooltimeLogoutAsync 호출
    // 🆕 11/28 — 바퀴가 읽은 그 회사 값(expected)을 끝내기 본체에 넘긴다 ⇒ 끝내기 거래 첫 문장이 설정을 다시 확인한다.
    //   서버 작업(① NoAnswer · ② Missed)만 이 길을 탄다. API 끝내기는 공개 CooltimeLogoutAsync(재확인 없음 · 설계 §2 P2).
    private async Task<CooltimeEndOutcome> EndQuietlyAsync(
        string tenantId, string sid, DateTime nowLocal, CooltimeEndCause cause, DateTime? cUtc,
        CooltimeTenantSnapshot expected, CancellationToken ct)
    {
        try
        {
            var r = await CooltimeLogoutCoreAsync(tenantId, sid, nowLocal, cause, jwtUserId: null, cooltimeUtc: cUtc, expected: expected, ct: ct)
                .ConfigureAwait(false);
            return r.Outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // #15 — 한 로그인의 실패가 다른 로그인 판정을 막지 않는다. 다음 바퀴가 다시 판정한다.
            _logger.LogWarning(ex, "[쿨타임] 끝내기 실패 — tenant={TenantId} sid={Sid} cause={Cause}", tenantId, sid, cause);
            return CooltimeEndOutcome.NotEnded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 끝내기 — 함수 하나(설계 §4)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 쿨타임 끝내기 — 한 트랜잭션: ① 세션 행 삭제(심판) ② 그 <c>sid</c> 의 refresh 폐기 ③ 퇴근 기록(§6).
    /// 트랜잭션 밖: 접속기록(<c>audit_trail</c> · 사유) · <c>CooltimeEnded</c> 푸시.
    /// </summary>
    /// <param name="tenantId">서버 작업 = <c>tenant_settings</c> 행 · API = JWT(#2).</param>
    /// <param name="sessionId">서버 작업 = <c>user_sessions</c> 행 · API = JWT <c>sid</c>(#2).</param>
    /// <param name="nowLocal">서버 PC 벽시계 — 물은 순간이 없을 때 퇴근 기준 순간 <c>A</c>(설계 §6 · §D-5).</param>
    /// <param name="cause">누가 불렀나 — 삭제 문장에 덧붙는 조건이 갈린다.</param>
    /// <param name="jwtUserId">API 경로(<see cref="CooltimeEndCause.UserChoice"/>·<see cref="CooltimeEndCause.Countdown"/>)만 — JWT 사용자. 남의 행을 지울 길을 조건으로도 닫는다.</param>
    /// <param name="cooltimeUtc"><see cref="CooltimeEndCause.Missed"/> 만 — 그 회사의 설정 시각 순간 <c>C</c>(UTC). 🆕 11/27 §E E-4: 놓침의 퇴근 기준 순간 <c>A</c> 로도 쓴다.</param>
    /// <param name="ct">취소.</param>
    public Task<CooltimeEndResult> CooltimeLogoutAsync(
        string tenantId,
        string sessionId,
        DateTime nowLocal,
        CooltimeEndCause cause,
        string? jwtUserId = null,
        DateTime? cooltimeUtc = null,
        CancellationToken ct = default)
        // ⬛ [낡은 줄 · 11/28 HQA 반려 P1 이전] public async Task<CooltimeEndResult> CooltimeLogoutAsync(...) { 본문 }
        // 🆕 11/28 — 공개 서명 그대로(이름 · 인자 · 기본값 · 결과 · 반사 게이트 함정 회피). 본문은 아래 비공개 본체로 옮겼다(글자 그대로).
        //   공개 길(API · 시험 직접 호출) = expected 없음 = 설정 재확인 없음 = 지금 동작 그대로(설계 §6 · P2).
        => CooltimeLogoutCoreAsync(tenantId, sessionId, nowLocal, cause, jwtUserId, cooltimeUtc, expected: null, ct);

    /// <summary>
    /// 끝내기 본체 — <see cref="CooltimeLogoutAsync"/> 의 본문을 그대로 옮겼다. 🆕 11/28 <paramref name="expected"/> 가 있으면(서버 작업 ①② 만)
    /// 거래 첫 문장에서 그 회사 설정을 공유 잠금으로 다시 확인한다(설계 쿨타임_설정끄기경주_고침 §3 (가) · §4).
    /// </summary>
    private async Task<CooltimeEndResult> CooltimeLogoutCoreAsync(
        string tenantId,
        string sessionId,
        DateTime nowLocal,
        CooltimeEndCause cause,
        string? jwtUserId,
        DateTime? cooltimeUtc,
        CooltimeTenantSnapshot? expected,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId 필수", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId 필수", nameof(sessionId));
        var isApi = cause is CooltimeEndCause.UserChoice or CooltimeEndCause.Countdown;
        if (isApi && string.IsNullOrWhiteSpace(jwtUserId))
            throw new ArgumentException("API 경로는 JWT 사용자 번호가 필요하다", nameof(jwtUserId));
        if (cause == CooltimeEndCause.Missed && cooltimeUtc is null)
            throw new ArgumentException("놓친 끝내기는 설정 시각 순간이 필요하다", nameof(cooltimeUtc));

        // 1단계 삭제 문장의 원인별 조건 — 심판(설계 §3 · §4 1단계).
        //   ① 무응답: [계속] 문장과 정반대 경계(asked_at <= 지금-65초) ⇒ 둘 중 하나만 1행.
        //   ② 놓침  : 묻기(③)와 겹치지 않는 조건 그대로.
        //   API     : 물음을 받은 로그인만(asked_at IS NOT NULL) + JWT 사용자(남의 행 차단 · 작2 절K 와 같은 모양).
        //   🆕 11/27 고침 차례(§275 · 설계 §E 「물음 만료 → logout ended:false」) — API 는 물음이 살아 있는 65초 안에서만 끝낸다
        //     ([계속] 과 같은 창 · ① 무응답과 정반대 경계 ⇒ API 와 서버 작업 ① 이 같은 행을 두고 겹치지 않는다).
        //     만료 뒤 끝내기 = 서버 작업 ① 몫(15초 안). ⚠️ 이 조건을 빼면 G-C14 ②(만료 뒤 logout = ended:false)가 FAIL 한다(대조군 ⓛ).
        //   ⬛ [낡은 줄 · 11/27 고침 차례 이전] _ => " AND cooltime_asked_at IS NOT NULL AND user_id = @JwtUserId"
        var guard = cause switch
        {
            CooltimeEndCause.NoAnswer =>
                " AND cooltime_asked_at IS NOT NULL AND cooltime_asked_at <= UTC_TIMESTAMP(6) - INTERVAL 65 SECOND",
            CooltimeEndCause.Missed =>
                " AND cooltime_asked_at IS NULL AND cooltime_snooze_until IS NULL AND login_at < @CUtc" +
                " AND UTC_TIMESTAMP(6) >= @CUtc + INTERVAL 5 MINUTE",
            _ => " AND cooltime_asked_at IS NOT NULL AND cooltime_asked_at > UTC_TIMESTAMP(6) - INTERVAL 65 SECOND AND user_id = @JwtUserId"
        };

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        SessionRow? row;
        CooltimeAttendanceOutcome attOutcome;
        string? employeeId = null, attendanceId = null;
        DateTime? written = null;

        using (var tx = _db.BeginTransaction())
        {
            try
            {
                // 🆕 11/28 HQA 반려 P1(설계 쿨타임_설정끄기경주_고침 §3 (가) · §4) — 서버 작업(①②)이면 거래 「첫 문장」 =
                //   그 회사 tenant_settings 행 공유 잠금 재확인(켬 · cooltime_at · cooltime_changed_at 세 값이 바퀴가 읽은 값과 같은가).
                //   다르면(끔 · 시각 바꿈 · 끄고 다시 켬) 아무것도 안 하고 물러난다 = 세션 유지 · 토큰 미폐기 · 근태 무변경.
                //   잠금 순서 = tenant_settings(공유) → user_sessions(X) → refresh_tokens → attendance
                //     = 설정 저장(SettingsService.SaveCooltimeAsync: tenant_settings X → user_sessions X)과 같은 순서 ⇒ 새 순환 0.
                //   🔴 세션 행을 잠근 「뒤」에 설정 행을 잠그면 저장과 반대 순서다(교착) — 반드시 이 자리(첫 문장).
                //   ⚠️ 이 재확인을 빼면 G-C25 ⓐ · ⓒ · G-C26 ⓑ 가 FAIL 한다(변이 M1). 세션 행 잠금 뒤로 옮기면 G-C26 ⓐ 가 FAIL 한다(변이 M5).
                if (expected is not null && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))
                {
                    tx.Rollback();
                    _logger.LogInformation(
                        "[쿨타임] 설정이 바뀌어 끝내기 물러남 — tenant={TenantId} sid={Sid} cause={Cause}", tenantId, sessionId, cause);
                    return NotEnded();
                }

                // 행을 먼저 잠그고(같은 표 · 같은 순서 · 교착 순서는 §D-6 그대로) 사용자 번호·물은 순간을 읽는다.
                //   🆕 11/28 — 서버 작업이면 그 앞에 위 설정 행 공유 잠금이 먼저 있다(설정 저장과 같은 순서).
                row = await _db.QueryFirstOrDefaultAsync<SessionRow>(new CommandDefinition(
                    @"SELECT user_id AS UserId, user_agent AS UserAgent, cooltime_asked_at AS AskedAtUtc
                        FROM user_sessions
                       WHERE session_id = @Sid AND tenant_id = @TenantId
                       FOR UPDATE",
                    new { Sid = sessionId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (row is null)
                {
                    tx.Rollback();
                    return NotEnded();
                }

                // 1 — 심판. 0행이면 여기서 끝(이미 끝남 · [계속]이 이김 · 다른 자리가 먼저 지움 = 퇴근 안 적음 · §D-6 (가)).
                //   ⚠️ `device_kind = 'pc'` 를 빼면 G-C7 이 FAIL 한다(음성 대조군 ⓐ).
                var deleted = await _db.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM user_sessions WHERE session_id = @Sid AND tenant_id = @TenantId AND device_kind = 'pc'" + guard,
                    new { Sid = sessionId, TenantId = tenantId, CUtc = cooltimeUtc, JwtUserId = jwtUserId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (deleted == 0)
                {
                    tx.Rollback();
                    return NotEnded();
                }

                // 2 — 그 로그인의 refresh 만 폐기(작2 DB-130 · 휴대폰 무접촉 Q3).
                //   🔴 localStorage 갈래에서 「다음 날 로그인 화면 없이 되살아나 출근 미기록」 을 막는 주 장치다(설계 §D-1).
                await _db.ExecuteAsync(new CommandDefinition(
                    "UPDATE refresh_tokens SET is_revoked = 1 WHERE user_id = @UserId AND session_id = @Sid",
                    new { row.UserId, Sid = sessionId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 3 — 퇴근 기록(§6). 물은 순간(없으면 끝낸 순간)이 기준 A.
                //   ⚠️ 이 호출을 빼면 G-C8 이 FAIL 한다(음성 대조군 ⓑ).
                //   ⬛ [낡은 문장 · 11/27 §E E-4 이전] var anchorLocal = row.AskedAtUtc is DateTime askedUtc ? UtcToServerLocal(askedUtc) : nowLocal;
                //   🆕 11/27 §E E-4 — 놓침(②)의 A = 그 회사 설정 시각 순간 C(로컬). ② 의 전제가 「C 순간에 로그인이 살아 있었다」.
                //   무응답(①)·API = 물은 순간 그대로. ⚠️ 놓침 A 를 nowLocal 로 되돌리면 G-C21 이 FAIL 한다(음성 대조군 ⓘ).
                var anchorLocal = row.AskedAtUtc is DateTime askedUtc
                    ? UtcToServerLocal(askedUtc)
                    : cause == CooltimeEndCause.Missed && cooltimeUtc is DateTime missedCUtc
                        ? UtcToServerLocal(missedCUtc)
                        : nowLocal;
                (attOutcome, employeeId, attendanceId, written) =
                    await WriteCooltimeCheckOutAsync(tenantId, row.UserId, anchorLocal, tx, ct).ConfigureAwait(false);

                tx.Commit();
            }
            catch (MySqlException ex) when (ex.Number == 1213)
            {
                // §D-6 — 계정 정지·비번 바꿈(refresh → sessions 순)과 엇갈려 교착. 전체 되돌림 · 다음 바퀴가 다시 판정.
                _logger.LogWarning(ex, "[쿨타임] 교착(1213) — 되돌리고 다음 바퀴에 다시 판정. tenant={TenantId} sid={Sid}", tenantId, sessionId);
                TryRollback(tx, tenantId, sessionId);
                return new CooltimeEndResult(CooltimeEndOutcome.Deadlock, null, null, null, CooltimeAttendanceOutcome.NotAttempted, null);
            }
        }

        // 트랜잭션 밖 — 실패해도 끝내기를 되돌리지 않는다(#15 · 경고 로그는 각 함수 안).
        await SessionAccessTrail.WriteAsync(
            _db, tenantId, row.UserId, SessionAccessTrail.ActionLogout, sessionId, "pc", row.UserAgent,
            CooltimeWire.AuditReason(cause)).ConfigureAwait(false);
        await _pusher.EndedAsync(tenantId, sessionId, cause, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[쿨타임] 끝냄 — tenant={TenantId} sid={Sid} cause={Cause} 퇴근={Attendance}",
            tenantId, sessionId, cause, attOutcome);
        return new CooltimeEndResult(CooltimeEndOutcome.Ended, row.UserId, employeeId, attendanceId, attOutcome, written);

        static CooltimeEndResult NotEnded() =>
            new(CooltimeEndOutcome.NotEnded, null, null, null, CooltimeAttendanceOutcome.NotAttempted, null);
    }

    /// <summary>
    /// 퇴근 기록(설계 §6) — 끝내기 트랜잭션 안에서만 부른다. 기존 손 퇴근(<c>CheckOutAsync</c> · 오늘 행만)과 다른 함수다.
    /// </summary>
    private async Task<(CooltimeAttendanceOutcome Outcome, string? EmployeeId, string? AttendanceId, DateTime? Written)>
        WriteCooltimeCheckOutAsync(string tenantId, string userId, DateTime anchorLocal, IDbTransaction tx, CancellationToken ct)
    {
        // §D-3 — 사원 찾기 한 줄(세 경로 공통 · JWT employee_id 안 씀). 0행 = 건너뜀.
        //   🆕 11/28 리뷰 P3-1 — ⚠️ 아래 NoEmployee 반환을 빼면 G-C23 이 FAIL 한다(음성 대조군 ⓧ — 결과가 NoOpenRow 로 바뀜).
        var employeeId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT employee_id FROM employees WHERE tenant_id = @TenantId AND user_id = @UserId",
            new { TenantId = tenantId, UserId = userId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (string.IsNullOrEmpty(employeeId))
        {
            _logger.LogInformation("[쿨타임] 사원 연결 없는 계정 — 퇴근 기록 건너뜀. user={UserId}", userId);
            return (CooltimeAttendanceOutcome.NoEmployee, null, null, null);
        }

        var regularLeave = await _db.QueryFirstOrDefaultAsync<TimeSpan?>(new CommandDefinition(
            "SELECT regular_leave_time FROM tenant_settings WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (regularLeave is not TimeSpan leave)
        {
            _logger.LogInformation("[쿨타임] 정시 퇴근 시각 미설정 — 퇴근 기록 건너뜀. tenant={TenantId}", tenantId);
            return (CooltimeAttendanceOutcome.NoRegularLeaveTime, employeeId, null, null);
        }

        // 붙일 행: 퇴근 빈칸 · 출근이 A 이전이고 A 의 24시간 이내 · 가장 최근 1줄(자정 넘김 = 출근한 날 행).
        //   이미 퇴근이 있는 행은 고르지 않는다(C-3 — 사람이 만든 기록을 자동이 덮지 않는다).
        var open = await _db.QueryFirstOrDefaultAsync<OpenAttendanceRow>(new CommandDefinition(
            @"SELECT attendance_id AS AttendanceId, check_in AS CheckIn
                FROM attendance
               WHERE tenant_id = @TenantId AND employee_id = @EmployeeId
                 AND check_out IS NULL AND check_in IS NOT NULL
                 AND check_in <= @Anchor AND check_in >= @AnchorFrom
               ORDER BY check_in DESC
               LIMIT 1
               FOR UPDATE",
            new { TenantId = tenantId, EmployeeId = employeeId, Anchor = anchorLocal, AnchorFrom = anchorLocal.AddHours(-24) },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (open is null)
        {
            _logger.LogInformation("[쿨타임] 붙일 출근 행 없음 — 퇴근 기록 건너뜀. employee={EmployeeId}", employeeId);
            return (CooltimeAttendanceOutcome.NoOpenRow, employeeId, null, null);
        }

        var leaveAt = FirstRegularLeaveAfter(open.CheckIn, leave);
        if (leaveAt > anchorLocal)
        {
            // C-2 (다) — 아직 안 온 시각은 적지 않는다. 퇴근 빈칸 그대로 · 표시만 남겨 인사담당이 찾게(§D-2).
            await _db.ExecuteAsync(new CommandDefinition(
                @"UPDATE attendance SET check_out_source = 'cooltime_blank'
                   WHERE attendance_id = @Id AND tenant_id = @TenantId AND check_out IS NULL",
                new { Id = open.AttendanceId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            return (CooltimeAttendanceOutcome.LeftBlank, employeeId, open.AttendanceId, null);
        }

        await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE attendance SET check_out = @CheckOut, work_hours = @Hours, check_out_source = 'cooltime'
               WHERE attendance_id = @Id AND tenant_id = @TenantId AND check_out IS NULL",
            new { CheckOut = leaveAt, Hours = WorkHours(open.CheckIn, leaveAt), Id = open.AttendanceId, TenantId = tenantId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return (CooltimeAttendanceOutcome.WroteRegularLeave, employeeId, open.AttendanceId, leaveAt);
    }

    // ═══════════════════════════════════════════════════════════════════
    // [계속] · 상태 — API 가 부른다(테넌트·sid·사용자 = JWT · #2)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// [계속] — 한 문장(설계 §3). 1행 = 1시간 미룸 · 0행 = 이미 끝남(호출자가 409).
    /// ⚠️ <c>cooltime_asked_at &gt; UTC_TIMESTAMP(6) - INTERVAL 65 SECOND</c> 를 빼면 G-C6 이 FAIL 한다(음성 대조군 ⓒ).
    /// </summary>
    public async Task<bool> ContinueAsync(string tenantId, string sessionId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var n = await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE user_sessions
                 SET cooltime_asked_at = NULL,
                     cooltime_snooze_until = UTC_TIMESTAMP(6) + INTERVAL 1 HOUR
               WHERE session_id = @Sid AND tenant_id = @TenantId AND user_id = @UserId
                 AND cooltime_asked_at IS NOT NULL
                 AND cooltime_asked_at > UTC_TIMESTAMP(6) - INTERVAL 65 SECOND",
            new { Sid = sessionId, TenantId = tenantId, UserId = userId }, cancellationToken: ct)).ConfigureAwait(false);
        // 🆕 11/27 고침 차례(§273 V1) — 이긴 [계속] 만 「계속됨」 을 그 로그인 그룹에 알린다(같은 출입증을 쓰는 다른 탭이 팝업을 닫는다).
        //   푸시 실패는 예외로 오르지 않는다(구현체가 경고 1줄) — [계속] 결과를 되돌리지 않는다.
        //   ⚠️ 이 줄을 빼면 G-C14 ①(계속됨 푸시 1회)이 FAIL 한다(대조군 ⓜ).
        if (n == 1) await _pusher.ContinuedAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        return n == 1;
    }

    /// <summary>놓친 물음 확인(화면 재연결 때 · 설계 §5-2). 행이 없으면 안 물음으로 답한다.</summary>
    public async Task<CooltimeStateResponse> GetStateAsync(string tenantId, string sessionId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var s = await _db.QueryFirstOrDefaultAsync<StateRow>(new CommandDefinition(
            @"SELECT CASE WHEN cooltime_asked_at IS NULL THEN 0 ELSE 1 END AS Asked,
                     COALESCE(GREATEST(0, 60 - TIMESTAMPDIFF(SECOND, cooltime_asked_at, UTC_TIMESTAMP(6))), 0) AS SecondsLeft,
                     cooltime_snooze_until AS SnoozeUntilUtc
                FROM user_sessions
               WHERE session_id = @Sid AND tenant_id = @TenantId AND user_id = @UserId",
            new { Sid = sessionId, TenantId = tenantId, UserId = userId }, cancellationToken: ct)).ConfigureAwait(false);
        // ⬛ [낡은 줄 · 11/27 고침 차례 이전] if (s is null) return new CooltimeStateResponse(false, 0, null);
        // 🆕 11/27 고침 차례(§275 E-5) — 행 없음 = active:false(지운 직후 캐시 10초 틈에서 「살아 있음」 으로 잘못 읽지 않게).
        if (s is null) return new CooltimeStateResponse(false, 0, null, Active: false);
        return new CooltimeStateResponse(s.Asked != 0, (int)Math.Clamp(s.SecondsLeft, 0, CooltimeWire.PromptSeconds), s.SnoozeUntilUtc);
    }

    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🆕 11/28 HQA 반려 P1(설계 <c>docs/설계/erp/쿨타임_설정끄기경주_고침_설계_20261128.md</c> §3 (가)) — 서버 작업 쓰기 거래의 첫 문장.
    /// 그 회사 <c>tenant_settings</c> 행을 공유 잠금으로 다시 읽어 켬 = 1 · <c>cooltime_at</c> · <c>cooltime_changed_at</c> 이
    /// 바퀴가 읽은 값과 같으면(<c>&lt;=&gt;</c> · NULL 안전 · 비교는 SQL 안) <c>true</c>. 행 없음 · 하나라도 다름 = <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>공유 잠금인 까닭(설계 §1 꼴 B): 설정 저장 거래가 그 행을 <c>FOR UPDATE</c> 로 쥐고 있으면 여기서 기다렸다가
    /// 커밋된 새 값을 본다. 잠그지 않는 읽기는 저장 커밋 전 옛 값을 보고 지나간다 ⇒ ⚠️ 잠금을 빼면 G-C26 ⓑ 가 FAIL 한다(변이 M4).</para>
    /// <para>비교 칸 세 값 = 판 번호 몫: 끔 → 켬 값 · 시각 바꿈 → <c>cooltime_at</c> · <c>cooltime_changed_at</c> · 끄고 다시 켬 → <c>cooltime_changed_at</c>.
    /// 같은 값 다시 저장은 <c>cooltime_changed_at</c> 이 그대로라 <c>true</c>(헛물러남 0 · G-C25 ⓕ).
    /// ⚠️ 비교를 켬 하나로 줄이면 G-C25 ⓒ · ⓓ 가 FAIL 한다(변이 M3).</para>
    /// <para>칸 형(출하 DDL <c>installer/hitpan_db_clean.sql</c> <c>tenant_settings</c> · #13): <c>tenant_id</c> varchar(36) 기본 키 ·
    /// <c>cooltime_enabled</c> tinyint(1) · <c>cooltime_at</c> time · <c>cooltime_changed_at</c> datetime(6).</para>
    /// </remarks>
    private async Task<bool> StillSameSettingAsync(CooltimeTenantSnapshot expected, IDbTransaction tx, CancellationToken ct)
    {
        var hit = await _db.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT 1 FROM tenant_settings WHERE tenant_id = @TenantId AND cooltime_enabled = 1" +
            " AND cooltime_at <=> @At AND cooltime_changed_at <=> @Changed LOCK IN SHARE MODE",
            new { expected.TenantId, At = expected.CooltimeAt, Changed = expected.ChangedAtUtc },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return hit == 1;
    }

    private void TryRollback(IDbTransaction tx, string tenantId, string sessionId)
    {
        try { tx.Rollback(); }
        catch (Exception ex)
        {
            // #15 — 교착 뒤 서버가 이미 되돌렸으면 여기서 실패할 수 있다. 결과는 같다(반영 0).
            _logger.LogWarning(ex, "[쿨타임] 되돌림 호출 실패(이미 되돌려졌을 수 있음) — tenant={TenantId} sid={Sid}", tenantId, sessionId);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is System.Data.Common.DbConnection c) await c.OpenAsync(ct).ConfigureAwait(false);
        else _db.Open();
    }

    private sealed class TenantCooltimeRow
    {
        public string TenantId { get; set; } = string.Empty;
        public TimeSpan CooltimeAt { get; set; }
        public DateTime? ChangedAtUtc { get; set; }   // 🆕 11/27 §E E-2
    }

    private sealed class SessionRow
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
        public DateTime? AskedAtUtc { get; set; }
    }

    private sealed class OpenAttendanceRow
    {
        public string AttendanceId { get; set; } = string.Empty;
        public DateTime CheckIn { get; set; }
    }

    private sealed class StateRow
    {
        public long Asked { get; set; }
        public long SecondsLeft { get; set; }
        public DateTime? SnoozeUntilUtc { get; set; }
    }
}
