using Dapper;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-N1 · G-N2</b> — 승인 요청을 대표에게 알릴 <b>주소를 찾을 수 있는가</b> (20260818작3).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 사장님 1.2.84 실측: <i>"승인 메시지 안 떴어."</i>
/// 승인 화면은 있었으나 <b>알림이 0곳</b>이었다. 대표가 [설정 → 등록 기기 관리] 에
/// <b>직접 들어가야만</b> 요청이 온 줄 알 수 있었다.
/// ⇒ 직원은 기다리고 대표는 모른다. <b>아무도 틀리지 않았는데 일이 안 된다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트가 무엇을 지키나 — 경계를 정확히 적는다.</b>
/// 알림이 <b>화면에 뜨는 것</b>은 SignalR 이라 xUnit 으로 못 본다(브라우저·허브 동작).
/// 여기서 지키는 것은 그 앞 단계 — <b>보낼 주소를 찾아내는가</b> 다.
/// 8/18 결함의 실체가 정확히 거기였다: 배관은 있는데 <b>주소를 묻는 코드가 없었다.</b>
/// ⚠️ <b>"알림이 뜬다"를 증명하지 않는다.</b> 그것은 Playwright 몫이다(L-5).
/// </para>
///
/// <para>
/// 🔴 <b>글자를 안 본다 — 값을 본다.</b> 격리 DB 에 출하 DDL 을 넣고 실제 서비스를 부른다.
/// </para>
///
/// <para>
/// 🔴 <b>초록불이 어디서 오는지 물었다.</b> 이 시험이 세우는 줄은 <c>users</c>·<c>employees</c>
/// 두 개뿐이고 어떤 UNIQUE 도 걸리지 않는다 — <b>막는 것도 찾는 것도 오직 내 코드</b>다.
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_notify_gate_*</c>)만 만들고 반드시 지운다.
/// ⚠️ <b>MariaDB 가 없으면 조용히 통과시킨다</b> — 그 환경에서 이 게이트는 <b>아무것도 검사하지 않는다.</b>
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class DeviceApprovalNotifyGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_notify_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private const string TenantId = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// 저장소 뿌리. ⚠️ <c>HitPan.sln</c> 은 <c>src/</c> <b>안에</b> 있으므로 그 <b>부모</b>가 뿌리다
    /// (<c>installer/</c> 는 뿌리에 있다).
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL 을 읽을 수 없다.");
    }

    /// <summary>
    /// ⚠️ 접속 정보는 <b>작1 게이트와 똑같이</b> 둔다(`DeviceAndKeyGateTests`).
    /// 🔴 <c>hitpan</c> 계정은 <b>DB 이름별로만</b> 권한이 있어 임시 DB 를 못 연다 —
    /// 임시 DB 를 만들고 지우는 시험은 <c>root</c> 로 돈다.
    /// </summary>
    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    /// <summary>🔴 신규 설치를 그대로 재현한다 — 빈 DB 에 출하 DDL 한 방(헌법 #36).</summary>
    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddlPath), $"출하 DDL 이 없다: {ddlPath}");

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0,
            $"출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

    private TenantDeviceService NewService(MySqlConnection db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeviceApproval:Enabled"] = "true"
            })
            .Build();

        return new TenantDeviceService(
            db, new NoOpAudit(), config, NullLogger<TenantDeviceService>.Instance);
    }

    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>대표계정 한 사람을 세운다 — 계정(users) 과 사원(employees) 두 줄이 필요하다.</summary>
    private static async Task InsertOwnerAsync(
        MySqlConnection db, string userId, string employeeId,
        string accountType = "tenant_admin", bool isParent = true, bool asEmployee = true)
    {
        // ⚠️ 출하 DDL 의 NOT NULL 을 전부 채운다(헌법 #13 — 쓰기 전에 스키마를 봤다).
        //   `role`·`updated_at` 은 기본값이 없어 빼면 INSERT 가 죽는다.
        await db.ExecuteAsync(
            """
            INSERT INTO users
              (user_id, tenant_id, user_name, emp_name, email, password_hash, role,
               account_type, is_parent, is_active, is_deleted, created_at, updated_at)
            VALUES
              (@Uid, @Tid, @Name, @Name, @Email, 'x', 'admin',
               @Type, @Parent, 1, 0, NOW(6), NOW(6))
            """,
            new
            {
                Uid = userId,
                Tid = TenantId,
                Name = "대표" + userId[..4],
                Email = userId[..4] + "@t.kr",
                Type = accountType,
                Parent = isParent ? 1 : 0
            });

        if (asEmployee)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO employees
                  (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
                   join_date, is_active, created_at, updated_at)
                VALUES
                  (@Eid, @Tid, @Uid, @No, @Name, 'regular',
                   NOW(6), 1, NOW(6), NOW(6))
                """,
                new
                {
                    Eid = employeeId,
                    Tid = TenantId,
                    Uid = userId,
                    No = "E" + employeeId.GetHashCode().ToString("X")[..6],
                    Name = "대표" + userId[..4]
                });
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-N1 — 알릴 주소를 찾아낸다  🔴 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-N1. 대표에게 알릴 사원 ID 를 찾아낸다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 알림 배관(<c>INotificationService</c>)은 진작 있었는데
    /// <b>부르는 곳이 0곳</b>이었다. 만들어 놓고 안 쓴 것이다.
    /// 그래서 대표는 요청이 온 줄도 몰랐다(사장님 실측).
    /// </para>
    ///
    /// <para>
    /// [반증] <c>GetAdminEmployeeIdAsync</c> 가 <c>null</c> 을 주면 FAIL —
    /// 주소를 못 찾으면 알림은 <b>영원히 안 간다.</b>
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-N1 🔴 승인 요청을 알릴 대표 사원 ID 를 찾아낸다")]
    public async Task GN1_대표_사원ID를_찾는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        const string ownerUser = "aaaaaaaa-1111-1111-1111-111111111111";
        const string ownerEmp = "emp-aaaa-1111";
        await InsertOwnerAsync(db, ownerUser, ownerEmp);

        var svc = NewService(db);
        var found = await svc.GetAdminEmployeeIdAsync(TenantId);

        Assert.True(
            found == ownerEmp,
            $"🔴 대표에게 알릴 주소를 못 찾았다(돌아온 값: '{found ?? "없음"}'). " +
            "주소가 없으면 알림은 영영 안 간다 — 대표는 요청이 온 줄도 모르고, " +
            "직원은 승인만 기다린다. 아무도 틀리지 않았는데 일이 안 되는 상태다(사장님 1.2.84 실측).");
    }

    /// <summary>
    /// <b>G-N1-b. 대표가 여럿이면 부모계정을 고른다.</b>
    ///
    /// <para>
    /// [왜 필요한가] 대표는 여럿일 수 있다. 아무나 고르면 <b>알림이 엉뚱한 사람에게 간다</b> —
    /// 그 사람은 승인 권한이 있어도 <b>그 기기를 모른다.</b> 원본은 부모계정이다(헌법 #38).
    /// </para>
    ///
    /// <para>[반증] <c>ORDER BY u.is_parent DESC</c> 를 빼면 먼저 만들어진 자식이 뽑혀 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-N1-b 대표가 여럿이면 부모계정을 고른다")]
    public async Task GN1b_부모계정을_고른다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 🔴 자식을 **먼저** 넣는다 — 순서로 뽑으면 이쪽이 걸리게 해서 정렬을 진짜로 시험한다.
        const string childUser = "bbbbbbbb-2222-2222-2222-222222222222";
        const string childEmp = "emp-bbbb-2222";
        await InsertOwnerAsync(db, childUser, childEmp, isParent: false);

        await Task.Delay(15);   // created_at 을 확실히 가른다

        const string parentUser = "cccccccc-3333-3333-3333-333333333333";
        const string parentEmp = "emp-cccc-3333";
        await InsertOwnerAsync(db, parentUser, parentEmp, isParent: true);

        var svc = NewService(db);
        var found = await svc.GetAdminEmployeeIdAsync(TenantId);

        Assert.True(
            found == parentEmp,
            $"🔴 대표가 둘인데 부모가 아닌 쪽('{found}')이 뽑혔다. " +
            "알림이 엉뚱한 사람에게 간다 — 그 사람은 승인 권한이 있어도 그 기기를 모른다. " +
            "ERP 계정 원본은 부모계정이다(헌법 #38).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-N2 — 못 찾아도 죽지 않는다  🔴 짝
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-N2. 대표가 사원으로 등록돼 있지 않으면 조용히 <c>null</c> 이다 — 터지지 않는다.</b>
    ///
    /// <para>
    /// [왜 이 짝이 필요한가] G-N1 만 있으면 <b>예외를 던져서 통과시킬</b> 수 있다.
    /// 그러면 알림을 못 보낼 때 <b>로그인 자체가 죽는다</b> —
    /// 🔴 <b>부수 기능이 본 기능을 죽이는 것</b>이 히트판이 여러 번 겪은 사고다.
    /// </para>
    ///
    /// <para>[반증] 예외를 밖으로 던지게 하면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-N2 🔴 알릴 주소가 없어도 터지지 않는다 (로그인을 막지 않는다)")]
    public async Task GN2_주소가_없어도_안터진다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 대표는 있으나 **사원으로 등록되지 않았다** — 실제로 흔한 상태다.
        const string ownerUser = "dddddddd-4444-4444-4444-444444444444";
        await InsertOwnerAsync(db, ownerUser, "emp-없음", asEmployee: false);

        var svc = NewService(db);

        var ex = await Record.ExceptionAsync(() => svc.GetAdminEmployeeIdAsync(TenantId));

        Assert.True(
            ex is null,
            $"🔴 알릴 주소가 없다고 예외가 터졌다({ex?.GetType().Name}). " +
            "이 값은 로그인 도중에 읽는다 — 여기서 터지면 **대표가 사원으로 안 잡힌 회사는 아무도 로그인을 못 한다.** " +
            "부수 기능(알림)이 본 기능(로그인)을 죽이면 안 된다.");

        var found = await svc.GetAdminEmployeeIdAsync(TenantId);
        Assert.True(found is null, $"사원이 없는데 '{found}' 를 주소라고 내놓았다.");
    }

    /// <summary>
    /// <b>G-N2-b. 다른 회사의 대표를 알림 주소로 내주지 않는다</b> (헌법 #2 테넌트 격리).
    ///
    /// <para>[반증] <c>WHERE u.tenant_id</c> 를 빼면 남의 회사 대표가 뽑혀 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-N2-b 다른 회사 대표를 알림 주소로 내주지 않는다")]
    public async Task GN2b_남의회사_대표는_안된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 남의 회사 대표만 존재한다 — 우리 회사에는 대표가 없다.
        const string otherTenant = "99999999-9999-9999-9999-999999999999";
        await db.ExecuteAsync(
            """
            INSERT INTO users
              (user_id, tenant_id, user_name, emp_name, email, password_hash, role,
               account_type, is_parent, is_active, is_deleted, created_at, updated_at)
            VALUES
              ('eeeeeeee-5555-5555-5555-555555555555', @Other, '남의회사대표', '남의회사대표',
               'other@t.kr', 'x', 'admin', 'tenant_admin', 1, 1, 0, NOW(6), NOW(6))
            """,
            new { Other = otherTenant });

        await db.ExecuteAsync(
            """
            INSERT INTO employees
              (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
               join_date, is_active, created_at, updated_at)
            VALUES
              ('emp-남의회사', @Other, 'eeeeeeee-5555-5555-5555-555555555555',
               'E99999', '남의회사대표', 'regular', NOW(6), 1, NOW(6), NOW(6))
            """,
            new { Other = otherTenant });

        var svc = NewService(db);
        var found = await svc.GetAdminEmployeeIdAsync(TenantId);

        Assert.True(
            found is null,
            $"🔴 남의 회사 대표('{found}')가 알림 주소로 나왔다 — 우리 회사 기기 요청이 " +
            "다른 회사 대표에게 간다. 테넌트 격리가 깨졌다(헌법 #2).");
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
        catch (MySqlException)
        {
            // 시험용 임시 DB 정리 실패는 시험 결과를 바꾸지 않는다.
        }
    }
}
