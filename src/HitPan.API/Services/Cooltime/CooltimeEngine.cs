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

    /// <summary>근무시간 — 기존 손 퇴근과 같은 식(<c>HrService.CheckOutAsync</c> · 소수 1자리).</summary>
    public static double WorkHours(DateTime checkIn, DateTime checkOut)
        => Math.Round((checkOut - checkIn).TotalHours, 1);

    // ═══════════════════════════════════════════════════════════════════
    // 한 바퀴 — 설계 §3 규칙 ①②③ (④ 는 개발명세서 「못 한 것」 — 결재 대기)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 서버 작업 한 바퀴. 켜진 회사마다 ① 무응답 끝내기 → ② 놓친 끝내기 → ③ 묻기 순서로 돈다.
    /// </summary>
    /// <param name="nowLocal">서버 PC 벽시계(운영 = <c>DateTime.Now</c>) — <c>C</c> 계산 · 퇴근 기록에만 쓴다. 시험이 주입한다.</param>
    public async Task<CooltimeTickResult> RunOnceAsync(DateTime nowLocal, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 켜진 회사가 없으면 한 바퀴 = 이 가벼운 조회 1회(설계 §2). 행 없는 회사 = 꺼짐.
        var tenants = (await _db.QueryAsync<TenantCooltimeRow>(new CommandDefinition(
            @"SELECT tenant_id AS TenantId, cooltime_at AS CooltimeAt
                FROM tenant_settings
               WHERE cooltime_enabled = 1 AND cooltime_at IS NOT NULL",
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        int asked = 0, endedNoAnswer = 0, endedMissed = 0, deadlocks = 0;

        foreach (var t in tenants)
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
                var r = await EndQuietlyAsync(t.TenantId, sid, nowLocal, CooltimeEndCause.NoAnswer, null, ct).ConfigureAwait(false);
                if (r == CooltimeEndOutcome.Ended) endedNoAnswer++;
                else if (r == CooltimeEndOutcome.Deadlock) deadlocks++;
            }

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
                var r = await EndQuietlyAsync(t.TenantId, sid, nowLocal, CooltimeEndCause.Missed, cUtc, ct).ConfigureAwait(false);
                if (r == CooltimeEndOutcome.Ended) endedMissed++;
                else if (r == CooltimeEndOutcome.Deadlock) deadlocks++;
            }

            // ③ 묻는다 — 설정 시각 전 로그인 · 안 물음 · (미룸 없음이면 C+5분 전 / 미룸이 있으면 그 순간이 지남)
            //   「②가 아님」 = 미룸이 있거나, 없으면 아직 C+5분 전. 같은 조건을 UPDATE 문장 WHERE 에 다시 건다(심판).
            const string askWhere = @"
                     tenant_id = @TenantId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
                     AND cooltime_asked_at IS NULL
                     AND login_at < @CUtc
                     AND ( (cooltime_snooze_until IS NULL AND UTC_TIMESTAMP(6) < @CUtc + INTERVAL 5 MINUTE)
                        OR (cooltime_snooze_until IS NOT NULL AND cooltime_snooze_until <= UTC_TIMESTAMP(6)) )";
            var toAsk = await _db.QueryAsync<string>(new CommandDefinition(
                "SELECT session_id FROM user_sessions WHERE " + askWhere,
                new { t.TenantId, CUtc = cUtc }, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var sid in toAsk.ToList())
            {
                try
                {
                    var n = await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) WHERE session_id = @Sid AND " + askWhere,
                        new { t.TenantId, CUtc = cUtc, Sid = sid }, cancellationToken: ct)).ConfigureAwait(false);
                    if (n == 1)
                    {
                        asked++;
                        await _pusher.PromptAsync(t.TenantId, sid, CooltimeWire.PromptSeconds, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // #15 — 한 로그인의 실패가 다른 로그인 판정을 막지 않는다. 다음 바퀴가 다시 판정한다.
                    _logger.LogWarning(ex, "[쿨타임] 묻기 실패 — tenant={TenantId} sid={Sid}", t.TenantId, sid);
                }
            }
        }

        return new CooltimeTickResult(tenants.Count, asked, endedNoAnswer, endedMissed, deadlocks);
    }

    private async Task<CooltimeEndOutcome> EndQuietlyAsync(
        string tenantId, string sid, DateTime nowLocal, CooltimeEndCause cause, DateTime? cUtc, CancellationToken ct)
    {
        try
        {
            var r = await CooltimeLogoutAsync(tenantId, sid, nowLocal, cause, jwtUserId: null, cooltimeUtc: cUtc, ct: ct)
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
    /// <param name="cooltimeUtc"><see cref="CooltimeEndCause.Missed"/> 만 — 그 회사의 설정 시각 순간 <c>C</c>(UTC).</param>
    /// <param name="ct">취소.</param>
    public async Task<CooltimeEndResult> CooltimeLogoutAsync(
        string tenantId,
        string sessionId,
        DateTime nowLocal,
        CooltimeEndCause cause,
        string? jwtUserId = null,
        DateTime? cooltimeUtc = null,
        CancellationToken ct = default)
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
        var guard = cause switch
        {
            CooltimeEndCause.NoAnswer =>
                " AND cooltime_asked_at IS NOT NULL AND cooltime_asked_at <= UTC_TIMESTAMP(6) - INTERVAL 65 SECOND",
            CooltimeEndCause.Missed =>
                " AND cooltime_asked_at IS NULL AND cooltime_snooze_until IS NULL AND login_at < @CUtc" +
                " AND UTC_TIMESTAMP(6) >= @CUtc + INTERVAL 5 MINUTE",
            _ => " AND cooltime_asked_at IS NOT NULL AND user_id = @JwtUserId"
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
                // 행을 먼저 잠그고(같은 표 · 같은 순서 · 교착 순서는 §D-6 그대로) 사용자 번호·물은 순간을 읽는다.
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
                var anchorLocal = row.AskedAtUtc is DateTime askedUtc ? UtcToServerLocal(askedUtc) : nowLocal;
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
        if (s is null) return new CooltimeStateResponse(false, 0, null);
        return new CooltimeStateResponse(s.Asked != 0, (int)Math.Clamp(s.SecondsLeft, 0, CooltimeWire.PromptSeconds), s.SnoozeUntilUtc);
    }

    // ═══════════════════════════════════════════════════════════════════

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
