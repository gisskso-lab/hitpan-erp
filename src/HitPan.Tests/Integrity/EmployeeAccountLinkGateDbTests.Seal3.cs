using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 10/6 작5 §8-12(작업지시서 · [4] 3차 V5-22·V5-23) 게이트의 <b>DB 몫</b>.
/// <list type="bullet">
/// <item><b>G-E21</b>(V5-22) — 같은 사람의 [사용 안 함]·[다시 사용](직원·대표 경로)과 퇴사가 동시에 → 1213 교착 0 · 한쪽이 기다린 뒤 정상.
///   대조군: §8-7 판의 옛 순서(users 먼저 → 하위질의 employees)를 시험 안에서 재현하면 1213.</item>
/// <item><b>G-E9v</b>(V5-23) — 2단계 직원 경로 for-employee 의 변형 직무(<c>"1"</c>·대소문자·앞뒤 공백)도 409.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>판정은 전부 운영 코드(<see cref="UserService"/> · <see cref="EmployeeService.ResignAsync"/>)가 한다.
/// 이 파일의 SQL 은 상태를 만드는 준비 · 줄 세우기용 잠금 · 결과 확인 · 대조군의 <b>옛 판 재현</b>뿐이다.</para>
/// <para>⚠️ 개발 PC 는 SKIP — CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB=1</c>)가 유일한 계측 경로. <b>로컬 초록은 증거가 아니다.</b></para>
/// </remarks>
public sealed partial class EmployeeAccountLinkGateDbTests
{
    // ══ G-E21 — V5-22 잠금 순서(employees → users) ══

    /// <summary>예외 사슬 어디든 MariaDB 1213(교착 · 희생자로 되돌려짐)이 있나.</summary>
    private static bool IsDeadlock(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is MySqlException { ErrorCode: MySqlErrorCode.LockDeadlock }) return true;
            if (e is AggregateException agg && agg.InnerExceptions.Any(IsDeadlock)) return true;
        }
        return false;
    }

    /// <summary>작업의 끝 — 성공이면 null, 실패면 그 예외(시험이 판정한다 · 삼키지 않는다).</summary>
    private static async Task<Exception?> OutcomeAsync(Task work)
    {
        try
        {
            await work;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// 같은 사람(사원 <paramref name="emp"/>)에 대해 퇴사와 <paramref name="op"/> 를 <b>결정적으로</b> 겹친다.
    /// ① 시험이 사원 행(기본키)을 쥔다 ② 실물 퇴사(<see cref="EmployeeService.ResignAsync"/>)가 그 행 앞에서 기다린다(LOCK WAIT 확인)
    /// ③ <paramref name="op"/> 가 기다림에 들어간다(LOCK WAIT 확인) ④ 시험이 놓는다 — 퇴사가 줄 맨 앞이라 먼저 깨어나 users 로 간다.
    /// <paramref name="op"/> 가 users 를 이미 쥐고 있으면(옛 순서) 여기서 1213 이 난다.
    /// </summary>
    private async Task<(Exception? Resign, bool Blocked, Exception? Op)> RaceWithResignAsync(
        string emp, Func<MySqlConnection, Task> op, string what)
    {
        await using var holder = await OpenAsync();
        await using var resignConn = await OpenAsync();
        await using var opConn = await OpenAsync();
        await using var htx = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT employee_id FROM employees WHERE employee_id = @E FOR UPDATE", new { E = emp }, htx);

        var resign = Task.Run(() => new EmployeeService(resignConn, new NoOpAudit()).ResignAsync(_tenantA, emp, null, "G-E21"));
        Task work = Task.CompletedTask;
        try
        {
            await WaitForLockWaitAsync(resignConn, resign, $"G-E21 {what} — 퇴사");
            work = Task.Run(() => op(opConn));
            await WaitForLockWaitAsync(opConn, work, $"G-E21 {what} — 조작");
            await htx.RollbackAsync();
        }
        catch (Exception first)
        {
            throw await DrainOnFailureAsync(first, Task.WhenAll(resign, work), htx, $"G-E21 {what}");
        }

        var resignErr = await OutcomeAsync(resign);
        var opErr = await OutcomeAsync(work);
        return (resignErr, resignErr is null && await resign, opErr);
    }

    /// <summary>§8-7 판 <c>ResumeAsync</c> 의 UPDATE 원문(옛 순서 재현 · 대조군 전용) — users X 를 먼저 쥐고 하위질의가 employees 를 잠금 읽기.</summary>
    private const string OldResumeUsersFirstSql = @"
        UPDATE users u SET u.is_active = 1, u.updated_at = NOW(6)
        WHERE u.user_id = @U AND u.tenant_id = @T AND u.is_deleted = 0 AND u.is_active = 0
          AND NOT EXISTS (SELECT 1 FROM employees e WHERE e.tenant_id = u.tenant_id AND e.user_id = u.user_id AND
              (e.is_active = 0 OR e.is_resigned = 1 OR (e.resign_date IS NOT NULL AND e.resign_date <= NOW(6))))";

    [Fact(DisplayName = "G-E21 🔴 V5-22 잠금 순서 — 같은 사람에 퇴사(실물 ResignAsync)와 [다시 사용](대표 ResumeAsync · 직원 ResumeAsStaffAsync)·[사용 안 함](직원 SuspendAsStaffAsync · 대표 SuspendAsync)을 겹쳐도 1213 0 · 퇴사 성공 · 조작은 기다린 뒤 정상 거절(지운 계정) · 대조군①(§8-7 옛 순서 users 먼저 UPDATE 를 시험이 재현하면 1213) · 대조군②(연결 사원 행을 보조 색인 tenant_id·user_id 로 바로 FOR UPDATE 하면 1213)")]
    public async Task E21_Suspend_Resume_Vs_Resign_No_Deadlock()
    {
        if (!Ready("G-E21")) return;
        string actor, rOwner, rStaff, sStaff, sOwner, ctl1, ctl2;
        string eROwner, eRStaff, eSStaff, eSOwner, eCtl1, eCtl2;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            actor = await InsertUserAsync(seed, "act2101", "2단계직원");
            rOwner = await InsertUserAsync(seed, "ro2102", "대표다시사용", active: false);
            rStaff = await InsertUserAsync(seed, "rs2103", "직원다시사용", active: false);
            sStaff = await InsertUserAsync(seed, "ss2104", "직원사용안함");
            sOwner = await InsertUserAsync(seed, "so2105", "대표사용안함");
            ctl1 = await InsertUserAsync(seed, "c12106", "대조군옛순서", active: false);
            ctl2 = await InsertUserAsync(seed, "c22107", "대조군보조색인", active: false);
            eROwner = await LinkedEmployeeAsync(seed, "2102", rOwner, "sales_user");
            eRStaff = await LinkedEmployeeAsync(seed, "2103", rStaff, "sales_user");
            eSStaff = await LinkedEmployeeAsync(seed, "2104", sStaff, "sales_user");
            eSOwner = await LinkedEmployeeAsync(seed, "2105", sOwner, "sales_user");
            eCtl1 = await LinkedEmployeeAsync(seed, "2106", ctl1, "sales_user");
            eCtl2 = await LinkedEmployeeAsync(seed, "2107", ctl2, "sales_user");
        }

        // ── 봉합 — 연결 사원 행이 users 보다 먼저(퇴사와 같은 순서) ──
        var cases = new (string What, string Emp, string User, Func<MySqlConnection, Task> Op)[]
        {
            ("대표 ResumeAsync", eROwner, rOwner, c => new UserService(c, new NoOpAudit()).ResumeAsync(rOwner, _tenantA)),
            ("직원 ResumeAsStaffAsync", eRStaff, rStaff, c => new UserService(c, new NoOpAudit()).ResumeAsStaffAsync(rStaff, actor, _tenantA)),
            ("직원 SuspendAsStaffAsync", eSStaff, sStaff, c => new UserService(c, new NoOpAudit()).SuspendAsStaffAsync(sStaff, actor, _tenantA)),
        };
        await using var db = await OpenAsync();
        // ⬛ [10/6 이어받기 전] 경우마다 바로 Assert — 첫 경우가 FAIL 하면 나머지 둘은 안 돌아 「봉합 빼면 셋 다 FAIL」을 한 번에 못 봤다.
        //   경우별 첫 실패 한 줄을 모아 끝에 한 번 단언한다(경우끼리 다른 사원·계정 — 서로 안 오염).
        var failures = new List<string>();
        foreach (var (what, emp, user, op) in cases)
        {
            var (resignErr, blocked, opErr) = await RaceWithResignAsync(emp, op, what);
            string? fail = null;
            if (IsDeadlock(resignErr) || IsDeadlock(opErr))
                fail = $"V5-22 — {what} ↔ 퇴사 1213 교착 · 퇴사={resignErr?.GetBaseException().Message ?? "성공"} · 조작={opErr?.GetBaseException().Message ?? "성공"}";
            else if (resignErr is not null)
                fail = $"{what} — 퇴사가 실패했다: {resignErr}";
            else if (!blocked)
                fail = $"{what} — 퇴사가 계정을 지우지 않았다(연결 계정 없음?)";
            // 퇴사가 먼저 끝나 계정을 지웠다 ⇒ 기다리던 조작은 「찾을 수 없음」으로 정상 거절(서버 오류 아님)
            else if (opErr is not InvalidOperationException)
                fail = $"{what} — 기다린 뒤 정상 거절이 아니다: {opErr?.GetType().Name} {opErr?.Message}";
            else if (!await db.ExecuteScalarAsync<bool>("SELECT is_deleted FROM users WHERE user_id=@U", new { U = user }))
                fail = $"{what} — 퇴사가 계정을 지우지 않았다";
            else if (await EmpLinkAsync(db, emp) != (null, null))
                fail = $"{what} — 퇴사 뒤 사원 연결이 남았다";
            if (fail is not null) failures.Add(fail);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // 대표 경로 [사용 안 함](SuspendAsync)은 사원 행을 안 잡는다 — 퇴사가 기다리는 동안 기다림 없이 끝나고 퇴사도 그 뒤 정상
        await using (var holder = await OpenAsync())
        await using (var resignConn = await OpenAsync())
        await using (var opConn = await OpenAsync())
        {
            await using var htx = await holder.BeginTransactionAsync();
            await holder.ExecuteAsync("SELECT employee_id FROM employees WHERE employee_id = @E FOR UPDATE", new { E = eSOwner }, htx);
            var resign = Task.Run(() => new EmployeeService(resignConn, new NoOpAudit()).ResignAsync(_tenantA, eSOwner, null, "G-E21"));
            try
            {
                await WaitForLockWaitAsync(resignConn, resign, "G-E21 대표 SuspendAsync — 퇴사");
                await new UserService(opConn, new NoOpAudit()).SuspendAsync(sOwner, _tenantA);
                await htx.RollbackAsync();
            }
            catch (Exception first)
            {
                throw await DrainOnFailureAsync(first, resign, htx, "G-E21 대표 SuspendAsync");
            }
            var resignErr = await OutcomeAsync(resign);
            Assert.False(IsDeadlock(resignErr), $"대표 SuspendAsync ↔ 퇴사 1213: {resignErr?.GetBaseException().Message}");
            Assert.True(resignErr is null, $"대표 SuspendAsync 뒤 퇴사가 정상이 아니다: {resignErr}");
            Assert.True(await resign, "대표 SuspendAsync 뒤 퇴사가 계정을 지우지 않았다");
        }

        // ── 🔴 대조군① — §8-7 판의 옛 순서(UPDATE users 먼저 → 하위질의 employees 잠금 읽기)를 시험이 재현하면 1213 ──
        var (r1, _, o1) = await RaceWithResignAsync(eCtl1, async c =>
        {
            await using var otx = await c.BeginTransactionAsync();
            await c.ExecuteAsync(OldResumeUsersFirstSql, new { U = ctl1, T = _tenantA }, otx);
            await otx.CommitAsync();
        }, "대조군① 옛 순서");
        Assert.True(IsDeadlock(r1) || IsDeadlock(o1),
            $"대조군① 무효 — 옛 순서(users 먼저)인데 1213 이 안 났다 · 퇴사={r1?.GetBaseException().Message ?? "성공"} · 옛 UPDATE={o1?.GetBaseException().Message ?? "성공"}");

        // ── 🔴 대조군② — 연결 사원 행을 보조 색인(uq_employees_tenant_user)으로 바로 FOR UPDATE 하면(보조 색인 줄 먼저 → 기본키 대기)
        //   퇴사가 마지막 SET user_id = NULL 로 그 보조 색인 줄을 고치러 와서 1213 — 봉합이 기본키로 잠그는 이유 ──
        var (r2, _, o2) = await RaceWithResignAsync(eCtl2, async c =>
        {
            await using var otx = await c.BeginTransactionAsync();
            await c.QueryAsync<string>("SELECT employee_id FROM employees WHERE tenant_id = @T AND user_id = @U FOR UPDATE",
                new { U = ctl2, T = _tenantA }, otx);
            await c.ExecuteAsync(OldResumeUsersFirstSql, new { U = ctl2, T = _tenantA }, otx);
            await otx.CommitAsync();
        }, "대조군② 보조 색인 잠금");
        Assert.True(IsDeadlock(r2) || IsDeadlock(o2),
            $"대조군② 무효 — 보조 색인으로 잠갔는데 1213 이 안 났다 · 퇴사={r2?.GetBaseException().Message ?? "성공"} · 조작={o2?.GetBaseException().Message ?? "성공"}");
    }

    // ══ G-E21(작5 §8-13) — 계정폐기 RetireAsync 잠금 순서(employees → users) ══

    /// <summary>
    /// 같은 사람(사원 <paramref name="emp"/>)에 대해 <paramref name="first"/> 와 <paramref name="second"/> 를 <b>결정적으로</b> 겹친다 —
    /// <see cref="RaceWithResignAsync"/> 의 두 조작판. ① 시험이 사원 행(기본키)을 쥔다 ② <paramref name="first"/> 가 그 행 앞에서 기다린다(LOCK WAIT 확인)
    /// ③ <paramref name="second"/> 가 기다림에 들어간다(LOCK WAIT 확인) ④ 시험이 놓는다 — <paramref name="first"/> 가 줄 맨 앞이라 먼저 깨어나 users 로 간다.
    /// <paramref name="second"/> 가 users 를 이미 쥐고 있으면(옛 계정폐기 순서) 여기서 1213 이 난다. 실패하면 연결 정리 전에 두 작업을 끝까지 기다린다.
    /// </summary>
    private async Task<(Exception? First, Exception? Second)> RaceOnEmployeeRowAsync(
        string emp, Func<MySqlConnection, Task> first, Func<MySqlConnection, Task> second, string what)
    {
        await using var holder = await OpenAsync();
        await using var firstConn = await OpenAsync();
        await using var secondConn = await OpenAsync();
        await using var htx = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT employee_id FROM employees WHERE employee_id = @E FOR UPDATE", new { E = emp }, htx);

        var t1 = Task.Run(() => first(firstConn));
        Task t2 = Task.CompletedTask;
        try
        {
            await WaitForLockWaitAsync(firstConn, t1, $"G-E21 {what} — 먼저");
            t2 = Task.Run(() => second(secondConn));
            await WaitForLockWaitAsync(secondConn, t2, $"G-E21 {what} — 나중");
            await htx.RollbackAsync();
        }
        catch (Exception failure)
        {
            throw await DrainOnFailureAsync(failure, Task.WhenAll(t1, t2), htx, $"G-E21 {what}");
        }

        return (await OutcomeAsync(t1), await OutcomeAsync(t2));
    }

    /// <summary>§8-13 전 <c>RetireAsync</c> 의 두 UPDATE 원문(옛 순서 재현 · 대조군 전용) — users X 를 먼저 쥐고 employees 를 고치러 간다.
    /// 출입증 끊기(refresh_tokens·user_sessions)는 잠금 고리와 무관해 뺐다.</summary>
    private static async Task OldRetireUsersFirstAsync(MySqlConnection c, string userId, string tenantId)
    {
        await using var otx = await c.BeginTransactionAsync();
        await c.ExecuteAsync(@"
            UPDATE users SET is_active = 0, is_deleted = 1, email = CONCAT('retired+', user_id, '+', LEFT(email, 40)),
                deleted_at = NOW(6), updated_at = NOW(6)
            WHERE user_id = @U AND tenant_id = @T AND is_deleted = 0 AND is_parent = 0", new { U = userId, T = tenantId }, otx);
        await c.ExecuteAsync(
            "UPDATE employees SET user_id = NULL, login_id = NULL, updated_at = NOW(6) WHERE tenant_id = @T AND user_id = @U",
            new { U = userId, T = tenantId }, otx);
        await otx.CommitAsync();
    }

    [Fact(DisplayName = "G-E21 🔴 작5 §8-13 계정폐기 잠금 순서 — 같은 사람에 계정폐기(실물 RetireAsync)와 [사용 안 함](직원 SuspendAsStaffAsync · 대표 SuspendAsync)·퇴사(실물 ResignAsync · V5-26)를 겹쳐도 1213 0 · 계정폐기 끝에 계정 지움·사원 연결 끊김 · 대표 SuspendAsync 는 사원 행 앞에서 기다리는 계정폐기에 막히지 않는다(users 를 아직 안 건드렸다) · 대조군③(§8-13 전 users 먼저 계정폐기를 시험이 재현하면 직원 [사용 안 함]과 1213) · 대조군④(같은 재현이 퇴사와 1213)")]
    public async Task E21_Retire_Vs_Suspend_Resign_No_Deadlock()
    {
        if (!Ready("G-E21")) return;
        string actor, rvStaff, rvOwner, rvResign, ctl3, ctl4;
        string eRvStaff, eRvOwner, eRvResign, eCtl3, eCtl4;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            actor = await InsertUserAsync(seed, "act2111", "2단계직원");
            rvStaff = await InsertUserAsync(seed, "rv2112", "폐기직원사용안함");
            rvOwner = await InsertUserAsync(seed, "rv2113", "폐기대표사용안함");
            rvResign = await InsertUserAsync(seed, "rv2114", "폐기퇴사");
            ctl3 = await InsertUserAsync(seed, "c32115", "대조군옛폐기직원");
            ctl4 = await InsertUserAsync(seed, "c42116", "대조군옛폐기퇴사");
            eRvStaff = await LinkedEmployeeAsync(seed, "2112", rvStaff, "sales_user");
            eRvOwner = await LinkedEmployeeAsync(seed, "2113", rvOwner, "sales_user");
            eRvResign = await LinkedEmployeeAsync(seed, "2114", rvResign, "sales_user");
            eCtl3 = await LinkedEmployeeAsync(seed, "2115", ctl3, "sales_user");
            eCtl4 = await LinkedEmployeeAsync(seed, "2116", ctl4, "sales_user");
        }

        await using var db = await OpenAsync();
        // 경우별 첫 실패 한 줄을 모아 끝에 한 번 단언한다(봉합 빼면 어느 경우가 FAIL 하는지 한 번에 본다 · 경우끼리 다른 사원·계정).
        var failures = new List<string>();

        async Task CheckRetiredAsync(string what, string user, string emp)
        {
            if (!await db.ExecuteScalarAsync<bool>("SELECT is_deleted FROM users WHERE user_id=@U", new { U = user }))
                failures.Add($"{what} — 계정이 지워지지 않았다");
            else if (await EmpLinkAsync(db, emp) != (null, null))
                failures.Add($"{what} — 사원 연결(user_id·login_id)이 남았다");
        }

        // ① 계정폐기 ↔ 직원 [사용 안 함] — 직원 끄기가 줄 맨 앞(employees → users) · 계정폐기가 뒤에서 기다린다.
        //   봉합 뒤: 계정폐기는 사원 행 앞에서 아무것도 안 쥔 채 기다리다 끄기 커밋 뒤 정상 폐기(끈 계정도 폐기 대상).
        {
            const string what = "계정폐기 ↔ 직원 SuspendAsStaffAsync";
            var (suspendErr, retireErr) = await RaceOnEmployeeRowAsync(eRvStaff,
                c => new UserService(c, new NoOpAudit()).SuspendAsStaffAsync(rvStaff, actor, _tenantA),
                c => new UserService(c, new NoOpAudit()).RetireAsync(rvStaff, _tenantA),
                what);
            if (IsDeadlock(suspendErr) || IsDeadlock(retireErr))
                failures.Add($"{what} 1213 교착 · 끄기={suspendErr?.GetBaseException().Message ?? "성공"} · 폐기={retireErr?.GetBaseException().Message ?? "성공"}");
            else if (suspendErr is not null || retireErr is not null)
                failures.Add($"{what} — 둘 다 정상이어야 한다 · 끄기={suspendErr} · 폐기={retireErr}");
            else
                await CheckRetiredAsync(what, rvStaff, eRvStaff);
        }

        // ② 계정폐기 ↔ 대표 [사용 안 함] — 대표 끄기(SuspendAsync)는 사원 행을 안 잡는다. 계정폐기가 사원 행 앞에서 기다리는 동안
        //   users 를 아직 안 쥐었으므로 끄기는 기다림 없이 끝나야 한다(옛 순서면 계정폐기가 users X 를 쥐고 있어 끄기가 막힌다).
        {
            const string what = "계정폐기 ↔ 대표 SuspendAsync";
            await using var holder = await OpenAsync();
            await using var retireConn = await OpenAsync();
            await using var opConn = await OpenAsync();
            await using var htx = await holder.BeginTransactionAsync();
            await holder.ExecuteAsync("SELECT employee_id FROM employees WHERE employee_id = @E FOR UPDATE", new { E = eRvOwner }, htx);
            var retire = Task.Run(() => new UserService(retireConn, new NoOpAudit()).RetireAsync(rvOwner, _tenantA));
            Task suspend = Task.CompletedTask;
            var suspendBlocked = false;
            try
            {
                await WaitForLockWaitAsync(retireConn, retire, $"G-E21 {what} — 계정폐기");
                suspend = Task.Run(() => new UserService(opConn, new NoOpAudit()).SuspendAsync(rvOwner, _tenantA));
                suspendBlocked = await Task.WhenAny(suspend, Task.Delay(TimeSpan.FromSeconds(10))) != suspend;
                await htx.RollbackAsync();
            }
            catch (Exception failure)
            {
                throw await DrainOnFailureAsync(failure, Task.WhenAll(retire, suspend), htx, $"G-E21 {what}");
            }
            var retireErr = await OutcomeAsync(retire);
            var suspendErr = await OutcomeAsync(suspend);
            if (IsDeadlock(retireErr) || IsDeadlock(suspendErr))
                failures.Add($"{what} 1213 교착 · 폐기={retireErr?.GetBaseException().Message ?? "성공"} · 끄기={suspendErr?.GetBaseException().Message ?? "성공"}");
            else if (suspendBlocked)
                failures.Add($"{what} — 사원 행 앞에서 기다리는 계정폐기가 users 를 이미 쥐었다(끄기가 10초 넘게 막힘 · 옛 순서) · 끄기={suspendErr?.GetBaseException().Message ?? "성공"}");
            else if (retireErr is not null || suspendErr is not null)
                failures.Add($"{what} — 둘 다 정상이어야 한다 · 폐기={retireErr} · 끄기={suspendErr}");
            else
                await CheckRetiredAsync(what, rvOwner, eRvOwner);
        }

        // ③ 계정폐기 ↔ 퇴사(V5-26) — 퇴사가 줄 맨 앞 · 계정폐기는 기다린 뒤 「찾을 수 없음」 정상 거절(퇴사가 이미 계정을 지웠다).
        {
            const string what = "계정폐기 RetireAsync ↔ 퇴사(V5-26)";
            var (resignErr, deleted, retireErr) = await RaceWithResignAsync(eRvResign,
                c => new UserService(c, new NoOpAudit()).RetireAsync(rvResign, _tenantA), what);
            if (IsDeadlock(resignErr) || IsDeadlock(retireErr))
                failures.Add($"{what} 1213 교착 · 퇴사={resignErr?.GetBaseException().Message ?? "성공"} · 폐기={retireErr?.GetBaseException().Message ?? "성공"}");
            else if (resignErr is not null)
                failures.Add($"{what} — 퇴사가 실패했다: {resignErr}");
            else if (!deleted)
                failures.Add($"{what} — 퇴사가 계정을 지우지 않았다(연결 계정 없음?)");
            else if (retireErr is not InvalidOperationException)
                failures.Add($"{what} — 기다린 뒤 정상 거절이 아니다: {retireErr?.GetType().Name} {retireErr?.Message}");
            else
                await CheckRetiredAsync(what, rvResign, eRvResign);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // ── 🔴 대조군③ — §8-13 전 계정폐기 순서(UPDATE users 먼저 → UPDATE employees)를 시험이 재현하면 직원 [사용 안 함]과 1213 ──
        var (s3, o3) = await RaceOnEmployeeRowAsync(eCtl3,
            c => new UserService(c, new NoOpAudit()).SuspendAsStaffAsync(ctl3, actor, _tenantA),
            c => OldRetireUsersFirstAsync(c, ctl3, _tenantA),
            "대조군③ 옛 계정폐기 ↔ 직원 사용 안 함");
        Assert.True(IsDeadlock(s3) || IsDeadlock(o3),
            $"대조군③ 무효 — 옛 계정폐기(users 먼저)인데 1213 이 안 났다 · 끄기={s3?.GetBaseException().Message ?? "성공"} · 옛 폐기={o3?.GetBaseException().Message ?? "성공"}");

        // ── 🔴 대조군④ — 같은 재현이 퇴사와 1213(V5-26 이 실제로 있던 자리) ──
        var (r4, _, o4) = await RaceWithResignAsync(eCtl4, c => OldRetireUsersFirstAsync(c, ctl4, _tenantA), "대조군④ 옛 계정폐기 ↔ 퇴사");
        Assert.True(IsDeadlock(r4) || IsDeadlock(o4),
            $"대조군④ 무효 — 옛 계정폐기(users 먼저)인데 1213 이 안 났다 · 퇴사={r4?.GetBaseException().Message ?? "성공"} · 옛 폐기={o4?.GetBaseException().Message ?? "성공"}");
    }

    // ══ G-E9v — V5-23 for-employee 변형 직무 ══

    [Fact(DisplayName = "G-E9v 🔴 V5-23 P1-01 변형 직무 — 2단계 직원 경로 for-employee 에 사원 직무 \"1\"·\"TenantAdmin\"·\"TENANT_ADMIN\"·\" TenantAdmin \"·\" tenant_admin \" 는 전부 409 employee_role_not_general · users 무변화 · 연결 없음 · 일반 변형(\"3\"·\" sales_user \")은 된다 · 대조군(문자열 그대로 비교 판정 role == \"tenant_admin\" 은 변형 다섯을 전부 일반으로 본다 ⇒ 201)")]
    public async Task E9v_ForEmployee_Variant_Admin_Roles_409()
    {
        if (!Ready("G-E9v")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var svc = new UserService(db, new NoOpAudit());

        var adminVariants = new[] { "1", "TenantAdmin", "TENANT_ADMIN", " TenantAdmin ", " tenant_admin " };
        var n = 0;
        foreach (var role in adminVariants)
        {
            n++;
            var emp = await InsertEmployeeAsync(db, $"09{60 + n}", $"변형직무{n}");
            await db.ExecuteAsync("UPDATE employees SET role = @R WHERE employee_id = @E", new { R = role, E = emp });
            Assert.Equal(role, await db.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE employee_id=@E", new { E = emp }));   // 저장값 그대로(공백 포함)

            var users0 = await CountAsync(db, "users");
            var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => CreateForEmployeeAsync(svc, _tenantA, emp, $"var09{60 + n}"));
            Assert.True(ex.Code == "employee_role_not_general", $"V5-23 — 직무 「{role}」 의 거절 코드가 다르다: {ex.Code}");
            Assert.Equal(users0, await CountAsync(db, "users"));
            Assert.Null((await EmpLinkAsync(db, emp)).userId);
        }

        // 일반 변형 — 울타리가 일반 직원 일을 막지 않는다(숫자 3 = User · 앞뒤 공백 *_user)
        foreach (var (role, i) in new[] { ("3", 71), (" sales_user ", 72) })
        {
            var emp = await InsertEmployeeAsync(db, $"09{i}", $"일반변형{i}");
            await db.ExecuteAsync("UPDATE employees SET role = @R WHERE employee_id = @E", new { R = role, E = emp });
            var uid = await CreateForEmployeeAsync(svc, _tenantA, emp, $"gen09{i}");
            Assert.Equal(uid, (await EmpLinkAsync(db, emp)).userId);
        }

        // 🔴 대조군 — 문자열 그대로 비교하는 판정(정규화 없음)이었다면 변형 다섯이 전부 「일반」으로 새어 201 이 됐다
        static bool NaiveIsGeneral(string r) => !string.Equals(r, "tenant_admin", StringComparison.Ordinal);
        Assert.Equal(adminVariants.Length, adminVariants.Count(NaiveIsGeneral));
        Assert.DoesNotContain(adminVariants, UserService.IsGeneralEmployeeRole);
    }
}
