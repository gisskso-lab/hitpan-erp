using Dapper;
using HitPan.Application.DTOs.User;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 10/5 작5 서버 보정(작업지시서 §8-7 · [3-V] 2차 이슈 12·13·16 · P2-04 부분) 게이트의 <b>DB 몫</b>.
/// <list type="bullet">
/// <item><b>G-E17</b>(P2-12) — 2단계 직원의 끄기·켜기가 연결 사원 직무(출입증 role 의 출처)를 본다 · 판정 뒤 승격도 원자로 막는다.</item>
/// <item><b>G-E18</b>(P2-13) — [다시 사용]이 연결 사원 퇴사면 409(대표·관리자·직원 경로 모두 · 실물 파이프라인 응답 모양까지).</item>
/// <item><b>G-E4b · G-E4c</b>(P2-04) — <b>실물</b> <see cref="UserService.CreateForEmployeeAsync"/> 가 좌석 잠금 · 사원 행 잠금(INSERT 앞)에서 기다린다.
///   좌석 장치를 빼면 G-E4b 가, 사원 행 FOR UPDATE 를 빼면 G-E4c 가 FAIL 한다(대조는 G-E4s 비DB 구조 게이트와 짝).</item>
/// <item><b>G-E19</b>(P3-16) — 대표 경로 계정 추가도 아이디를 trim 값으로 저장 · 사본 · 중복 검사.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>판정은 전부 운영 코드가 한다. 이 파일의 SQL 은 상태를 만드는 준비 · 결과 확인 · 대조군의 <b>옛 판 재현</b>뿐이다.</para>
/// <para>⚠️ 개발 PC 는 SKIP — CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB=1</c>)가 유일한 계측 경로. <b>로컬 초록은 증거가 아니다.</b></para>
/// </remarks>
public sealed partial class EmployeeAccountLinkGateDbTests
{
    /// <summary>사원 한 줄을 계정에 연결해 넣고 직무(role)를 정한다. 퇴사 모양은 <paramref name="active"/>·<paramref name="resigned"/>.</summary>
    private async Task<string> LinkedEmployeeAsync(MySqlConnection db, string empNo, string userId, string role,
        bool active = true, bool resigned = false)
    {
        var emp = await InsertEmployeeAsync(db, empNo, "사원" + empNo, userId, active, resigned);
        await db.ExecuteAsync("UPDATE employees SET role = @R WHERE employee_id = @E", new { R = role, E = emp });
        return emp;
    }

    private async Task<long> ActiveUsersAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE tenant_id=@T AND is_active=1 AND is_deleted=0", new { T = _tenantA });

    // ══ G-E17 — P2-12 직무 울타리 ══

    [Fact(DisplayName = "G-E17 🔴 P2-12 — 2단계 직원의 [사용 안 함]·[다시 사용]은 연결 사원 직무가 일반이 아닌 계정(account_type=tenant_user · 사원 role tenant_admin·'1')을 403 protected_account · 상태 그대로 · 일반 직무(sales_user)는 된다 · 판정 뒤 직무 승격(미커밋)도 원자로 막는다 · 대조군(직무 조건 없는 옛 술어 UPDATE 는 관리자 직무 계정을 끈다)")]
    public async Task E17_Staff_Fence_Sees_Employee_Role()
    {
        if (!Ready("G-E17")) return;
        string actor, admJob, admJobOff, numJob, gen, raced, oldTarget, racedEmp;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            actor = await InsertUserAsync(seed, "act1701", "2단계직원");
            admJob = await InsertUserAsync(seed, "adm1702", "관리직무");
            admJobOff = await InsertUserAsync(seed, "adm1703", "관리직무꺼짐", active: false);
            numJob = await InsertUserAsync(seed, "num1704", "숫자직무");
            gen = await InsertUserAsync(seed, "gen1705", "일반직무");
            raced = await InsertUserAsync(seed, "rac1706", "경쟁");
            oldTarget = await InsertUserAsync(seed, "old1707", "대조군");
            await LinkedEmployeeAsync(seed, "1702", admJob, "tenant_admin");
            await LinkedEmployeeAsync(seed, "1703", admJobOff, "tenant_admin");
            await LinkedEmployeeAsync(seed, "1704", numJob, "1");                 // P3-07 — 「1」 = TenantAdmin
            await LinkedEmployeeAsync(seed, "1705", gen, "sales_user");
            racedEmp = await LinkedEmployeeAsync(seed, "1706", raced, "sales_user");
            await LinkedEmployeeAsync(seed, "1707", oldTarget, "tenant_admin");
        }
        await using var db = await OpenAsync();
        var svc = new UserService(db, new NoOpAudit());

        // ① 관리자 직무 계정 끄기 · 켜기 · 숫자 직무 — 전부 403 · 상태 그대로
        var e1 = await Assert.ThrowsAsync<AccountActionForbiddenException>(() => svc.SuspendAsStaffAsync(admJob, actor, _tenantA));
        Assert.Equal("protected_account", e1.Code);
        Assert.True(await IsActiveAsync(db, admJob), "P2-12 — 관리자 직무 계정이 직원 손에 꺼졌다");

        var e2 = await Assert.ThrowsAsync<AccountActionForbiddenException>(() => svc.ResumeAsStaffAsync(admJobOff, actor, _tenantA));
        Assert.Equal("protected_account", e2.Code);
        Assert.False(await IsActiveAsync(db, admJobOff), "P2-12 — 관리자 직무 계정이 직원 손에 되살아났다");

        var e3 = await Assert.ThrowsAsync<AccountActionForbiddenException>(() => svc.SuspendAsStaffAsync(numJob, actor, _tenantA));
        Assert.Equal("protected_account", e3.Code);
        Assert.True(await IsActiveAsync(db, numJob));

        // ② 일반 직무 — 끄기·켜기 된다(울타리가 일반 직원 일을 막지 않는다)
        await svc.SuspendAsStaffAsync(gen, actor, _tenantA);
        Assert.False(await IsActiveAsync(db, gen));
        await svc.ResumeAsStaffAsync(gen, actor, _tenantA);
        Assert.True(await IsActiveAsync(db, gen));

        // ③ 원자 — 직원이 직무(sales_user)를 읽은 뒤 · UPDATE 전에 직무가 관리자로 승격(미커밋 · 사원 행 X 잠금)
        await using (var promoter = await OpenAsync())
        await using (var staffConn = await OpenAsync())
        {
            await using var ptx = await promoter.BeginTransactionAsync();
            await promoter.ExecuteAsync("UPDATE employees SET role = 'tenant_admin', updated_at = NOW(6) WHERE employee_id = @E",
                new { E = racedEmp }, ptx);

            var staff = Task.Run(() => new UserService(staffConn, new NoOpAudit()).SuspendAsStaffAsync(raced, actor, _tenantA));
            await WaitForLockWaitAsync(staffConn, staff, "G-E17 원자");
            await ptx.CommitAsync();

            var ex = await Assert.ThrowsAsync<AccountActionForbiddenException>(() => staff);
            Assert.Equal("protected_account", ex.Code);
        }
        Assert.True(await IsActiveAsync(db, raced), "P2-12 — 판정 뒤 관리자로 승격된 계정이 직원 손에 꺼졌다(원자화 실패)");

        // 🔴 대조군 — 직무 조건 없는 옛 술어(P2-12 전 StaffTouchablePredicate 그대로)는 같은 관리자 직무 계정을 「바꿔도 되는 대상」으로 보고 끈다
        Assert.True(await OldStaffJudgeAsync(db, oldTarget), "대조군 전제 — 옛 판정은 account_type 만 본다");
        var oldAffected = await db.ExecuteAsync(@"
            UPDATE users u SET u.is_active = 0, u.updated_at = NOW(6)
            WHERE u.user_id = @U AND u.tenant_id = @T AND u.is_deleted = 0
              AND u.is_parent = 0 AND COALESCE(u.account_type, '') <> 'tenant_admin'
              AND NOT EXISTS (SELECT 1 FROM user_permissions p WHERE p.user_id = u.user_id AND p.tenant_id = u.tenant_id
                              AND p.menu_code IN ('USERS', 'USERS_ACCOUNT', 'USERS_SEAT') AND p.can_view = 1)",
            new { U = oldTarget, T = _tenantA });
        Assert.Equal(1, oldAffected);
        Assert.False(await IsActiveAsync(db, oldTarget), "대조군 무효 — 옛 술어가 관리자 직무 계정을 못 껐다");
    }

    // ══ G-E18 — P2-13 퇴사 사원 계정 다시 사용 ══

    [Fact(DisplayName = "G-E18 🔴 P2-13 — [다시 사용] 연결 사원 퇴사(is_active=0 · MDB 모양 1·1) → 대표 경로·직원 경로 모두 409 employee_leaver 「퇴사한 사원의 계정은 다시 사용할 수 없습니다」 · 사용 중 수 불변 · 실물 파이프라인 응답 409+문구 · 재직 사원·사원 없는 계정은 된다 · 대조군(users 만 보는 옛 UPDATE 는 퇴사자 계정을 살린다)")]
    public async Task E18_Resume_Refuses_Leaver()
    {
        if (!Ready("G-E18")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var actor = await InsertUserAsync(db, "act1801", "2단계직원");
        var leaverOff = await InsertUserAsync(db, "lv1802", "퇴사자", active: false);
        var mdbOff = await InsertUserAsync(db, "lv1803", "MDB퇴사", active: false);
        var staffLeaver = await InsertUserAsync(db, "lv1804", "직원경로퇴사", active: false);
        var pipeLeaver = await InsertUserAsync(db, "lv1805", "파이프퇴사", active: false);
        var okOff = await InsertUserAsync(db, "ok1806", "재직꺼짐", active: false);
        var noEmpOff = await InsertUserAsync(db, "ne1807", "사원없음", active: false);
        var oldLeaver = await InsertUserAsync(db, "lv1808", "대조군퇴사", active: false);
        await LinkedEmployeeAsync(db, "1802", leaverOff, "sales_user", active: false);
        await LinkedEmployeeAsync(db, "1803", mdbOff, "sales_user", active: true, resigned: true);
        await LinkedEmployeeAsync(db, "1804", staffLeaver, "sales_user", active: false);
        await LinkedEmployeeAsync(db, "1805", pipeLeaver, "sales_user", active: false);
        await LinkedEmployeeAsync(db, "1806", okOff, "sales_user");
        await LinkedEmployeeAsync(db, "1808", oldLeaver, "sales_user", active: false);
        var svc = new UserService(db, new NoOpAudit());
        var active0 = await ActiveUsersAsync(db);

        // ① 대표·관리자 경로(ResumeAsync)
        foreach (var (id, what) in new[] { (leaverOff, "is_active=0"), (mdbOff, "MDB 1·1") })
        {
            var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => svc.ResumeAsync(id, _tenantA));
            Assert.Equal("employee_leaver", ex.Code);
            Assert.Equal(UserService.ResumeLeaverMessage, ex.Message);
            Assert.False(await IsActiveAsync(db, id), $"P2-13 — 퇴사 사원({what}) 계정이 대표 경로로 되살아났다");
        }
        Assert.Equal("퇴사한 사원의 계정은 다시 사용할 수 없습니다", UserService.ResumeLeaverMessage);

        // ② 직원 경로(ResumeAsStaffAsync) — 울타리는 통과하는 일반 직무 계정이어도 퇴사면 409
        var es = await Assert.ThrowsAsync<AccountLinkConflictException>(() => svc.ResumeAsStaffAsync(staffLeaver, actor, _tenantA));
        Assert.Equal("employee_leaver", es.Code);
        Assert.False(await IsActiveAsync(db, staffLeaver));
        Assert.Equal(active0, await ActiveUsersAsync(db));

        // ③ 실물 파이프라인 — 관리자 출입증으로 POST /api/users/{id}/resume ⇒ 409 + 같은 문구(컨트롤러 catch 배선 확인)
        var admin = await StaffAsync(db, "adm1809", 0, admin: true);
        var active1 = await ActiveUsersAsync(db);
        var res = await PipeAsync(db, "POST", $"/api/users/{pipeLeaver}/resume", admin);
        Assert.True(res.Status == 409, $"P2-13 — 퇴사 사원 계정 다시 사용 응답이 409 가 아니다: {res.Status} {res.Body}");
        Assert.Contains("employee_leaver", res.Body, StringComparison.Ordinal);
        Assert.Contains(UserService.ResumeLeaverMessage, System.Text.RegularExpressions.Regex.Unescape(res.Body), StringComparison.Ordinal);
        Assert.False(await IsActiveAsync(db, pipeLeaver));
        Assert.Equal(active1, await ActiveUsersAsync(db));

        // ④ 재직 사원 · 사원 행 없는 옛 계정 — 그대로 된다
        await svc.ResumeAsync(okOff, _tenantA);
        Assert.True(await IsActiveAsync(db, okOff));
        await svc.ResumeAsync(noEmpOff, _tenantA);
        Assert.True(await IsActiveAsync(db, noEmpOff));

        // 🔴 대조군 — P2-13 전 ResumeAsync 의 UPDATE(users 만 본다)는 같은 퇴사자 계정을 살린다
        var oldAffected = await db.ExecuteAsync(@"
            UPDATE users SET is_active = 1, updated_at = NOW(6)
            WHERE user_id = @U AND tenant_id = @T AND is_deleted = 0 AND is_active = 0",
            new { U = oldLeaver, T = _tenantA });
        Assert.Equal(1, oldAffected);
        Assert.True(await IsActiveAsync(db, oldLeaver), "대조군 무효 — 옛 UPDATE 가 퇴사자 계정을 못 살렸다");
    }

    // ══ G-E4b · G-E4c — P2-04 실물 함수의 장치 ══

    [Fact(DisplayName = "G-E4b 🔴 P2-04 좌석 장치 — 시험이 좌석 잠금(AccountSeatGuard.AcquireAsync 실물)을 쥔 동안 실물 CreateForEmployeeAsync 는 행 잠금 대기에 들어간다 · 그동안 users 0행(미커밋 포함) · 놓으면 성공 1 · 함수에서 좌석 장치를 빼면 대기 없이 끝나 FAIL")]
    public async Task E4b_Real_Function_Waits_On_Seat_Lock()
    {
        if (!Ready("G-E4b")) return;
        string emp;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            emp = await InsertEmployeeAsync(seed, "0411", "좌석대기");
        }

        await using var holder = await OpenAsync();
        await using var work = await OpenAsync();
        await using var htx = await holder.BeginTransactionAsync();
        Task<string> t;
        await using (await AccountSeatGuard.AcquireAsync(holder, htx, _tenantA, null, CancellationToken.None))
        {
            t = Task.Run(() => CreateForEmployeeAsync(new UserService(work, new NoOpAudit()), _tenantA, emp, "seat0411"));
            await WaitForLockWaitAsync(work, t, "G-E4b 좌석 장치");           // 좌석 장치가 없으면 여기서 「잠금을 안 기다렸다」 FAIL
            Assert.Equal(0L, await DirtyUsersAsync("seat0411"));                // 기다리는 자리는 INSERT 앞이다
            await htx.RollbackAsync();
        }

        // 좌석 잠금을 놓으면 기다리던 실물 호출이 끝난다 — 계정 1 · 사원 연결 1
        var uid = await t;
        await using var db = await OpenAsync();
        var (linked, lid) = await EmpLinkAsync(db, emp);
        Assert.Equal(uid, linked);
        Assert.Equal("seat0411", lid);
    }

    [Fact(DisplayName = "G-E4c 🔴 P2-04 사원 행 장치 — 시험이 사원 행을 FOR UPDATE 로 쥔 동안 실물 CreateForEmployeeAsync 는 INSERT 앞에서 기다린다(미커밋 users 0행) · 그사이 다른 계정이 연결되면 409 employee_has_account · 함수에서 FOR UPDATE 를 빼면 INSERT 가 먼저 돌아 미커밋 users 1행 ⇒ FAIL")]
    public async Task E4c_Real_Function_Waits_On_Employee_Row_Before_Insert()
    {
        if (!Ready("G-E4c")) return;
        string emp, live;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            emp = await InsertEmployeeAsync(seed, "0421", "사원행대기");
            live = await InsertUserAsync(seed, "live0421", "먼저연결");
        }

        await using var holder = await OpenAsync();
        await using var work = await OpenAsync();
        await using var htx = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT employee_id FROM employees WHERE employee_id = @E FOR UPDATE", new { E = emp }, htx);

        var t = Task.Run(() => CreateForEmployeeAsync(new UserService(work, new NoOpAudit()), _tenantA, emp, "row0421"));
        await WaitForLockWaitAsync(work, t, "G-E4c 사원 행 장치");
        Assert.True(await DirtyUsersAsync("row0421") == 0,
            "P2-04 — 사원 행을 잠그기 전에 users INSERT 가 돌았다(FOR UPDATE 없음 · 기다린 자리는 조건부 UPDATE)");

        // 기다리는 동안 다른 계정이 이 사원에 연결됐다 ⇒ 깨어나서 최신 값을 읽고 「이미 계정」 409
        await holder.ExecuteAsync("UPDATE employees SET user_id = @U, login_id = 'live0421' WHERE employee_id = @E", new { U = live, E = emp }, htx);
        await htx.CommitAsync();
        var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => t);
        Assert.Equal("employee_has_account", ex.Code);

        await using var db = await OpenAsync();
        Assert.Equal(0L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE tenant_id=@T AND email='row0421'", new { T = _tenantA }));
        Assert.Equal(live, (await EmpLinkAsync(db, emp)).userId);
    }

    /// <summary>미커밋 포함(READ UNCOMMITTED) users 행 수 — 기다리는 자리가 INSERT 앞인지 뒤인지 가른다.</summary>
    private async Task<long> DirtyUsersAsync(string email)
    {
        await using var probe = await OpenAsync();
        await probe.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");
        return await probe.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE tenant_id=@T AND email=@E", new { T = _tenantA, E = email });
    }

    // ══ G-E19 — P3-16 아이디 trim 저장 ══

    [Fact(DisplayName = "G-E19 🔴 P3-16 — 대표 경로 계정 추가(CreateAsync)에 \" hong19\" → users.email·사원 사본 모두 \"hong19\" · 다시 \"hong19 \" 는 「이미 사용 중인 아이디」 · 대조군(옛 원문 중복 검사는 앞 공백 아이디를 다른 아이디로 본다)")]
    public async Task E19_Create_Stores_Trimmed_LoginId()
    {
        if (!Ready("G-E19")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var svc = new UserService(db, new NoOpAudit());

        var id = await svc.CreateAsync(new CreateUserDto { Email = " hong19", UserName = "홍길동", Password = Pw, Role = "User" }, _tenantA);
        Assert.Equal("hong19", await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = id }));
        Assert.Equal("hong19", await db.ExecuteScalarAsync<string>("SELECT login_id FROM employees WHERE user_id=@U", new { U = id }));

        var users0 = await CountAsync(db, "users");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new CreateUserDto { Email = "hong19 ", UserName = "홍길동2", Password = Pw, Role = "User" }, _tenantA));
        Assert.Equal(UserService.LoginIdTakenMessage, ex.Message);
        Assert.Equal(users0, await CountAsync(db, "users"));

        // 🔴 대조군 — 옛 중복 검사(원문 그대로)는 「 hong19」 를 저장된 hong19 와 다른 아이디로 본다 ⇒ 통과 ⇒ 앞 공백째 두 번째 계정
        Assert.Equal(0L, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @T AND email = @E AND is_deleted = 0", new { T = _tenantA, E = " hong19" }));
    }
}
