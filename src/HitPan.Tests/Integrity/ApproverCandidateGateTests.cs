using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G1-2 · G1-5</b> — 결재선에 넣을 수 있는 사람만 뽑는가 (작20260822작1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나.</b> 결재선에 <c>APPROVAL</c> 권한 없는 사람이 들어가면 그 사람은
/// 결재함에 <c>[RequirePermission("APPROVAL","view")]</c> 에서 막혀 <b>진입 자체를 못 한다.</b>
/// ⇒ 그 문서가 영영 안 간다. 아무도 틀리지 않았는데 일이 선다.
/// </para>
///
/// <para>
/// 🔴 <b>사장님 결재(2026-08-23): "부모계정, 그리고 권한자만."</b>
/// PM 은 <i>"권한자만"</i> 이라 권고했고 <b>그것이 틀렸다.</b>
/// <c>PermissionService.HasPermissionAsync</c> 는 부모계정을 <c>user_permissions</c>
/// 조회 <b>전에</b> 통과시킨다(락아웃 방지) — 대표는 그 표에 줄이 없을 수 있다.
/// 권한자만 뽑으면 <b>대표가 목록에서 사라지는데</b>, 최종 결재 단계는 대표여야 한다.
/// ⇒ 화면이 스스로와 충돌한다. 이 시험이 그 자리를 지킨다.
/// </para>
///
/// <para>
/// 🔴 <b>글자를 안 본다 — 값을 본다.</b> 격리 DB 에 출하 DDL 을 넣고 실제 서비스를 부른다.
/// 반환값이 <b>누구를 담고 누구를 안 담는지</b> 로 판정한다.
/// </para>
///
/// <para>
/// 🔴 <b>초록불이 어디서 오는가.</b> 이 시험이 세우는 줄에는 어떤 UNIQUE·FK 도 걸리지 않는다 —
/// 거르는 것은 <b>오직 내 SQL</b> 이다. 조건을 지우면 그 사람이 즉시 목록에 나타난다.
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_g1_gate_*</c>)만 만들고 반드시 지운다.
/// ⚠️ <b>MariaDB 가 없으면 조용히 통과시킨다</b> — 그 환경에서 이 게이트는 <b>아무것도 검사하지 않는다.</b>
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class ApproverCandidateGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_g1_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private const string TenantId = "22222222-2222-2222-2222-222222222222";

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

    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>사람 하나를 세운다 — 계정(users)과 사원(employees) 두 줄이 필요하다.</summary>
    private static async Task InsertPersonAsync(
        MySqlConnection db, string userId, string employeeId, string name,
        bool isParent, bool userActive = true, bool empActive = true, bool resigned = false)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO users
              (user_id, tenant_id, user_name, emp_name, email, password_hash, role,
               account_type, is_parent, is_active, is_deleted, created_at, updated_at)
            VALUES
              (@Uid, @Tid, @Name, @Name, @Email, 'x', 'admin',
               @Type, @Parent, @UActive, 0, NOW(6), NOW(6))
            """,
            new
            {
                Uid = userId,
                Tid = TenantId,
                Name = name,
                Email = userId[..4] + "@t.kr",
                Type = isParent ? "tenant_admin" : "tenant_user",
                Parent = isParent ? 1 : 0,
                UActive = userActive ? 1 : 0
            });

        await db.ExecuteAsync(
            """
            INSERT INTO employees
              (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
               join_date, is_active, is_resigned, created_at, updated_at)
            VALUES
              (@Eid, @Tid, @Uid, @No, @Name, 'regular',
               NOW(6), @EActive, @Resigned, NOW(6), NOW(6))
            """,
            new
            {
                Eid = employeeId,
                Tid = TenantId,
                Uid = userId,
                No = employeeId[..6],
                Name = name,
                EActive = empActive ? 1 : 0,
                Resigned = resigned ? 1 : 0
            });
    }

    /// <summary>APPROVAL 권한을 준다 — user_permissions 에 줄을 하나 세운다.</summary>
    private static async Task GrantApprovalAsync(MySqlConnection db, string userId)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO user_permissions
              (perm_id, tenant_id, user_id, menu_code,
               can_view, can_create, can_update, can_delete, can_export,
               created_at, updated_at)
            VALUES
              (@Pid, @Tid, @Uid, 'APPROVAL', 1, 0, 0, 0, 0, NOW(6), NOW(6))
            """,
            new { Pid = Guid.NewGuid().ToString(), Tid = TenantId, Uid = userId });
    }

    /// <summary>
    /// 🔴 <b>G1-2</b> — 권한 없는 사람은 결재자 후보에 <b>안 나온다.</b>
    /// <b>G1-5</b> — 대표는 <c>user_permissions</c> 에 줄이 없어도 <b>나온다.</b>
    /// </summary>
    /// <remarks>
    /// [반증] SQL 의 <c>AND (u.is_parent = 1 OR p.user_id IS NOT NULL)</c> 를 지우면
    ///        무권한자 "박무권" 이 목록에 나타나 FAIL 한다.
    /// [반증] 같은 줄에서 <c>u.is_parent = 1 OR</c> 만 지우면 대표가 사라져 FAIL 한다 —
    ///        <b>이것이 PM 권고("권한자만")가 틀렸던 자리다.</b>
    /// </remarks>
    [Fact]
    public async Task 결재자후보는_부모계정과_권한자만_나온다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 대표 — 권한 줄을 **일부러 안 준다.** 권한검사가 부모계정을 먼저 통과시키기 때문이다.
        await InsertPersonAsync(db, "u-owner-0001", "e-owner-0001", "김대표", isParent: true);

        // 권한자 — APPROVAL 을 받은 일반 직원
        await InsertPersonAsync(db, "u-appr--0002", "e-appr--0002", "이결재", isParent: false);
        await GrantApprovalAsync(db, "u-appr--0002");

        // 무권한자 — 권한 줄이 없다. 결재함에 못 들어간다.
        await InsertPersonAsync(db, "u-none--0003", "e-none--0003", "박무권", isParent: false);

        var svc = new EmployeeService(db, new NoOpAudit());
        var list = await svc.GetApproverCandidatesAsync(TenantId);
        var names = list.Select(x => x.EmpName).ToList();

        Assert.Contains("김대표", names);   // 🔴 권한 줄이 없어도 나와야 한다
        Assert.Contains("이결재", names);
        Assert.DoesNotContain("박무권", names);  // 🔴 나오면 그 문서가 영영 안 간다

        // 대표는 is_parent 로 잡힌다 — position 문자열이 아니다(G1-5).
        var owner = list.Single(x => x.EmpName == "김대표");
        Assert.True(owner.IsParentAccount, "대표를 is_parent 로 판정해야 한다");
        Assert.False(owner.HasApprovalPermission,
            "대표는 user_permissions 에 줄이 없다 — 그래도 후보다. 이 값 하나로 판정하면 안 된다.");

        var appr = list.Single(x => x.EmpName == "이결재");
        Assert.False(appr.IsParentAccount);
        Assert.True(appr.HasApprovalPermission);
    }

    /// <summary>
    /// 🔴 <b>퇴사자·꺼진 계정은 권한이 남아 있어도 후보가 아니다.</b>
    /// </summary>
    /// <remarks>
    /// 2026-08-14 실측 사고와 같은 자리다 — 퇴사자는 계정이 <c>is_active=0</c> 으로만 꺼지고
    /// <c>is_deleted</c> 는 0 이다. <b>권한 줄은 그대로 남는다.</b>
    /// 계정 상태를 안 보면 <b>퇴사한 사람이 결재자 후보로 올라온다.</b>
    /// <para>[반증] <c>u.is_active = 1</c> 을 지우면 "최퇴사" 가 나타나 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task 퇴사자와_꺼진계정은_권한이_남아도_후보가_아니다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await InsertPersonAsync(db, "u-live--0001", "e-live--0001", "정재직", isParent: false);
        await GrantApprovalAsync(db, "u-live--0001");

        // 퇴사자 — 계정이 꺼졌고 사원도 퇴사 처리됐다. 권한 줄은 **그대로 남아 있다.**
        await InsertPersonAsync(db, "u-quit--0002", "e-quit--0002", "최퇴사", isParent: false,
            userActive: false, empActive: false, resigned: true);
        await GrantApprovalAsync(db, "u-quit--0002");

        var svc = new EmployeeService(db, new NoOpAudit());
        var names = (await svc.GetApproverCandidatesAsync(TenantId))
            .Select(x => x.EmpName).ToList();

        Assert.Contains("정재직", names);
        Assert.DoesNotContain("최퇴사", names);
    }

    /// <summary>
    /// 🔴 <b>계정이 아예 없는 사원은 후보가 아니다.</b> 로그인을 못 하니 결재도 못 한다.
    /// </summary>
    /// <remarks>[반증] INNER JOIN 을 LEFT JOIN 으로 바꾸면 "무계정" 이 나타나 FAIL 한다.</remarks>
    [Fact]
    public async Task 계정없는_사원은_후보가_아니다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await InsertPersonAsync(db, "u-has---0001", "e-has---0001", "한계정", isParent: true);

        // 계정 없는 사원 — employees 만 있고 users 가 없다(user_id NULL).
        await db.ExecuteAsync(
            """
            INSERT INTO employees
              (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
               join_date, is_active, is_resigned, created_at, updated_at)
            VALUES
              (@Eid, @Tid, NULL, 'E90001', '무계정', 'regular',
               NOW(6), 1, 0, NOW(6), NOW(6))
            """,
            new { Eid = "e-noacc-0009", Tid = TenantId });

        var svc = new EmployeeService(db, new NoOpAudit());
        var names = (await svc.GetApproverCandidatesAsync(TenantId))
            .Select(x => x.EmpName).ToList();

        Assert.Contains("한계정", names);
        Assert.DoesNotContain("무계정", names);
    }

    /// <summary>
    /// 🔴 <b>다른 회사 사람은 안 나온다</b>(헌법 #2 테넌트 격리).
    /// </summary>
    /// <remarks>[반증] <c>WHERE e.tenant_id = @TenantId</c> 를 지우면 남의 회사 대표가 나타나 FAIL.</remarks>
    [Fact]
    public async Task 다른_회사_사람은_후보에_안_나온다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await InsertPersonAsync(db, "u-mine--0001", "e-mine--0001", "우리대표", isParent: true);

        const string otherTenant = "33333333-3333-3333-3333-333333333333";
        await db.ExecuteAsync(
            """
            INSERT INTO users
              (user_id, tenant_id, user_name, emp_name, email, password_hash, role,
               account_type, is_parent, is_active, is_deleted, created_at, updated_at)
            VALUES
              ('u-other-0002', @Tid, '남의대표', '남의대표', 'other@t.kr', 'x', 'admin',
               'tenant_admin', 1, 1, 0, NOW(6), NOW(6))
            """,
            new { Tid = otherTenant });
        await db.ExecuteAsync(
            """
            INSERT INTO employees
              (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
               join_date, is_active, is_resigned, created_at, updated_at)
            VALUES
              ('e-other-0002', @Tid, 'u-other-0002', 'E80001', '남의대표', 'regular',
               NOW(6), 1, 0, NOW(6), NOW(6))
            """,
            new { Tid = otherTenant });

        var svc = new EmployeeService(db, new NoOpAudit());
        var names = (await svc.GetApproverCandidatesAsync(TenantId))
            .Select(x => x.EmpName).ToList();

        Assert.Contains("우리대표", names);
        Assert.DoesNotContain("남의대표", names);
    }

    /// <summary>
    /// 🔴 <b>서버가 막는가</b> — 화면만 거르면 그것은 규칙이 아니라 권유다.
    /// ([3-V] 동시검증 적발 — 저장 경로가 권한을 안 봤다)
    /// </summary>
    /// <remarks>
    /// 화면은 후보를 걸러 보여주지만, 화면을 거치지 않는 저장이면 권한 없는 사람이
    /// 그대로 결재선에 앉는다. 그러면 그 사람은 결재함에 못 들어가
    /// (<c>RequirePermission("APPROVAL","view")</c>) <b>그 문서가 영영 안 간다.</b>
    /// <para>
    /// ⚠️ 이 자리는 <c>tenant_admin</c>(대표)만 부를 수 있다 — 권한 상승 구멍이 아니라
    /// <b>대표가 자기 발등을 찍는 자리</b>다.
    /// </para>
    /// <para>[반증] <c>SaveLinesAsync</c> 의 권한 검사 블록을 지우면 저장이 통과해 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task 권한없는_사람을_결재선에_저장하면_막힌다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await InsertPersonAsync(db, "u-sown--0001", "e-sown--0001", "김대표", isParent: true);
        await InsertPersonAsync(db, "u-snon--0002", "e-snon--0002", "박무권", isParent: false);

        var svc = new ApprovalService(db, new NoOpAudit());

        var req = new HitPan.Application.DTOs.Approval.SaveApprovalLinesRequest
        {
            DocType = "expense",
            Lines = new List<HitPan.Application.DTOs.Approval.ApprovalLineItem>
            {
                new() { SeqNo = 1, ApproverId = "e-snon--0002", ApproverName = "박무권" },
                new() { SeqNo = 2, ApproverId = "e-sown--0001", ApproverName = "김대표" }
            }
        };

        // 🔴 권한 없는 박무권이 끼어 있으므로 저장이 막혀야 한다.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveLinesAsync(req, TenantId));
        Assert.Contains("박무권", ex.Message);

        // 🔴 대표만 남기면 통과해야 한다 — 대표는 user_permissions 에 줄이 없어도 결재한다.
        req.Lines = new List<HitPan.Application.DTOs.Approval.ApprovalLineItem>
        {
            new() { SeqNo = 1, ApproverId = "e-sown--0001", ApproverName = "김대표" }
        };
        await svc.SaveLinesAsync(req, TenantId);   // 예외가 나면 FAIL
    }

    /// <summary>
    /// 🔴🔴 <b>계정 없는 사원을 결재선에 저장하면 막히는가</b> — 터널 실측이 잡은 버그.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>2026-08-23 실측 적발</b> — 터널 test1234 에서 계정 없는 사원을 결재선에
    /// 저장했더니 <b>HTTP 200 "저장되었습니다"</b> 가 나왔다. 서버가 안 막았다.
    /// <para>
    /// <b>원인 — SQL 3값 논리.</b> 계정이 없으면 <c>LEFT JOIN users</c> 가 실패해
    /// <c>u.is_parent</c> 가 NULL 이 된다. 그러면
    /// <code>NOT (NULL = 1 OR NULL IS NOT NULL) → NOT (NULL) → NULL</code>
    /// 이고 <c>WHERE</c> 는 NULL 을 TRUE 로 안 보므로 <b>그 사람이 결과에 안 담긴다</b> —
    /// "못 막는 사람" 목록에조차 안 잡혀 저장이 통과했다.
    /// ⇒ <b>제일 확실히 막아야 할 사람(로그인 자체가 안 되는 사람)이 제일 잘 빠져나갔다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>왜 종전 시험이 못 잡았나</b> — <c>계정없는_사원은_후보가_아니다</c> 는
    /// <b>조회</b>만 봤다. <b>저장</b>을 안 봤다. 같은 사람인데 경로가 둘이었다.
    /// </para>
    /// <para>[반증] <c>COALESCE(u.is_parent, 0)</c> 를 <c>u.is_parent</c> 로 되돌리면 저장이 통과해 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task 계정없는_사원을_결재선에_저장하면_막힌다()
    {
        if (!ServerAvailable()) return;

        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await InsertPersonAsync(db, "u-own2--0001", "e-own2--0001", "김대표", isParent: true);

        // 계정 없는 사원 — employees 만 있고 users 가 없다(user_id NULL).
        await db.ExecuteAsync(
            """
            INSERT INTO employees
              (employee_id, tenant_id, user_id, emp_no, emp_name, emp_type,
               join_date, is_active, is_resigned, created_at, updated_at)
            VALUES
              ('e-noacc-0077', @Tid, NULL, 'E77001', '무계정', 'regular',
               NOW(6), 1, 0, NOW(6), NOW(6))
            """,
            new { Tid = TenantId });

        var svc = new ApprovalService(db, new NoOpAudit());

        var req = new HitPan.Application.DTOs.Approval.SaveApprovalLinesRequest
        {
            DocType = "leave",
            Lines = new List<HitPan.Application.DTOs.Approval.ApprovalLineItem>
            {
                new() { SeqNo = 1, ApproverId = "e-noacc-0077", ApproverName = "무계정" }
            }
        };

        // 🔴 로그인을 못 하는 사람은 결재도 못 한다 — 저장이 막혀야 한다.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => svc.SaveLinesAsync(req, TenantId));
        Assert.True(ex is InvalidOperationException,
            $"막히긴 했는데 다른 이유다: {ex.GetType().Name} — {ex.Message}");
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
            // 지우기 실패는 시험 결과가 아니다 — 임시 DB 라 다음 실행에 지장 없다.
        }
    }
}