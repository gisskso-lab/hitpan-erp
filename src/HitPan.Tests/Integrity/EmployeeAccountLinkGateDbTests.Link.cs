using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Dapper;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Common;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 10/5 작5 §연결 — 갈래2 합류 뒤 「합류 후 연결」 목록(게이트 개발명세서 §5) 중 새 함수가 필요했던 셋.
/// <list type="bullet">
/// <item><b>G-E9t</b>(P1-01) — 관리자 직무 사원 409 를 <b>실제로 발급된 출입증의 role 클레임</b>으로 잰다(출처 칸 employees.role 이 아니라 토큰).</item>
/// <item><b>G-E8r</b>(P-6 · V5-01) — 되돌림 기간 옛 판이 남긴 낡은 사본을 재전진 기동 1회 <see cref="EmployeeLoginIdSync.ResyncAsync"/> 가 다시 맞춘다.</item>
/// <item><b>G-E16</b>(작업지시서 §8-4 R-1) — 2단계 직원의 끄기가 판정 뒤 관리자로 바뀐 대상을 못 끈다(원자 UPDATE).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>판정은 전부 운영 코드가 한다. 이 파일의 SQL 은 상태를 <b>만드는</b> 준비와 결과를 <b>읽는</b> 확인, 그리고 대조군의 <b>옛 판 재현</b>뿐이다.</para>
/// <para>⚠️ 개발 PC 는 SKIP — CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB=1</c>)가 유일한 계측 경로.</para>
/// </remarks>
public sealed partial class EmployeeAccountLinkGateDbTests
{
    // ══ G-E9t — P1-01 실제 출입증 ══

    /// <summary>
    /// <c>AuthService.LoginAsync</c> <b>실물</b>로 로그인해 발급된 access 토큰의 <c>role</c> 클레임을 읽는다.
    /// 계정·사원 조회는 운영 <see cref="AuthUserLookup"/>(EF · 운영과 같은 조회식) — 토큰의 role 이 어디서 오는지를 시험이 정하지 않는다.
    /// </summary>
    private async Task<(List<string> Roles, string? AccountType)> LoginClaimsAsync(string loginId)
    {
        // JWT_SECRET 은 설정 파일이 없는 환경에서 환경변수로 폴백한다 — 값을 레포에 남기지 않는다(매 실행 무작위 · 선례 SessionRecordConcurrentPcGateTests).
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
            Environment.SetEnvironmentVariable("JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(DbConnString(), new MariaDbServerVersion(new Version(11, 4, 0))).Options;
        await using var ef = new AppDbContext(opts, Mock.Of<ICurrentTenant>(), Mock.Of<IEncryptionService>());
        await using var conn = await OpenAsync();
        var auth = new AuthService(new LinkGateUnitOfWork(conn), new AuthUserLookup(ef));

        var resp = await auth.LoginAsync(new LoginRequest { Email = loginId, Password = Pw, DeviceType = "pc", UserAgent = PcUa });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resp.AccessToken);
        return (jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Distinct().ToList(),
                jwt.Claims.FirstOrDefault(c => c.Type == "account_type")?.Value);
    }

    [Fact(DisplayName = "G-E9t 🔴 P1-01 실제 출입증 — 2단계 경로로 일반 직무 사원에 만든 계정의 로그인 토큰 role 클레임 = 일반 · 관리자 직무 사원은 409(employee_role_not_general) · 대조군(울타리 없이 같은 사원에 「일반」 계정을 만들면 account_type 은 tenant_user 인데 실제 토큰 role 은 tenant_admin)")]
    public async Task E9t_Issued_Token_Role_Claim()
    {
        if (!Ready("G-E9t")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var svc = new UserService(db, new NoOpAudit());
        var gen = await InsertEmployeeAsync(db, "0931", "일반직무");              // role = 출하 DDL 기본값(sales_user)
        var adminJob = await InsertEmployeeAsync(db, "0932", "관리직무");
        await db.ExecuteAsync("UPDATE employees SET role='tenant_admin' WHERE employee_id=@E", new { E = adminJob });

        // ① 2단계 직원 경로(actorIsAdmin=false) — 요청 role=TenantAdmin 이어도 무시 · 실제 토큰 role 은 일반
        await CreateForEmployeeAsync(svc, _tenantA, gen, "gen0931", role: "TenantAdmin");
        var (genRoles, genType) = await LoginClaimsAsync("gen0931");
        Assert.NotEmpty(genRoles);
        foreach (var r in genRoles)
            Assert.True(UserService.IsGeneralEmployeeRole(r), $"2단계가 만든 계정의 토큰 role 이 일반이 아니다: {r}");
        Assert.Equal("tenant_user", genType);

        // ② 관리자 직무 사원 — 2단계 경로는 409 · users 무변화 · 연결 없음
        var users0 = await CountAsync(db, "users");
        var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => CreateForEmployeeAsync(svc, _tenantA, adminJob, "adm0932"));
        Assert.Equal("employee_role_not_general", ex.Code);
        Assert.Equal(users0, await CountAsync(db, "users"));
        Assert.Null((await EmpLinkAsync(db, adminJob)).userId);

        // 🔴 대조군 — 울타리가 없으면: 같은 사원에 「일반(User)」 계정을 만들어도(대표 경로 actorIsAdmin=true 로 재현)
        //   users.account_type 은 tenant_user 인데 실제 발급 토큰의 role 은 사원 직무 tenant_admin 이다(AuthService CreateLoginResponse — 사원 role 이 먼저).
        //   ⇒ 409 가 없으면 2단계 직원이 관리자 role 출입증을 만들 수 있다. 위 ② 의 409 가 그 길을 막는 유일한 자리다.
        await CreateForEmployeeAsync(svc, _tenantA, adminJob, "adm0932", role: "User", actorIsAdmin: true);
        Assert.Equal("tenant_user", await db.ExecuteScalarAsync<string>(
            "SELECT account_type FROM users WHERE tenant_id=@T AND email='adm0932'", new { T = _tenantA }));
        var (admRoles, _) = await LoginClaimsAsync("adm0932");
        Assert.Contains("tenant_admin", admRoles);
        Assert.DoesNotContain(admRoles, UserService.IsGeneralEmployeeRole);
    }

    // ══ G-E8r — V5-01 재전진 재맞춤 ══

    [Fact(DisplayName = "G-E8r 🔴 V5-01 재전진 — 되돌림 기간 옛 판(login_id 모름)의 계정폐기·계정추가 흔적 → 기동 1회 ResyncAsync 가 채움 1·비움 1 · 사본 어긋남 0 · 두 번째 0행(멱등) · 칸 없는 DB 는 건너뜀 · 대조군(재맞춤 전에는 낡은 사본 2)")]
    public async Task E8r_Reforward_Resync()
    {
        if (!Ready("G-E8r")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var svc = new UserService(db, new NoOpAudit());
        var keep = await svc.CreateAsync(new CreateUserDto { Email = "keep0851", UserName = "유지", Password = Pw, Role = "User" }, _tenantA);
        var gone = await svc.CreateAsync(new CreateUserDto { Email = "gone0852", UserName = "옛폐기", Password = Pw, Role = "User" }, _tenantA);
        var made = await svc.CreateAsync(new CreateUserDto { Email = "made0853", UserName = "옛추가", Password = Pw, Role = "User" }, _tenantA);
        await svc.SuspendAsync(keep, _tenantA);                                    // 사용 안 함 = 계정 있음 ⇒ 사본 유지
        Assert.Equal(0, await LoginIdDriftAsync(db));
        Assert.Equal(new EmployeeLoginIdSync.Result(false, 0, 0), await EmployeeLoginIdSync.ResyncAsync(db, null));   // 맞는 DB = 0행

        // 되돌림 기간(1.3.52 재게시)의 옛 판 흔적 — 옛 SQL 그대로 재현
        //  ⓐ 옛 계정폐기: users 폐기 + ⬛ 옛 줄 "UPDATE employees SET user_id = NULL, updated_at = NOW(6) …"(login_id 를 모른다) ⇒ 사본이 남는다
        var empGone = await db.QuerySingleAsync<string>("SELECT employee_id FROM employees WHERE user_id=@U", new { U = gone });
        await db.ExecuteAsync(@"UPDATE users SET is_active = 0, is_deleted = 1, email = CONCAT('retired+', user_id, '+', LEFT(email, 40)),
                                       deleted_at = NOW(6), updated_at = NOW(6)
                                WHERE user_id = @U AND tenant_id = @T", new { U = gone, T = _tenantA });
        await db.ExecuteAsync("UPDATE employees SET user_id = NULL, updated_at = NOW(6) WHERE tenant_id = @T AND user_id = @U",
            new { U = gone, T = _tenantA });
        //  ⓑ 옛 계정추가: 옛 INSERT employees 에는 login_id 칸이 없다 ⇒ 사본 NULL
        await db.ExecuteAsync("UPDATE employees SET login_id = NULL WHERE tenant_id = @T AND user_id = @U", new { U = made, T = _tenantA });

        // 🔴 대조군 — 재맞춤이 없으면 낡은 사본 2(폐기된 아이디가 남고 · 새 계정 아이디가 비었다)
        Assert.Equal(2, await LoginIdDriftAsync(db));

        var r1 = await EmployeeLoginIdSync.ResyncAsync(db, null);
        Assert.False(r1.ColumnMissing);
        Assert.Equal(1, r1.Filled);
        Assert.Equal(1, r1.Cleared);
        Assert.Equal(0, await LoginIdDriftAsync(db));
        Assert.Equal((null, null), await EmpLinkAsync(db, empGone));
        Assert.Equal("made0853", await db.ExecuteScalarAsync<string?>("SELECT login_id FROM employees WHERE user_id=@U", new { U = made }));
        Assert.Equal("keep0851", await db.ExecuteScalarAsync<string?>("SELECT login_id FROM employees WHERE user_id=@U", new { U = keep }));

        // 멱등 — 두 번째(다음 기동)는 0행
        Assert.Equal(new EmployeeLoginIdSync.Result(false, 0, 0), await EmployeeLoginIdSync.ResyncAsync(db, null));

        // 칸 없는 DB(DB-137 미적용) — 던지지 않고 건너뛴다(기동을 막지 않는다)
        await db.ExecuteAsync("ALTER TABLE employees DROP COLUMN login_id");
        Assert.Equal(new EmployeeLoginIdSync.Result(true, 0, 0), await EmployeeLoginIdSync.ResyncAsync(db, null));
    }

    // ══ G-E16 — R-1 원자 울타리 ══

    /// <summary>옛 울타리 판정(R-1 전 <c>EnsureStaffMayTouchAsync</c> 의 두 읽기) 재현 — 대조군 전용. 참 = 「바꿔도 되는 대상」.</summary>
    private async Task<bool> OldStaffJudgeAsync(MySqlConnection db, string userId)
    {
        var t = await db.QuerySingleOrDefaultAsync<(bool isParent, string? accountType)>(
            "SELECT is_parent, account_type FROM users WHERE user_id = @U AND tenant_id = @T AND is_deleted = 0",
            new { U = userId, T = _tenantA });
        if (t.isParent || string.Equals(t.accountType, "tenant_admin", StringComparison.OrdinalIgnoreCase)) return false;
        var perms = await db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM user_permissions
            WHERE user_id = @U AND tenant_id = @T AND menu_code IN ('USERS', 'USERS_ACCOUNT', 'USERS_SEAT') AND can_view = 1",
            new { U = userId, T = _tenantA });
        return perms == 0;
    }

    /// <summary>
    /// <paramref name="waiter"/> 연결의 트랜잭션이 행 잠금을 기다리는 상태(<c>LOCK WAIT</c>)에 들어갈 때까지 본다 —
    /// 「판정 뒤 · 바꾸기 전」 틈에 승격이 끼어드는 순간을 결정적으로 만든다. 그 전에 작업이 끝나면 경쟁이 재현되지 않은 것이다(FAIL).
    /// </summary>
    /// <remarks>
    /// 🔴 작5 §CI3 G-E16 — <c>information_schema.INNODB_TRX</c> 는 서버의 중간 캐시를 읽는다. 그 캐시는
    /// <b>마지막으로 읽힌 지 0.1초가 넘었을 때만</b> 새로 채워진다(MariaDB <c>trx0i_s.cc</c> <c>CACHE_MIN_IDLE_TIME_NS</c> ·
    /// <c>can_cache_be_updated</c> · <c>trx_i_s_cache_end_read</c> 가 읽을 때마다 <c>last_read</c> 를 갱신).
    /// 옛 탐침은 50ms 마다 읽어 <b>자기 읽기로 캐시를 얼렸다</b> — 첫 읽기가 낡은 스냅숏이면(같은 시험 안 직전 블록의 탐침이
    /// 0.1초 안에 읽었으면) 20초 내내 같은 스냅숏을 본다. G-E16 은 한 시험 안에서 탐침을 두 번 연달아 쓰는 유일한 게이트다.
    /// ⇒ 읽기 사이를 <see cref="TrxProbeIntervalMs"/>(&gt; 100ms) 로 벌리고, 첫 읽기 전에도 그만큼 쉰다(직전 탐침 읽기와 띄움).
    /// </remarks>
    private async Task WaitForLockWaitAsync(MySqlConnection waiter, Task work, string what)
    {
        await using var probe = await OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            // ⬛ [§CI3 전] 읽은 뒤 Task.Delay(50) — 0.1초 캐시를 스스로 얼렸다
            await Task.Delay(TrxProbeIntervalMs);
            if (work.IsCompleted)
            {
                // ⬛ var err = work.Exception?.GetBaseException().Message ?? "예외 없음";
                throw new Xunit.Sdk.XunitException($"{what} — 승격 커밋 전에 끝났다(행 잠금을 안 기다렸다 · 경쟁 미재현): {DescribeWork(work)}");
            }
            var waiting = await probe.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_state = 'LOCK WAIT' AND trx_mysql_thread_id = @Id",
                new { Id = waiter.ServerThread });
            if (waiting > 0) return;
        }
        // ⬛ throw new Xunit.Sdk.XunitException($"{what} — 20초 안에 행 잠금 대기에 들어가지 않았다(경쟁 미재현)");
        throw new Xunit.Sdk.XunitException(
            $"{what} — 20초 안에 행 잠금 대기에 들어가지 않았다(경쟁 미재현)\n{await LockDiagnosticsAsync(probe, waiter.ServerThread, work)}");
    }

    /// <summary>INNODB_TRX 캐시 최소 쉼(0.1초)보다 길게 — 매 읽기가 새 스냅숏이 되게.</summary>
    private const int TrxProbeIntervalMs = 150;

    /// <summary>작업 상태 한 줄 — 실패면 예외 종류·글(안쪽 예외까지). 예외를 읽어 「관찰 안 된 예외」로 남지 않게 한다.</summary>
    private static string DescribeWork(Task work)
    {
        if (!work.IsCompleted) return $"작업 상태={work.Status}(안 끝남)";
        if (work.Exception is null) return $"작업 상태={work.Status}";
        var parts = work.Exception.Flatten().InnerExceptions.Select(e =>
        {
            var s = $"{e.GetType().Name}: {e.Message}";
            for (var inner = e.InnerException; inner is not null; inner = inner.InnerException)
                s += $" ← {inner.GetType().Name}: {inner.Message}";
            return s;
        });
        return $"작업 상태={work.Status} · 작업 예외=[{string.Join(" | ", parts)}]";
    }

    /// <summary>
    /// 🔴 작5 §CI3 G-E16 — 잠금 대기를 못 봤을 때 남기는 표: 작업 상태 · 기다리는 쪽 스레드 · INNODB_TRX(새 스냅숏) ·
    /// 잠금 대기 대상(INNODB_LOCK_WAITS × INNODB_LOCKS) · PROCESSLIST(기다리는 쪽의 상태 — MDL 대기 등 InnoDB 밖 대기를 가른다).
    /// 진단 읽기 전에 캐시 쉼보다 길게 쉰다 — 여기서 기다리는 쪽이 LOCK WAIT 이면 「탐침이 낡은 스냅숏을 봤다」가 확정된다.
    /// 각 조회 실패는 표 자리에 그 글을 적는다(진단이 원래 실패를 가리지 않게).
    /// </summary>
    private static async Task<string> LockDiagnosticsAsync(MySqlConnection probe, int waiterThread, Task work)
    {
        await Task.Delay(TrxProbeIntervalMs);
        var sb = new System.Text.StringBuilder();
        sb.Append("── 진단 · ").Append(DescribeWork(work)).Append(" · 기다리는 쪽 스레드=").Append(waiterThread).AppendLine();

        async Task Table(string title, string sql)
        {
            sb.Append("[").Append(title).AppendLine("]");
            try
            {
                var rows = (await probe.QueryAsync(sql, new { Id = waiterThread })).ToList();
                if (rows.Count == 0) sb.AppendLine("  (행 없음)");
                foreach (IDictionary<string, object?> r in rows)
                    sb.Append("  ").AppendLine(string.Join(" · ", r.Select(kv => $"{kv.Key}={kv.Value}")));
            }
            catch (MySqlException ex)
            {
                sb.Append("  조회 실패: ").AppendLine(ex.Message);
            }
        }

        await Table("INNODB_TRX(새 스냅숏)", @"
            SELECT trx_mysql_thread_id AS thread, trx_state AS state, trx_started AS started, trx_wait_started AS wait_started,
                   trx_requested_lock_id AS req_lock, LEFT(IFNULL(trx_query, ''), 160) AS query
            FROM information_schema.INNODB_TRX ORDER BY trx_started LIMIT 20");
        await Table("잠금 대기 대상(INNODB_LOCK_WAITS × INNODB_LOCKS)", @"
            SELECT w.requesting_trx_id AS req_trx, w.blocking_trx_id AS blk_trx,
                   l.lock_mode AS mode, l.lock_type AS type, l.lock_table AS tbl, l.lock_index AS idx, l.lock_data AS data
            FROM information_schema.INNODB_LOCK_WAITS w
            LEFT JOIN information_schema.INNODB_LOCKS l ON l.lock_id = w.requested_lock_id
            LIMIT 20");
        await Table("PROCESSLIST(기다리는 쪽)", @"
            SELECT ID AS id, COMMAND AS cmd, STATE AS state, TIME AS secs, LEFT(IFNULL(INFO, ''), 160) AS info
            FROM information_schema.PROCESSLIST WHERE ID = @Id");
        return sb.ToString();
    }

    /// <summary>
    /// 🔴 작5 §CI3 G-E16 — 블록 안에서 실패했을 때 <b>연결 정리 전에</b> 작업을 끝까지 기다린다. 옛 판은 작업(Task.Run)이 아직 돌 때
    /// <c>await using</c> 정리(연결 DisposeAsync → 트랜잭션 Rollback)가 같은 연결에서 부딪혀 원래 예외를
    /// <c>NullReferenceException</c> · <c>another read operation is pending</c> 로 가렸다(CI 3차 로그 1·2차).
    /// 잠금을 쥔 트랜잭션을 먼저 되돌려(작업이 깨어나게) 최대 30초 기다리고, 원래 예외에 작업 결과를 붙여 다시 던진다.
    /// </summary>
    private static async Task<Exception> DrainOnFailureAsync(Exception first, Task work, MySqlTransaction holderTx, string what)
    {
        var notes = new System.Text.StringBuilder();
        if (!work.IsCompleted)
        {
            try
            {
                await holderTx.RollbackAsync();
                notes.Append("잠금 쥔 트랜잭션 되돌림 · ");
            }
            catch (InvalidOperationException rex)                    // 이미 커밋된 뒤 — 잠금은 이미 풀렸다
            {
                notes.Append("잠금 쥔 트랜잭션 되돌림 불가(").Append(rex.Message).Append(") · ");
            }
            catch (MySqlException mex)
            {
                notes.Append("잠금 쥔 트랜잭션 되돌림 실패(").Append(mex.Message).Append(") · ");
            }
            if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(30))) != work)
                notes.Append("작업이 30초 안에 안 끝났다(연결 정리가 부딪힐 수 있다) · ");
        }
        notes.Append(DescribeWork(work));
        return new Xunit.Sdk.XunitException($"{first.Message}\n── [{what}] 정리 전 작업 결과: {notes}", first);
    }

    private static async Task<bool> IsActiveAsync(MySqlConnection db, string userId) =>
        await db.ExecuteScalarAsync<bool>("SELECT is_active FROM users WHERE user_id = @U", new { U = userId });

    [Fact(DisplayName = "G-E16 🔴 R-1 — 2단계 직원의 [사용 안 함] 도중 대상이 관리자로 승격(미커밋 · 행 잠금) → 직원 UPDATE 가 잠금을 기다린 뒤 승격 커밋 → 403 protected_account · 대상 그대로 사용중 · 대조군(옛 두 단계 = 판정 따로 → SuspendAsync 는 같은 경쟁에서 관리자를 끈다)")]
    public async Task E16_Staff_Suspend_Atomic_Against_Promotion()
    {
        if (!Ready("G-E16")) return;
        string actor, target, targetOld;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            actor = await InsertUserAsync(seed, "act1601", "2단계직원");
            target = await InsertUserAsync(seed, "tgt1602", "대상");
            targetOld = await InsertUserAsync(seed, "tgt1603", "옛대상");
        }
        const string promote = "UPDATE users SET account_type = 'tenant_admin', role = 'TenantAdmin', updated_at = NOW(6) WHERE user_id = @U";

        // ── 봉합(원자 UPDATE) ──
        await using (var promoter = await OpenAsync())
        await using (var staffConn = await OpenAsync())
        {
            await using var ptx = await promoter.BeginTransactionAsync();
            await promoter.ExecuteAsync(promote, new { U = target }, ptx);                  // 대상 행 X 잠금(미커밋)

            var staff = Task.Run(() => new UserService(staffConn, new NoOpAudit()).SuspendAsStaffAsync(target, actor, _tenantA));
            // 🔴 §CI3 — 실패하면 연결 정리 전에 작업을 끝까지 기다리고 그 결과를 붙인다(원래 예외가 정리 예외에 가리지 않게).
            try
            {
                await WaitForLockWaitAsync(staffConn, staff, "봉합");
                await ptx.CommitAsync();                                                  // 판정 시점 뒤에 대상이 관리자가 됐다

                var ex = await Assert.ThrowsAsync<AccountActionForbiddenException>(() => staff);
                Assert.Equal("protected_account", ex.Code);
            }
            catch (Exception first)
            {
                throw await DrainOnFailureAsync(first, staff, ptx, "봉합");
            }
        }
        await using var db = await OpenAsync();
        Assert.True(await IsActiveAsync(db, target), "R-1 — 관리자로 바뀐 대상이 직원 손에 꺼졌다(원자화 실패)");
        Assert.Equal("tenant_admin", await db.ExecuteScalarAsync<string>("SELECT account_type FROM users WHERE user_id=@U", new { U = target }));

        // ── 🔴 대조군 — 옛 두 단계(R-1 전 SuspendAsStaffAsync = 옛 판정 → SuspendAsync), 같은 경쟁 ──
        await using (var promoter = await OpenAsync())
        await using (var staffConn = await OpenAsync())
        {
            await using var ptx = await promoter.BeginTransactionAsync();
            await promoter.ExecuteAsync(promote, new { U = targetOld }, ptx);

            var old = Task.Run(async () =>
            {
                Assert.True(await OldStaffJudgeAsync(staffConn, targetOld), "대조군 전제 — 판정 시점(커밋된 값)엔 바꿔도 되는 대상");
                await new UserService(staffConn, new NoOpAudit()).SuspendAsync(targetOld, _tenantA);
            });
            try
            {
                await WaitForLockWaitAsync(staffConn, old, "대조군");
                await ptx.CommitAsync();
                await old;                                                                // 옛 흐름은 성공한다
            }
            catch (Exception first)
            {
                throw await DrainOnFailureAsync(first, old, ptx, "대조군");
            }
        }
        Assert.False(await IsActiveAsync(db, targetOld), "대조군 무효 — 옛 두 단계가 같은 경쟁에서 막혔다(경쟁 재현 실패)");
        Assert.Equal("tenant_admin", await db.ExecuteScalarAsync<string>("SELECT account_type FROM users WHERE user_id=@U", new { U = targetOld }));
    }

    /// <summary>격리 DB 연결을 운영 <c>AuthService</c> 에 넘겨주는 통로. 판정하지 않는다(선례 SessionRecordConcurrentPcGateTests.GateUnitOfWork).</summary>
    private sealed class LinkGateUnitOfWork : IUnitOfWork
    {
        private readonly DbConnection _conn;
        public LinkGateUnitOfWork(DbConnection conn) => _conn = conn;

        public DbConnection GetDbConnection() => _conn;

        // 로그인 성공 때 LastLoginAt·실패 횟수를 EF 로 저장하는 자리 — role 클레임과 무관하다.
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

        // 여기 오는 길은 부모계정 사원 백필 하나뿐(운영 try/catch 가 받는다) — 이 게이트의 계정은 사원 행이 있어 안 온다.
        public IRepository<T> Repository<T>() where T : BaseEntity =>
            throw new NotSupportedException("게이트는 EF 리포지토리를 태우지 않는다(사원 백필 경로 아님).");

        public Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("게이트는 EF 공유 트랜잭션을 태우지 않는다.");

        public void Dispose() { }
    }
}
