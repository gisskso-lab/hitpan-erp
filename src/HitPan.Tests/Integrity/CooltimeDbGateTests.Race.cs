using System.Diagnostics;
using Dapper;
using HitPan.API.Services.Cooltime;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🆕 2026-11-28 쿨타임 1-b HQA 반려 P1 — <b>G-C25 ⓐ ~ ⓖ</b>(설정 끄기 · 시각 바꾸기 경주 · 순서 강제 · 🆕 고침 2 <b>ⓗ</b> 끄고 바로 다시 켬) ·
/// <b>G-C26 ⓐ ⓑ</b>(잠금 순서 · 교착 0)를 <b>실제 DB</b>로 잰다.
/// </summary>
/// <remarks>
/// <para>근거: 설계 <c>docs/설계/erp/쿨타임_설정끄기경주_고침_설계_20261128.md</c> §6 ·
/// 작업지시서 <c>docs/운영기록/20261128작_쿨타임1b_HQA반려P1_설정끄기경주_고침_작업지시서.md</c> §4 ·
/// HQA 판정 기준 = 「설정 끄기 커밋이 먼저 끝나면 세션 유지 · 토큰 미폐기 · 근태 무변경」.</para>
/// <para>🔴 순서는 시간이 아니라 호출 차례와 잠금 조건으로 세운다 — 지연 · 대리자 갈고리 0.
/// G-C25 = <see cref="CooltimeEngine.ReadTenantsAsync"/> → <b>실물</b> <see cref="SettingsService.SaveAsync"/> 커밋 →
/// <see cref="CooltimeEngine.RunTenantAsync"/>(먼저 읽은 값). G-C26 = 지키는 연결이 세션 행을 쥐고,
/// 기다림은 <c>information_schema.INNODB_TRX</c> 의 그 연결 번호 <c>trx_state = 'LOCK WAIT'</c> 로 확인(상한 10초 넘으면 FAIL · 건너뜀 아님).</para>
/// <para>🔴 판정은 제품 공개 메서드가 한다 — 이 파일의 SQL 은 준비 · 읽기 · 잠금 쥐기 · 기다림 관찰뿐.
/// 연결은 일마다 따로 연다(한 연결 동시 사용 0 · #16 · Task.WhenAll 0).</para>
/// <para>이 PC(개발 PC)는 3306 = 실물 DB 라 돌리지 않는다 — CI <c>db-gate</c> 잡(<c>HITPAN_REQUIRE_DB</c>)이 계측 경로(SKIP = FAIL).</para>
/// </remarks>
public sealed partial class CooltimeDbGateTests
{
    // ══════════════════════════════════════════════════════════════
    // 준비물 — 판정 0
    // ══════════════════════════════════════════════════════════════

    /// <summary>경주 준비 한 벌 — 회사 켬 · PC 세션 1 · 그 sid 의 refresh 1(미폐기) · 사원 연결 · 열린 출근 행 1.</summary>
    private sealed record RaceSetup(string UserId, string Sid, string RefreshId, string AttendanceId, DateTime NowLocal, DateTime CLocal);

    private static UpdateTenantSettingsDto CooltimeDto(bool on, string at) =>
        new() { CooltimeEnabled = on, CooltimeAt = at, RegularLeaveTime = "18:00" };

    /// <summary>
    /// ② 놓침 조건 참 — G-C11 꼴: 22:00 회사(기록 없는 옛 행 · <c>cooltime_changed_at</c> NULL) · 이틀 전 D 09:00 출근 · D 09:00 PC 로그인.
    /// 돌릴 <c>nowLocal</c> = D+1 08:00 ⇒ C = D 22:00 · DB 시계로 C + 5분 지남.
    /// </summary>
    private async Task<RaceSetup> MissedSetupAsync(MySqlConnection db, string tenantId)
    {
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, tenantId);
        var emp = await InsertEmployeeAsync(db, tenantId, u);
        await SettingsAsync(db, tenantId, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, tenantId, emp, d.AddHours(9));
        var sid = await InsertSessionAsync(db, tenantId, u, "pc", loginUtc: Utc(d.AddHours(9)));
        var rt = await InsertRefreshAsync(db, u, sid);
        return new RaceSetup(u, sid, rt, att, d.AddDays(1).AddHours(8), d.AddHours(22));
    }

    /// <summary>
    /// ③ 묻기 조건 참 — G-C2 꼴: 설정 시각 C = DB 시계 기준 「지금 − 15초」 를 분 단위로 내린 값(저장 화면의 HH:mm 과 같은 모양) ·
    /// 10분 전 PC 로그인 · 안 물음. 돌릴 <c>nowLocal</c> = 지금(C + 15 ~ 75초 · C + 5분 전).
    /// </summary>
    private async Task<RaceSetup> AskSetupAsync(MySqlConnection db, string tenantId)
    {
        var now = await DbNowLocalAsync(db);
        var c15 = now.AddSeconds(-15);
        var c = new DateTime(c15.Year, c15.Month, c15.Day, c15.Hour, c15.Minute, 0);
        var u = await InsertUserAsync(db, tenantId);
        var emp = await InsertEmployeeAsync(db, tenantId, u);
        await SettingsAsync(db, tenantId, true, new TimeSpan(c.Hour, c.Minute, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, tenantId, emp, now.AddHours(-3));
        var sid = await InsertSessionAsync(db, tenantId, u, "pc", loginAgoSec: 600);
        var rt = await InsertRefreshAsync(db, u, sid);
        return new RaceSetup(u, sid, rt, att, now, c);
    }

    private static async Task<CooltimeTenantSnapshot> SnapshotAsync(CooltimeEngine engine, string tenantId) =>
        (await engine.ReadTenantsAsync()).Single(x => x.TenantId == tenantId);

    /// <summary>HQA 판정 기준 세 값 + 접속기록 · 푸시 — 「끝내지 않았다」 를 직접 단언한다.</summary>
    private static async Task AssertNotEndedAsync(MySqlConnection db, CountingPusher pusher, RaceSetup s, string label)
    {
        Assert.True(await SessionExistsAsync(db, s.Sid) == 1, $"{label}: 세션 행이 지워졌다(세션 유지여야 함)");
        Assert.True(await RevokedAsync(db, s.RefreshId) == 0, $"{label}: refresh 가 폐기됐다(토큰 미폐기여야 함)");
        var a = await AttAsync(db, s.AttendanceId);
        Assert.True(a.CheckOut is null, $"{label}: 근태 check_out 이 적혔다({a.CheckOut}) — 근태 무변경이어야 함");
        Assert.True(a.Source is null, $"{label}: 근태 check_out_source 가 바뀌었다({a.Source}) — 근태 무변경이어야 함");
        Assert.Equal(0, await LogoutTrailCountAsync(db, s.Sid));
        Assert.DoesNotContain(pusher.Ended, e => e.Sid == s.Sid);
    }

    /// <summary>양성 대조 — 끝냈다(세션 0 · 폐기 1 · 근태 18:00 cooltime · 접속기록 1 · 끝남 푸시 1).</summary>
    private static async Task AssertEndedMissedAsync(MySqlConnection db, CountingPusher pusher, RaceSetup s, string tenantId, CooltimeTickResult r, string label)
    {
        Assert.True(r.EndedMissed == 1, $"{label}: 끝냄 {r.EndedMissed} — 1 이어야 한다(시험이 빨강을 낼 수 있음을 보이는 짝)");
        Assert.Equal(0, await SessionExistsAsync(db, s.Sid));
        Assert.Equal(1, await RevokedAsync(db, s.RefreshId));
        var a = await AttAsync(db, s.AttendanceId);
        Assert.Equal(s.CLocal.Date.AddHours(18), a.CheckOut);
        Assert.Equal(CooltimeWire.SourceCooltime, a.Source);
        Assert.Equal(1, await LogoutTrailCountAsync(db, s.Sid));
        Assert.Contains((tenantId, s.Sid, CooltimeEndCause.Missed), pusher.Ended);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C25 — 순서 강제 경주: 읽기 → 실물 저장 커밋 → 회사 한 곳 돌기(먼저 읽은 값)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C25 ⓐ 읽기 → 저장(끔) 커밋 → 오래된 값으로 ② 놓침 = 끝냄 0 · 세션 유지 · refresh 미폐기 · 근태 무변경 · 접속기록 0 · 푸시 0")]
    public async Task G_C25a_끈_뒤_오래된_바퀴는_놓침으로_끝내지_않는다()
    {
        if (!Ready("G-C25 ⓐ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(false, "22:00"), _tenantA);
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        Assert.Equal(0, r.EndedMissed);
        Assert.Equal(0, r.EndedNoAnswer);
        Assert.Equal(0, r.Asked);
        await AssertNotEndedAsync(db, pusher, s, "G-C25 ⓐ");
        Assert.Empty(pusher.Prompts);
    }

    [Fact(DisplayName = "G-C25 ⓑ 읽기 → 저장(끔) 커밋 → 오래된 값으로 ③ 묻기 = 물음 0 · asked_at NULL · Prompt 푸시 0 · 세션 유지")]
    public async Task G_C25b_끈_뒤_오래된_바퀴는_묻지_않는다()
    {
        if (!Ready("G-C25 ⓑ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await AskSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(false, s.CLocal.ToString("HH:mm")), _tenantA);
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        Assert.Equal(0, r.Asked);
        Assert.Null(await AskedAtAsync(db, s.Sid));
        Assert.Empty(pusher.Prompts);
        await AssertNotEndedAsync(db, pusher, s, "G-C25 ⓑ");
    }

    [Fact(DisplayName = "G-C25 ⓒ 읽기 → 저장(켬 그대로 · 시각 바꿈) 커밋 → 오래된 값으로 ② 놓침 = ⓐ 와 같음(끝냄 0 · 세 값 무변경)")]
    public async Task G_C25c_시각_바꾼_뒤_오래된_바퀴는_놓침으로_끝내지_않는다()
    {
        if (!Ready("G-C25 ⓒ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(true, "21:00"), _tenantA);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT CAST(cooltime_enabled AS SIGNED) FROM tenant_settings WHERE tenant_id = @T", new { T = _tenantA }));   // 준비 확인 — 켬 그대로
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        Assert.Equal(0, r.EndedMissed);
        Assert.Equal(0, r.Asked);
        await AssertNotEndedAsync(db, pusher, s, "G-C25 ⓒ");
    }

    [Fact(DisplayName = "G-C25 ⓓ 읽기 → 저장(켬 그대로 · 시각 바꿈) 커밋 → 오래된 값으로 ③ 묻기 = ⓑ 와 같음(물음 0 · Prompt 0)")]
    public async Task G_C25d_시각_바꾼_뒤_오래된_바퀴는_묻지_않는다()
    {
        if (!Ready("G-C25 ⓓ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await AskSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(true, s.CLocal.AddMinutes(-1).ToString("HH:mm")), _tenantA);
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        Assert.Equal(0, r.Asked);
        Assert.Null(await AskedAtAsync(db, s.Sid));
        Assert.Empty(pusher.Prompts);
        await AssertNotEndedAsync(db, pusher, s, "G-C25 ⓓ");
    }

    [Fact(DisplayName = "G-C25 ⓔ 양성 대조 — 사이 저장 없음 → 같은 준비 ② 놓침 = 끝냄 1 · 세션 0 · 폐기 1 · 근태 18:00 기록")]
    public async Task G_C25e_양성대조_저장_없으면_끝낸다()
    {
        if (!Ready("G-C25 ⓔ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        await AssertEndedMissedAsync(db, pusher, s, _tenantA, r, "G-C25 ⓔ");
    }

    [Fact(DisplayName = "G-C25 ⓕ 헛물러남 대조 — 같은 값 다시 저장(cooltime_changed_at 그대로) → ② 놓침 = 끝냄 1(비교가 지나치게 예민하지 않음)")]
    public async Task G_C25f_같은_값_다시_저장은_물러나지_않는다()
    {
        if (!Ready("G-C25 ⓕ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(true, "22:00"), _tenantA);
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT cooltime_changed_at FROM tenant_settings WHERE tenant_id = @T", new { T = _tenantA }));   // 준비 확인 — 같은 값 저장은 변경 시각을 안 바꾼다
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        await AssertEndedMissedAsync(db, pusher, s, _tenantA, r, "G-C25 ⓕ");
    }

    [Fact(DisplayName = "G-C25 ⓖ 읽기 → 저장(시각 바꿈) 커밋 → 오래된 값으로 ① 무응답(물음 70초 전) = 끝냄 0 → 새 한 바퀴(RunOnceAsync) = 끝냄 1(E-2 유지 · 한 바퀴 늦음)")]
    public async Task G_C25g_시각_바꾼_뒤_무응답은_한_바퀴_늦게_끝낸다()
    {
        if (!Ready("G-C25 ⓖ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 70);
        var rt = await InsertRefreshAsync(db, u, sid);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(true, "21:00"), _tenantA);
        Assert.NotNull(await AskedAtAsync(db, sid));                                   // 준비 확인 — 켬 그대로면 저장이 물음 칸을 안 비운다
        var old = await engine.RunTenantAsync(snap, await DbNowLocalAsync(db));
        Assert.Equal(0, old.EndedNoAnswer);
        Assert.Equal(1, await SessionExistsAsync(db, sid));
        Assert.Equal(0, await RevokedAsync(db, rt));
        Assert.DoesNotContain(pusher.Ended, e => e.Sid == sid);

        // 새 바퀴 — 새 값을 읽는다 ⇒ ① 은 E-2 건너뛰기 앞이라 떠 있던 물음의 무응답을 끝낸다(설계 §5 · 최종 결과 같음)
        var fresh = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(1, fresh.EndedNoAnswer);
        Assert.Equal(0, await SessionExistsAsync(db, sid));
        Assert.Equal(1, await RevokedAsync(db, rt));
        Assert.Contains((_tenantA, sid, CooltimeEndCause.NoAnswer), pusher.Ended);
    }

    // 🆕 11/28 고침 2([4] P2-1) — 「끄고 바로 다시 켬(시각 같음)」. 켬 · cooltime_at 은 읽은 값과 같아지고
    //   cooltime_changed_at 만 바뀐다(저장 UPSERT 의 CASE 가 0→1 에서 새로 적음) ⇒ 재확인의 그 칸 비교만 이 경주를 막는다.
    //   ⓒ ⓓ 는 cooltime_at 도 바꾸므로 그 칸 비교만으로 빨개진다 — 이 시험이 없으면 changed_at 비교를 빼도(변이 M6) 아무것도 안 빨개진다.
    [Fact(DisplayName = "G-C25 ⓗ 읽기 → 저장(끔) 커밋 → 저장(켬 · 같은 시각) 커밋 → 오래된 값으로 ② 놓침 = 끝냄 0 · 세션 유지 · refresh 미폐기 · 근태 무변경(cooltime_changed_at 이 바뀌어 물러남)")]
    public async Task G_C25h_끄고_바로_다시_켠_뒤_오래된_바퀴는_놓침으로_끝내지_않는다()
    {
        if (!Ready("G-C25 ⓗ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        var snap = await SnapshotAsync(engine, _tenantA);
        Assert.Null(snap.ChangedAtUtc);                                                // 준비 확인 — 읽은 값 = 기록 없는 옛 행(NULL)
        await new SettingsService(db).SaveAsync(CooltimeDto(false, "22:00"), _tenantA);
        await new SettingsService(db).SaveAsync(CooltimeDto(true, "22:00"), _tenantA);
        var after = await db.QueryFirstAsync<(long On, TimeSpan? At, DateTime? Changed)>(
            "SELECT CAST(cooltime_enabled AS SIGNED), cooltime_at, cooltime_changed_at FROM tenant_settings WHERE tenant_id = @T",
            new { T = _tenantA });
        // 준비 확인 — 켬 · 시각은 읽은 값과 같고 변경 시각만 새로 적혔다(이 칸 하나가 다른 경주임을 보인다)
        Assert.Equal(1, after.On);
        Assert.Equal<TimeSpan?>(snap.CooltimeAt, after.At);
        Assert.NotNull(after.Changed);
        var r = await engine.RunTenantAsync(snap, s.NowLocal);

        Assert.Equal(0, r.EndedMissed);
        Assert.Equal(0, r.EndedNoAnswer);
        Assert.Equal(0, r.Asked);
        await AssertNotEndedAsync(db, pusher, s, "G-C25 ⓗ");
        Assert.Empty(pusher.Prompts);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C26 — 잠금 순서 · 교착 0 (지키는 연결 L 이 세션 행을 쥐고 · 기다림 = INNODB_TRX 조건)
    // ══════════════════════════════════════════════════════════════

    private static Task<long> ThreadIdAsync(MySqlConnection c) => c.ExecuteScalarAsync<long>("SELECT CONNECTION_ID()");

    /// <summary>
    /// 주어진 연결 번호들이 <b>전부</b> InnoDB 잠금 기다림(<c>trx_state = 'LOCK WAIT'</c>)에 들어갈 때까지 본다 — 시간이 아니라 조건.
    /// 10초 안에 안 되면 FAIL(건너뜀 아님 · 지시서 §7 W2) · 그때 INNODB_TRX 를 그대로 남긴다(원인 찾기).
    /// </summary>
    private async Task WaitLockWaitAsync(MySqlConnection observer, string step, params long[] threadIds)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var waiting = await observer.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_state = 'LOCK WAIT' AND trx_mysql_thread_id IN @Ids",
                new { Ids = threadIds });
            if (waiting == threadIds.Length)
            {
                _out.WriteLine($"[G-C26] {step} — 잠금 기다림 {waiting}/{threadIds.Length} 확인({sw.ElapsedMilliseconds}ms)");
                return;
            }
            if (sw.Elapsed > TimeSpan.FromSeconds(10))
            {
                var rows = await observer.QueryAsync<(long Thread, string State, string? Query)>(
                    "SELECT CAST(trx_mysql_thread_id AS SIGNED), trx_state, trx_query FROM information_schema.INNODB_TRX");
                throw new Xunit.Sdk.XunitException(
                    $"[G-C26] {step} — 10초 안에 잠금 기다림 {threadIds.Length} 을 못 봤다(본 수 {waiting}). 건너뜀이 아니라 FAIL 이다. INNODB_TRX = "
                    + string.Join(" / ", rows.Select(x => $"{x.Thread}:{x.State}:{x.Query}")));
            }
            await Task.Delay(50);
        }
    }

    [Fact(DisplayName = "G-C26 ⓐ 엔진 먼저 — L 이 세션 행 쥠 → 엔진(② 참) 세션 행 기다림 → 저장(끔) 설정 행 기다림 → L 되돌림 = 저장 예외 0(1213 0) · 엔진 끝냄 1 · 교착 0 · 남은 PC 세션 물음 NULL")]
    public async Task G_C26a_엔진이_먼저_잡으면_끝내고_저장은_기다린다()
    {
        if (!Ready("G-C26 ⓐ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);
        // 남는 PC 세션 — 물음 10초 전 · C 뒤 로그인(①②③④ 아무것도 아님) ⇒ 저장(끔)이 물음 칸을 비우는지 본다
        var u2 = await InsertUserAsync(db, _tenantA);
        var other = await InsertSessionAsync(db, _tenantA, u2, "pc", loginAgoSec: 600, askedAgoSec: 10);

        await using var cEngine = await OpenAsync();
        await using var cSave = await OpenAsync();
        await using var cHold = await OpenAsync();
        var engineThread = await ThreadIdAsync(cEngine);
        var saveThread = await ThreadIdAsync(cSave);
        var engine = Engine(cEngine, pusher);
        var snap = await SnapshotAsync(engine, _tenantA);

        await using var hold = await cHold.BeginTransactionAsync();
        await cHold.QueryAsync<string>("SELECT session_id FROM user_sessions WHERE session_id = @S FOR UPDATE", new { S = s.Sid }, hold);

        var tEngine = engine.RunTenantAsync(snap, s.NowLocal);
        await WaitLockWaitAsync(db, "ⓐ 기다림 1 — 엔진이 세션 행을 기다림", engineThread);
        var tSave = new SettingsService(cSave).SaveAsync(CooltimeDto(false, "22:00"), _tenantA);
        await WaitLockWaitAsync(db, "ⓐ 기다림 2 — 저장이 설정 행을 기다림(엔진이 먼저 공유 잠금)", engineThread, saveThread);
        await hold.RollbackAsync();

        var r = await tEngine;
        var saveError = await Record.ExceptionAsync(() => tSave);
        Assert.True(saveError is null, $"G-C26 ⓐ: 저장이 실패했다 — {saveError}");   // 1213(교착)이면 여기서 잡힌다
        Assert.Equal(0, r.Deadlocks);
        Assert.Equal(1, r.EndedMissed);
        Assert.Equal(0, await SessionExistsAsync(db, s.Sid));
        Assert.Equal(1, await RevokedAsync(db, s.RefreshId));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT CAST(cooltime_enabled AS SIGNED) FROM tenant_settings WHERE tenant_id = @T", new { T = _tenantA }));
        Assert.Equal(1, await SessionExistsAsync(db, other));
        Assert.Null(await AskedAtAsync(db, other));
    }

    [Fact(DisplayName = "G-C26 ⓑ 저장 먼저 — 읽기 → L 이 세션 행 쥠 → 저장(끔) 세션 행 기다림(설정 행 쥔 채) → 엔진(먼저 읽은 값) 설정 행 기다림 → L 되돌림 = 저장 예외 0 · 엔진 끝냄 0 · 세션 유지 · 토큰 미폐기 · 근태 무변경")]
    public async Task G_C26b_저장이_먼저_잡으면_엔진은_바뀐_값을_보고_물러난다()
    {
        if (!Ready("G-C26 ⓑ")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var s = await MissedSetupAsync(db, _tenantA);

        await using var cEngine = await OpenAsync();
        await using var cSave = await OpenAsync();
        await using var cHold = await OpenAsync();
        var engineThread = await ThreadIdAsync(cEngine);
        var saveThread = await ThreadIdAsync(cSave);
        var engine = Engine(cEngine, pusher);
        var snap = await SnapshotAsync(engine, _tenantA);

        await using var hold = await cHold.BeginTransactionAsync();
        await cHold.QueryAsync<string>("SELECT session_id FROM user_sessions WHERE session_id = @S FOR UPDATE", new { S = s.Sid }, hold);

        var tSave = new SettingsService(cSave).SaveAsync(CooltimeDto(false, "22:00"), _tenantA);
        await WaitLockWaitAsync(db, "ⓑ 기다림 1 — 저장이 세션 행을 기다림(설정 행은 쥔 채)", saveThread);
        var tEngine = engine.RunTenantAsync(snap, s.NowLocal);
        await WaitLockWaitAsync(db, "ⓑ 기다림 2 — 엔진이 기다림(고친 코드 = 설정 행 공유 잠금)", saveThread, engineThread);
        await hold.RollbackAsync();

        var saveError = await Record.ExceptionAsync(() => tSave);
        Assert.True(saveError is null, $"G-C26 ⓑ: 저장이 실패했다 — {saveError}");
        var r = await tEngine;
        Assert.Equal(0, r.Deadlocks);
        Assert.Equal(0, r.EndedMissed);
        await AssertNotEndedAsync(db, pusher, s, "G-C26 ⓑ");
    }
}
