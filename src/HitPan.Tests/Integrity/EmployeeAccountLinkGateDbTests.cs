using Dapper;
using HitPan.API.Services;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 2026-10-05 작5 — 사원 ↔ 계정 양방향 연결 게이트의 <b>DB 몫</b>
/// (설계 <c>docs/설계/erp/20261005_설계_사원계정연결_아이디표기.md</c> §8 G-E1~E8 · G-E12).
/// </summary>
/// <remarks>
/// <para>실물 <see cref="UserService"/> · <see cref="EmployeeService"/> · <see cref="CompanyBootstrapProvisioner"/> 를
/// 출하 DDL(<c>installer/hitpan_db_clean.sql</c>) 위 격리 DB 에 붙여 잰다 — 흉내가 아니다(선례 <c>ApprovalRetiredAccessGateDbTests</c>).</para>
/// <para>🔴 <b>갈래 4(게이트)는 갈래 1·2 와 동시에 썼다</b> — 이 파일을 쓸 때 제품 쪽 새 칸·새 함수(<c>employees.login_id</c> ·
/// <c>EmployeeListDto.AccountStatus/IsLeaver/LoginId</c> · <c>UserService.CreateForEmployeeAsync</c> · <c>DB-137_*.sql</c>)가 아직 없었다.
/// 그래서 그 자리는 <b>리플렉션·SQL·파일 경로</b>로 부른다(빌드 0/0 유지). 없으면 「갈래N 합류 전」 문구로 <b>FAIL</b> 한다 —
/// SKIP 이 아니다(없는 봉합을 통과로 세지 않는다). 표시: <c>// 갈래2 합류 후 연결</c>.</para>
/// <para>🟢 10/5 §연결 — 갈래2 합류 뒤 리플렉션 호출은 <b>직접 호출</b>로 바꿨다(<c>CreateForEmployeeAsync</c> · DTO 칸).
/// 남은 「갈래1 합류 전」 문구는 SQL·파일 경로 판정이라 그대로 둔다(칸·파일이 없으면 FAIL).</para>
/// <para>⚠️ 개발 PC 는 SKIP 이 정상(<c>hitpan</c> 은 CREATE DATABASE 거부) — CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB=1</c>)가 FAIL 로 바꾼다.
/// <b>로컬 초록은 증거가 아니다.</b> draft PR 의 <c>db-gate</c> 가 유일한 계측 경로.</para>
/// <para>🔴 대조군 — 각 시험 안에서 <b>봉합 전 판정(옛 SQL·옛 경로)</b>이 같은 줄을 어떻게 봤는지 다시 잰다. 대조군이 「뚫렸을 것」을
/// 보여 주지 못하면 그 시험의 초록은 아무것도 증명하지 않는다 ⇒ 대조군 단언이 함께 실패한다.</para>
/// <para>연결 문자열은 <see cref="MySqlConnectionStringBuilder"/> 로 만든다(비밀 스캔) · 격리 DB 연결은 풀을 끈다.</para>
/// </remarks>
[Collection("EmployeeAccountLinkGateDb")]
public sealed partial class EmployeeAccountLinkGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_eal_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly string _tenantA = Guid.NewGuid().ToString();
    private string _ownerId = "";

    private const string Pw = "Temp1234!";

    // ══ 준비물 — ApprovalRetiredAccessGateDbTests 와 같은 방식(출하 DDL 위 격리 DB) ══
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static MySqlConnectionStringBuilder ServerBuilder() => new()
    {
        Server = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost",
        Port = uint.TryParse(Environment.GetEnvironmentVariable("HITPAN_DB_PORT"), out var p) ? p : 3306,
        UserID = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root",
        Password = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "",
        DefaultCommandTimeout = 90,
        GuidFormat = MySqlGuidFormat.None,
        AllowUserVariables = true,
    };

    private static string ServerConnString() => ServerBuilder().ConnectionString;

    private string DbConnString()
    {
        var b = ServerBuilder();
        b.Database = _dbName;
        b.Pooling = false;
        return b.ConnectionString;
    }

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private bool Ready(string gate)
    {
        if (!DbGateEnvironment.IsCi)
        {
            var ok = File.Exists(MysqlExe());
            if (ok)
            {
                try
                {
                    using var c = new MySqlConnection(ServerConnString());
                    c.Open();
                    c.Execute($"CREATE DATABASE IF NOT EXISTS `{_dbName}`");
                    c.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
                }
                catch (MySqlException ex)
                {
                    Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                    ok = false;
                }
            }
            if (!ok) return !DbGateEnvironment.SkipOrFail(gate);
        }
        SetUpFreshInstall();
        return true;
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패:\n{err}");
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[EmployeeAccountLinkGateDb] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ══ 씨앗 ══

    /// <summary>회사 + 구독(maxUsers) + 대표(is_parent=1 · 아이디 owner01). 대표 사원 행은 시험이 필요할 때만 만든다.</summary>
    private async Task SeedCompanyAsync(MySqlConnection db, int maxUsers = 50)
    {
        await db.ExecuteAsync(
            "INSERT INTO local_company (tenant_id, tenant_code, company_name, is_locked_from_landing) VALUES (@T, 'T1', '시험회사', 1)",
            new { T = _tenantA });
        await db.ExecuteAsync(
            "INSERT INTO local_subscription (tenant_id, max_users, extra_accounts) VALUES (@T, @M, 0)",
            new { T = _tenantA, M = maxUsers });
        _ownerId = await InsertUserAsync(db, "owner01", "대표", parent: true);
    }

    /// <summary>users 한 줄 — 상태를 시험이 지정한다(<c>is_active</c>·<c>is_deleted</c>·관리자 여부).</summary>
    private async Task<string> InsertUserAsync(MySqlConnection db, string email, string name, bool parent = false,
        bool active = true, bool deleted = false, bool admin = false)
    {
        var id = Guid.NewGuid().ToString();
        var isAdmin = parent || admin;
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (@U, @T, @E, 'x', @N, @R, @A, @P, @Act, @Del, NOW(6), NOW(6))",
            new
            {
                U = id, T = _tenantA, E = email, N = name,
                R = isAdmin ? "TenantAdmin" : "User",
                A = isAdmin ? "tenant_admin" : "tenant_user",
                P = parent ? 1 : 0,
                Act = active ? 1 : 0,
                Del = deleted ? 1 : 0
            });
        return id;
    }

    /// <summary>employees 한 줄(NOT NULL 칸은 출하 DDL :1127 판독 — #13). user_id 는 죽은 연결도 넣을 수 있다.</summary>
    private async Task<string> InsertEmployeeAsync(MySqlConnection db, string empNo, string name, string? userId = null,
        bool active = true, bool resigned = false)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO employees (employee_id, tenant_id, user_id, emp_no, emp_name, position, emp_type,
                                   join_date, is_active, is_resigned, created_at, updated_at)
            VALUES (@Id, @T, @U, @No, @N, '사원', 'regular', NOW(6), @Act, @Res, NOW(6), NOW(6))",
            new { Id = id, T = _tenantA, U = userId, No = empNo, N = name, Act = active ? 1 : 0, Res = resigned ? 1 : 0 });
        return id;
    }

    private async Task<long> CountAsync(MySqlConnection db, string table) =>
        await db.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE tenant_id=@T", new { T = _tenantA });

    private static async Task<(string? userId, string? loginId)> EmpLinkAsync(MySqlConnection db, string employeeId) =>
        await db.QuerySingleAsync<(string?, string?)>(
            "SELECT user_id, login_id FROM employees WHERE employee_id=@E", new { E = employeeId });   // login_id = DB-137(갈래1)

    // ══ 새 제품 함수 — 실물 직접 호출(§연결 · 10/5 갈래2 합류 후) ══
    // ⬛ [합류 전] 리플렉션(GetMethod("CreateForEmployeeAsync") + BuildArg 이름 맞추기)으로 불렀다. 갈래2 의 계약
    //   (개발명세서 §4 게이트 진입점)이 들어와 직접 호출로 바꿨다 — 인자 이름이 바뀌면 이제 빌드가 깨진다(조용한 오배선 없음).
    //   ⚠️ 리플렉션 판은 bool actorIsAdmin 을 Activator 기본값(false)으로 넘겼다 — 같은 뜻을 기본값으로 유지한다.

    /// <summary>
    /// <see cref="UserService.CreateForEmployeeAsync"/>(설계 §3 · 개발명세서 §4) — 요청 DTO <see cref="CreateForEmployeeDto"/>.
    /// <paramref name="actorIsAdmin"/> 거짓 = 2단계 직원이 부른 것(일반 직무만 · 만드는 계정 User 고정).
    /// </summary>
    private static Task<string> CreateForEmployeeAsync(UserService svc, string tenantId, string employeeId, string loginId,
        string? role = null, bool actorIsAdmin = false) =>
        svc.CreateForEmployeeAsync(
            new CreateForEmployeeDto { EmployeeId = employeeId, LoginId = loginId, Password = Pw, Role = role },
            tenantId, actorIsAdmin, CancellationToken.None);

    private static async Task<List<EmployeeListDto>> ListAsync(MySqlConnection db, string tenantId) =>
        await new EmployeeService(db, new NoOpAudit()).GetListAsync(tenantId, includeResigned: true);

    /// <summary>옛 판정 재현 — 지금 <c>GetListAsync</c> 의 <c>HasUserAccount</c> 식 그대로(:89·:94-98). 이 값이 바뀌면 안 된다(§2-1).</summary>
    private async Task<Dictionary<string, int>> OldHasUserAccountAsync(MySqlConnection db) =>
        (await db.QueryAsync<(string id, int has)>(@"
            SELECT e.employee_id, CASE WHEN u.user_id IS NULL THEN 0 ELSE 1 END
            FROM employees e
            LEFT JOIN users u ON u.user_id = e.user_id AND u.tenant_id = e.tenant_id AND u.is_deleted = 0 AND u.is_active = 1
            WHERE e.tenant_id = @T", new { T = _tenantA })).ToDictionary(x => x.id, x => x.has);

    /// <summary>
    /// 상태 판정식(설계 §0·§2-1) — <paramref name="filterActive"/>=true 면 <b>옛 조인</b>(is_active 거름)에 같은 CASE 를 얹은 것 = 대조군.
    /// </summary>
    private async Task<Dictionary<string, string>> StatusBySqlAsync(MySqlConnection db, bool filterActive) =>
        (await db.QueryAsync<(string id, string st)>($@"
            SELECT e.employee_id,
                   CASE WHEN ua.user_id IS NULL OR ua.is_deleted = 1 THEN 'none'
                        WHEN ua.is_parent = 1 THEN 'owner'
                        WHEN ua.is_active = 1 THEN 'active'
                        ELSE 'suspended' END
            FROM employees e
            LEFT JOIN users ua ON ua.user_id = e.user_id AND ua.tenant_id = e.tenant_id
                 {(filterActive ? "AND ua.is_active = 1" : "")}
            WHERE e.tenant_id = @T", new { T = _tenantA })).ToDictionary(x => x.id, x => x.st);

    // ══ G-E1 ══

    [Fact(DisplayName = "G-E1 🔴 미등록 사원에 for-employee → 사원 행 수 불변 · 그 행 user_id·login_id 채움 · users 1행 · 대조군(옛 계정 추가는 쌍둥이 사원 +1 · 연결 판정기가 「users 만 있고 연결 없음」을 FAIL 로 본다)")]
    public async Task E1_ForEmployee_Links_Without_Twin()
    {
        if (!Ready("G-E1")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var emp = await InsertEmployeeAsync(db, "0101", "김미등록");
        var svc = new UserService(db, new NoOpAudit());

        var emps0 = await CountAsync(db, "employees");
        var users0 = await CountAsync(db, "users");
        await CreateForEmployeeAsync(svc, _tenantA, emp, "kim0101");

        Assert.Equal(emps0, await CountAsync(db, "employees"));              // 사원 행 INSERT 0(쌍둥이 차단)
        Assert.Equal(users0 + 1, await CountAsync(db, "users"));
        var newUser = await db.QuerySingleAsync<(string id, string empName, string acct)>(
            "SELECT user_id, emp_name, account_type FROM users WHERE tenant_id=@T AND email='kim0101'", new { T = _tenantA });
        Assert.Equal("김미등록", newUser.empName);                            // 이름은 사원 값
        Assert.True(await LinkedOkAsync(db, emp, "kim0101"), "사원 행이 새 계정에 연결되지 않았다(7단계 UPDATE)");
        var (uid, lid) = await EmpLinkAsync(db, emp);
        Assert.Equal(newUser.id, uid);
        Assert.Equal("kim0101", lid);

        // 🔴 대조군 ① — 7단계 UPDATE 를 뺀 상태(users 만 생김)를 손으로 만들면 연결 판정기가 FAIL 로 본다
        var emp2 = await InsertEmployeeAsync(db, "0102", "박반쪽");
        await InsertUserAsync(db, "park0102", "박반쪽");
        Assert.False(await LinkedOkAsync(db, emp2, "park0102"), "대조군 무효 — 판정기가 연결 없음을 못 잡는다");

        // 🔴 대조군 ② — 옛 경로(계정 추가 = 새 사원과 함께)는 같은 사람을 넣으면 사원 행이 하나 더 생긴다(쌍둥이)
        var before = await CountAsync(db, "employees");
        await svc.CreateAsync(new CreateUserDto { Email = "park-old", UserName = "박반쪽", Password = Pw, Role = "User" }, _tenantA);
        Assert.Equal(before + 1, await CountAsync(db, "employees"));
    }

    private async Task<bool> LinkedOkAsync(MySqlConnection db, string employeeId, string loginId) =>
        await db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM employees e
            JOIN users u ON u.user_id = e.user_id AND u.tenant_id = e.tenant_id AND u.is_deleted = 0
            WHERE e.tenant_id=@T AND e.employee_id=@E AND u.email=@L", new { T = _tenantA, E = employeeId, L = loginId }) == 1;

    // ══ G-E2 ══

    [Fact(DisplayName = "G-E2 🔴 퇴사 사원(is_active=0) · MDB 모양(1·1) 사원 → 둘 다 거절(퇴사) · users 0행 · IsLeaver=1 · 대조군(is_active 만 보는 옛 판별은 MDB 사원을 재직으로 본다)")]
    public async Task E2_Leaver_Including_Mdb_Shape_Rejected()
    {
        if (!Ready("G-E2")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var gone = await InsertEmployeeAsync(db, "0201", "퇴사자", active: false, resigned: true);
        var mdb = await InsertEmployeeAsync(db, "MIG-0202", "이관퇴사자", active: true, resigned: true);
        var svc = new UserService(db, new NoOpAudit());
        var users0 = await CountAsync(db, "users");

        foreach (var (emp, id) in new[] { (gone, "gone0201"), (mdb, "mdb0202") })
        {
            // ⬛ [합류 전] ThrowsAnyAsync<Exception> + 문구 「퇴사」만 — 계약(§4 ④)이 들어와 코드까지 잰다
            var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => CreateForEmployeeAsync(svc, _tenantA, emp, id));
            Assert.Equal("employee_leaver", ex.Code);
            Assert.Contains("퇴사", ex.Message);
        }
        Assert.Equal(users0, await CountAsync(db, "users"));
        Assert.Null((await EmpLinkAsync(db, mdb)).userId);

        var rows = await ListAsync(db, _tenantA);
        foreach (var e in new[] { gone, mdb })
            Assert.True(rows.Single(r => r.EmployeeId == e).IsLeaver, $"IsLeaver 가 참이 아니다: {e}");

        // 🔴 대조군 — 판별을 is_active 만으로 하면(옛 판정) MDB 모양 사원은 「재직」 = 만들기가 통과했을 자리
        var oldLeaver = await db.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN is_active = 0 THEN 1 ELSE 0 END FROM employees WHERE employee_id=@E", new { E = mdb });
        Assert.Equal(0, oldLeaver);
    }

    // ══ G-E3 ══

    [Fact(DisplayName = "G-E3 🔴 계정 한도 꽉 참 → for-employee 거절(AccountSeatFull) · users·employees·연결 무변화 · 대조군(판정기 없으면 DB 는 6번째 계정을 그냥 받는다)")]
    public async Task E3_Seat_Full_Rolls_Back()
    {
        if (!Ready("G-E3")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, maxUsers: 5);
        var svc = new UserService(db, new NoOpAudit());
        for (var i = 0; i < 4; i++)
            await svc.CreateAsync(new CreateUserDto { Email = $"fill{i}", UserName = $"채움{i}", Password = Pw, Role = "User" }, _tenantA);
        var seats = await AccountSeatGuard.GetSeatsAsync(db, _tenantA, default);
        Assert.True(seats.Active >= seats.Limit, $"전제 — 한도가 차야 한다({seats.Active}/{seats.Limit})");

        var emp = await InsertEmployeeAsync(db, "0301", "한도막힘");
        var users0 = await CountAsync(db, "users");
        var emps0 = await CountAsync(db, "employees");
        await Assert.ThrowsAsync<AccountSeatFullException>(() => CreateForEmployeeAsync(svc, _tenantA, emp, "full0301"));
        Assert.Equal(users0, await CountAsync(db, "users"));
        Assert.Equal(emps0, await CountAsync(db, "employees"));
        Assert.Equal((null, null), await EmpLinkAsync(db, emp));

        // 🔴 대조군 — 판정기 호출이 빠지면 막을 것이 없다: 같은 회사에 6번째 활성 계정 INSERT 가 DB 에서 그냥 된다(되돌림)
        await using var tx = await db.BeginTransactionAsync();
        var n = await db.ExecuteAsync(@"INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_active, is_deleted, created_at, updated_at)
                                        VALUES (UUID(), @T, 'nojudge', 'x', 'n', 'User', 'tenant_user', 1, 0, NOW(6), NOW(6))", new { T = _tenantA }, tx);
        Assert.Equal(1, n);
        await tx.RollbackAsync();
    }

    // ══ G-E4 ══

    [Fact(DisplayName = "G-E4 🔴 두 연결이 같은 사원 동시 연결 → 정확히 1 성공 · users +1 · 사원은 성공한 계정에 연결 · 대조군(FOR UPDATE·조건부 UPDATE 없는 옛 흐름은 2 성공 · 조건부 UPDATE 하나만 있어도 두 번째 affected=0)")]
    public async Task E4_Concurrent_Link_Exactly_One()
    {
        if (!Ready("G-E4")) return;
        string emp;
        long users0;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed);
            emp = await InsertEmployeeAsync(seed, "0401", "동시연결");
            users0 = await CountAsync(seed, "users");
        }

        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        var t1 = Task.Run(() => CreateForEmployeeAsync(new UserService(c1, new NoOpAudit()), _tenantA, emp, "race-a"));
        var t2 = Task.Run(() => CreateForEmployeeAsync(new UserService(c2, new NoOpAudit()), _tenantA, emp, "race-b"));
        var ok = 0;
        foreach (var t in new[] { t1, t2 })
        {
            try { await t; ok++; }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                Console.Error.WriteLine($"[G-E4] 진 쪽: {ex.GetType().Name} {ex.Message}");
            }
        }
        // 게이트 자체 실패(XunitException)는 위 when 절로 그대로 올라온다(진 쪽으로 세지 않는다).
        await using var db = await OpenAsync();
        Assert.Equal(1, ok);
        Assert.Equal(users0 + 1, await CountAsync(db, "users"));
        var (uid, lid) = await EmpLinkAsync(db, emp);
        Assert.NotNull(uid);
        Assert.Equal(lid, await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = uid }));

        // 🔴 대조군 — 잠금·조건 없는 옛 흐름: 둘 다 「비었다」고 읽고 둘 다 덮어쓴다 ⇒ 2 성공 · 앞 계정은 고아
        var emp2 = await InsertEmployeeAsync(db, "0402", "옛흐름");
        await using var d1 = await OpenAsync();
        await using var d2 = await OpenAsync();
        await using var x1 = await d1.BeginTransactionAsync();
        await using var x2 = await d2.BeginTransactionAsync();
        const string read = "SELECT user_id FROM employees WHERE employee_id=@E";                 // FOR UPDATE 없음
        Assert.Null(await d1.ExecuteScalarAsync<string?>(read, new { E = emp2 }, x1));
        Assert.Null(await d2.ExecuteScalarAsync<string?>(read, new { E = emp2 }, x2));
        const string blind = "UPDATE employees SET user_id=@U WHERE employee_id=@E";               // 조건 없음
        var a1 = await d1.ExecuteAsync(blind, new { U = Guid.NewGuid().ToString(), E = emp2 }, x1);
        await x1.CommitAsync();
        var a2 = await d2.ExecuteAsync(blind, new { U = Guid.NewGuid().ToString(), E = emp2 }, x2);
        await x2.CommitAsync();
        Assert.Equal(2, a1 + a2);                                                                   // 옛 흐름 = 2 성공 관찰

        // 대조군 짝 — 조건부 UPDATE(설계 §3 7단계) 하나만 있어도 늦은 쪽은 affected=0 ⇒ 409 로 갈 자리
        var emp3 = await InsertEmployeeAsync(db, "0403", "조건부");
        const string guarded = "UPDATE employees SET user_id=@U WHERE employee_id=@E AND (user_id IS NULL OR user_id='')";
        Assert.Equal(1, await db.ExecuteAsync(guarded, new { U = Guid.NewGuid().ToString(), E = emp3 }));
        Assert.Equal(0, await db.ExecuteAsync(guarded, new { U = Guid.NewGuid().ToString(), E = emp3 }));
    }

    // ══ G-E5 ══

    [Fact(DisplayName = "G-E5 🔴 사용중지 계정을 가진 사원 → 두 번째 계정 거절(이미 계정) · users 무변화 · 대조군(옛 HasUserAccount 는 그 사원을 0 = 「계정 없음」으로 본다)")]
    public async Task E5_Suspended_Account_Blocks_Second()
    {
        if (!Ready("G-E5")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var sus = await InsertUserAsync(db, "sus0501", "중지된사람", active: false);
        var emp = await InsertEmployeeAsync(db, "0501", "중지된사람", userId: sus);
        var svc = new UserService(db, new NoOpAudit());
        var users0 = await CountAsync(db, "users");

        // ⬛ [합류 전] ThrowsAnyAsync<Exception> + 문구만 — 계약(§4 ④ employee_has_account)으로 잰다
        var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(() => CreateForEmployeeAsync(svc, _tenantA, emp, "second0501"));
        Assert.Equal("employee_has_account", ex.Code);
        Assert.Contains("이미 계정", ex.Message);
        Assert.Equal(users0, await CountAsync(db, "users"));
        Assert.Equal(sus, (await EmpLinkAsync(db, emp)).userId);

        // 🔴 대조군 — 옛 판정(is_active=1 조인)은 이 사원을 「계정 없음」으로 본다 ⇒ is_deleted=0 검사가 없으면 두 번째 계정이 만들어질 자리
        Assert.Equal(0, (await OldHasUserAccountAsync(db))[emp]);
    }

    // ══ G-E6 ══

    [Fact(DisplayName = "G-E6 🔴 죽은 연결(users is_deleted=1 · 행 없음) → 상태 none · 만들기 성공 · user_id 새 계정으로 교체")]
    public async Task E6_Dead_Link_Is_None_And_Replaced()
    {
        if (!Ready("G-E6")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var deadUser = await InsertUserAsync(db, "retired+x+old0601", "폐기됨", active: false, deleted: true);
        var empDeleted = await InsertEmployeeAsync(db, "0601", "죽은연결1", userId: deadUser);
        var ghost = Guid.NewGuid().ToString();
        var empGhost = await InsertEmployeeAsync(db, "0602", "죽은연결2", userId: ghost);

        var rows = await ListAsync(db, _tenantA);
        foreach (var e in new[] { empDeleted, empGhost })
            Assert.Equal("none", rows.Single(r => r.EmployeeId == e).AccountStatus);

        var svc = new UserService(db, new NoOpAudit());
        await CreateForEmployeeAsync(svc, _tenantA, empDeleted, "new0601");
        await CreateForEmployeeAsync(svc, _tenantA, empGhost, "new0602");
        Assert.True(await LinkedOkAsync(db, empDeleted, "new0601"));
        Assert.True(await LinkedOkAsync(db, empGhost, "new0602"));
        Assert.NotEqual(deadUser, (await EmpLinkAsync(db, empDeleted)).userId);
        Assert.NotEqual(ghost, (await EmpLinkAsync(db, empGhost)).userId);
    }

    // ══ G-E7 ══

    [Fact(DisplayName = "G-E7 🔴 상태 4종(owner·active·suspended·none) · 기존 HasUserAccount 값 불변 · 대조군(조인에 is_active 걸면 suspended 가 none 으로 사라진다)")]
    public async Task E7_Status_Four_Kinds()
    {
        if (!Ready("G-E7")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var act = await InsertUserAsync(db, "act0701", "사용중");
        var sus = await InsertUserAsync(db, "sus0702", "사용중지", active: false);
        var del = await InsertUserAsync(db, "retired+y+del0703", "폐기", active: false, deleted: true);
        var expect = new Dictionary<string, string>
        {
            [await InsertEmployeeAsync(db, "0001", "대표", userId: _ownerId)] = "owner",
            [await InsertEmployeeAsync(db, "0701", "사용중", userId: act)] = "active",
            [await InsertEmployeeAsync(db, "0702", "사용중지", userId: sus)] = "suspended",
            [await InsertEmployeeAsync(db, "0703", "폐기", userId: del)] = "none",
            [await InsertEmployeeAsync(db, "0704", "미등록")] = "none",
        };

        var rows = await ListAsync(db, _tenantA);
        foreach (var (emp, st) in expect)
            Assert.True(st == rows.Single(r => r.EmployeeId == emp).AccountStatus,
                $"{emp} 상태 기대 {st}");

        // 기존 HasUserAccount 는 그대로(§2-1 · 읽는 화면 3곳) — 옛 식으로 따로 잰 값과 같아야 한다
        var old = await OldHasUserAccountAsync(db);
        foreach (var r in rows) Assert.True(old[r.EmployeeId] == (r.HasUserAccount ? 1 : 0), $"HasUserAccount 가 바뀌었다: {r.EmpNo}");

        // 🔴 대조군 — 같은 CASE 를 옛 조인(is_active=1 거름)에 얹으면 suspended 가 none 으로 사라진다
        var ctl = await StatusBySqlAsync(db, filterActive: true);
        var susEmp = expect.Single(x => x.Value == "suspended").Key;
        Assert.Equal("none", ctl[susEmp]);
        Assert.Equal("suspended", (await StatusBySqlAsync(db, filterActive: false))[susEmp]);   // 설계 식은 살린다
    }

    // ══ G-E8 ══

    /// <summary>
    /// 불변식 — <c>employees.login_id</c> = (연결된 살아 있는 계정의 <c>users.email</c>) · 없으면 NULL. 어긋난 사원 수를 센다.
    /// 사용중지(is_active=0·is_deleted=0)는 계정이 있으므로 사본을 유지한다(설계 §2-2 표).
    /// </summary>
    private async Task<long> LoginIdDriftAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM employees e
            LEFT JOIN users u ON u.user_id = e.user_id AND u.tenant_id = e.tenant_id AND u.is_deleted = 0
            WHERE e.tenant_id = @T AND NOT (e.login_id <=> u.email)", new { T = _tenantA });

    [Fact(DisplayName = "G-E8 🔴 login_id 동기 6자리 — Create·CreateForEmployee·Retire·Resign·부트스트랩·재시드 · 각 사건 뒤 사본=계정 아이디(없으면 NULL) · 대조군(한 자리를 빼면 = 사본 하나 낡음 ⇒ 판정기가 1 을 센다)")]
    public async Task E8_LoginId_Sync_Six_Sites()
    {
        if (!Ready("G-E8")) return;

        // ⑤ 부트스트랩 — 설치 때 대표 사원(CreateParentAsync → SeedOwnerEmployeeAsync :277)
        await using (var seed = await OpenAsync())
        {
            await seed.ExecuteAsync(
                "INSERT INTO local_company (tenant_id, tenant_code, company_name, is_locked_from_landing) VALUES (@T, 'T1', '시험회사', 1)",
                new { T = _tenantA });
            await seed.ExecuteAsync("INSERT INTO local_subscription (tenant_id, max_users, extra_accounts) VALUES (@T, 50, 0)", new { T = _tenantA });
        }
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = DbConnString() })
            .Build();
        var prov = new CompanyBootstrapProvisioner(cfg, NullLogger<CompanyBootstrapProvisioner>.Instance);
        var (outcome, msg, ownerId) = await prov.CreateParentAsync(
            new ProofPayload { Sub = _tenantA, TenantCode = "T1", CompanyName = "시험회사" },
            new CreateParentInput { LoginId = "owner01", Password = "Owner1234!", Name = "대표" }, default);
        Assert.True(outcome == CompanyBootstrapProvisioner.CreateParentOutcome.Ok, msg);

        await using var db = await OpenAsync();
        Assert.Equal("owner01", await db.ExecuteScalarAsync<string?>(
            "SELECT login_id FROM employees WHERE tenant_id=@T AND user_id=@U", new { T = _tenantA, U = ownerId }));   // 갈래1 합류 후 칸 생김
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // ⑥ 재시드 — 초기화로 대표 사원이 지워진 뒤(:622) 되살리면 아이디 = parent.Email
        await db.ExecuteAsync("DELETE FROM employees WHERE tenant_id=@T AND user_id=@U", new { T = _tenantA, U = ownerId });
        await prov.ReseedCompanySkeletonAsync(_tenantA, default);
        Assert.Equal("owner01", await db.ExecuteScalarAsync<string?>(
            "SELECT login_id FROM employees WHERE tenant_id=@T AND user_id=@U", new { T = _tenantA, U = ownerId }));
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // ① Create(새 사원과 함께)
        var svc = new UserService(db, new NoOpAudit());
        var u1 = await svc.CreateAsync(new CreateUserDto { Email = "c0801", UserName = "새사원", Password = Pw, Role = "User" }, _tenantA);
        Assert.Equal("c0801", await db.ExecuteScalarAsync<string?>("SELECT login_id FROM employees WHERE user_id=@U", new { U = u1 }));
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // ② CreateForEmployee(기존 사원)
        var emp2 = await InsertEmployeeAsync(db, "0802", "기존사원");
        await CreateForEmployeeAsync(svc, _tenantA, emp2, "f0802");
        Assert.Equal("f0802", (await EmpLinkAsync(db, emp2)).loginId);
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // 사용 안 함 — 계정은 있다 ⇒ 사본 유지(무변경 자리)
        await svc.SuspendAsync(u1, _tenantA);
        Assert.Equal("c0801", await db.ExecuteScalarAsync<string?>("SELECT login_id FROM employees WHERE tenant_id=@T AND emp_name='새사원'", new { T = _tenantA }));
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // ③ Retire(계정폐기) — user_id NULL ⇒ login_id NULL
        await svc.RetireAsync(u1, _tenantA);
        Assert.Null(await db.ExecuteScalarAsync<string?>("SELECT login_id FROM employees WHERE tenant_id=@T AND emp_name='새사원'", new { T = _tenantA }));
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // ④ Resign(퇴사) — user_id NULL ⇒ login_id NULL
        var es = new EmployeeService(db, new NoOpAudit());
        Assert.True(await es.ResignAsync(_tenantA, emp2, DateTime.UtcNow.Date, "게이트"));
        Assert.Equal((null, null), await EmpLinkAsync(db, emp2));
        Assert.Equal(0, await LoginIdDriftAsync(db));

        // 🔴 대조군 — 한 자리를 빼먹은 결과(사본 하나 낡음)를 손으로 만들면 판정기가 정확히 1 을 센다
        var emp9 = await InsertEmployeeAsync(db, "0809", "낡은사본");
        await CreateForEmployeeAsync(svc, _tenantA, emp9, "f0809");
        await db.ExecuteAsync("UPDATE employees SET login_id='stale-copy' WHERE employee_id=@E", new { E = emp9 });
        Assert.Equal(1, await LoginIdDriftAsync(db));
    }

    // ══ G-E12 ══

    private static string Db137Path()
    {
        var dir = Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");
        var f = Directory.GetFiles(dir, "DB-137_*.sql");
        if (f.Length != 1)
            throw new Xunit.Sdk.XunitException($"갈래1 합류 전 — DB-137_*.sql 이 {f.Length}개다(설계 §0 · 정확히 1개여야 한다).");
        return f[0];
    }

    private sealed record ColShape(string ColumnType, string IsNullable);

    private static async Task<ColShape?> LoginIdShapeAsync(MySqlConnection db) =>
        await db.QuerySingleOrDefaultAsync<ColShape>(@"
            SELECT COLUMN_TYPE AS ColumnType, IS_NULLABLE AS IsNullable FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='employees' AND COLUMN_NAME='login_id'");

    private static async Task<string?> UniqueColsAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<string?>(@"
            SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='employees' AND INDEX_NAME='uq_employees_tenant_user' AND NON_UNIQUE=0");

    /// <summary>출하 DDL(갈래1 이후 새 칸·UNIQUE 포함)을 <b>DB-137 이전 모양</b>으로 되돌린다 — 고객 DB 가 업데이트 직전에 있는 모양.</summary>
    private static async Task RollBackTo136Async(MySqlConnection db)
    {
        if (await UniqueColsAsync(db) is not null)
            await db.ExecuteAsync("ALTER TABLE employees DROP INDEX uq_employees_tenant_user");
        if (await LoginIdShapeAsync(db) is not null)
            await db.ExecuteAsync("ALTER TABLE employees DROP COLUMN login_id");
        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id='DB-137'");
    }

    [Fact(DisplayName = "G-E12 🔴 DB-137 — 빈 DB · 채우기(살아 있는 계정만 · resigned+/retired+/삭제 ⇒ NULL) · 2회 실행 멱등 · 출하 DDL 과 같은 모양(ddl-smoke)")]
    public async Task E12_Db137_Fill_Idempotent_Same_Shape()
    {
        if (!Ready("G-E12")) return;
        var sql = await File.ReadAllTextAsync(Db137Path());
        await using var db = await OpenAsync();

        // 출하 DDL(#36 단일 진실원)의 모양을 먼저 적는다
        var shipShape = await LoginIdShapeAsync(db);
        var shipUq = await UniqueColsAsync(db);
        Assert.True(shipShape is not null, "갈래1 합류 전 — 출하 DDL employees 에 login_id 가 없다(#36 동시 반영)");
        Assert.Equal("tenant_id,user_id", shipUq);

        // 빈 DB(사원 0) — 업데이트 직전 모양으로 되돌린 뒤 실행
        await RollBackTo136Async(db);
        await db.ExecuteAsync(sql);
        Assert.Equal(shipShape, await LoginIdShapeAsync(db));
        Assert.Equal(shipUq, await UniqueColsAsync(db));

        // 채우기 — 다시 이전 모양 + 자료
        await RollBackTo136Async(db);
        await SeedCompanyAsync(db);
        var live = await InsertUserAsync(db, "live1201", "살아있음");
        var sus = await InsertUserAsync(db, "sus1202", "사용중지", active: false);
        var resg = await InsertUserAsync(db, "resigned+z+old1203", "옛퇴사", active: false);
        var retd = await InsertUserAsync(db, "retired+w+old1204", "옛폐기", active: false, deleted: true);
        var gone = await InsertUserAsync(db, "gone1205", "옛삭제", active: false, deleted: true);
        var e = new Dictionary<string, string?>
        {
            [await InsertEmployeeAsync(db, "1201", "살아있음", live)] = "live1201",
            [await InsertEmployeeAsync(db, "1202", "사용중지", sus)] = "sus1202",
            [await InsertEmployeeAsync(db, "1203", "옛퇴사", resg)] = null,
            [await InsertEmployeeAsync(db, "1204", "옛폐기", retd)] = null,
            [await InsertEmployeeAsync(db, "1205", "옛삭제", gone)] = null,
            [await InsertEmployeeAsync(db, "1206", "미등록")] = null,
        };
        // V5-02/V5-08(§8-2) — user_id='' 행 두 줄: NULL 로 정리돼야 UNIQUE 가 실제로 걸린다('' 는 서로 중복)
        var blank1 = await InsertEmployeeAsync(db, "1207", "빈칸1", "");
        var blank2 = await InsertEmployeeAsync(db, "1208", "빈칸2", "");

        await db.ExecuteAsync(sql);
        await db.ExecuteAsync(sql);                                  // 2회 — 멱등(오류 없음 · 값 그대로)
        foreach (var (emp, want) in e)
            Assert.True(want == (await EmpLinkAsync(db, emp)).loginId, $"{emp} login_id 기대 {want ?? "NULL"}");
        Assert.Equal((null, null), await EmpLinkAsync(db, blank1));
        Assert.Equal((null, null), await EmpLinkAsync(db, blank2));
        Assert.Equal(shipShape, await LoginIdShapeAsync(db));
        Assert.Equal(shipUq, await UniqueColsAsync(db));             // 빈칸 2줄이 있어도 UNIQUE 가 걸렸다
        // UNIQUE 가 실제로 문다 — 같은 user_id 를 두 번째 사원에 넣으면 1062
        var ex = await Assert.ThrowsAsync<MySqlException>(() =>
            db.ExecuteAsync("UPDATE employees SET user_id=@U WHERE employee_id=@E", new { U = live, E = blank1 }));
        Assert.Equal(1062, ex.Number);
    }

    [Fact(DisplayName = "G-E12d 🔴 DB-137 — user_id 중복 있는 DB(P-9) → 실패 없이 끝남 · UNIQUE 건너뜀 · 칸은 생김 · 대조군(선검사 없이 UNIQUE 를 걸면 1062)")]
    public async Task E12d_Db137_Duplicate_PreCheck()
    {
        if (!Ready("G-E12d")) return;
        var sql = await File.ReadAllTextAsync(Db137Path());
        await using var db = await OpenAsync();
        await RollBackTo136Async(db);
        await SeedCompanyAsync(db);
        var shared = await InsertUserAsync(db, "dup1210", "중복");
        await InsertEmployeeAsync(db, "1210", "중복1", shared);
        await InsertEmployeeAsync(db, "1211", "중복2", shared);

        await db.ExecuteAsync(sql);                                  // 선검사가 있으면 예외 없이 끝난다
        Assert.NotNull(await LoginIdShapeAsync(db));
        Assert.Null(await UniqueColsAsync(db));                      // 건너뛰고 경고(P-9) — 코드 잠금이 1차 방어
        await db.ExecuteAsync(sql);                                  // 두 번째도 같다

        // 🔴 대조군 — 선검사를 빼면(= UNIQUE 를 바로 걸면) 같은 DB 에서 1062
        var ex = await Assert.ThrowsAsync<MySqlException>(() =>
            db.ExecuteAsync("ALTER TABLE employees ADD UNIQUE KEY uq_employees_tenant_user (tenant_id, user_id)"));
        Assert.Equal(1062, ex.Number);
    }
}
