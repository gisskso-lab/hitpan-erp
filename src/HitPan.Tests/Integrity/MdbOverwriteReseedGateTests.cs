using System.Data;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Services;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

// MigrationController 는 [SupportedOSPlatform("windows")] 다(ACE OLEDB 의존). 이 게이트가 실제로 부르는 자리는
// 모드·확인문구 검사뿐이라 OS 와 무관하고, DB 게이트 잡은 ubuntu 에서 돈다.
// 컨트롤러 파일 자신이 쓰는 방식(MigrationController.cs:8)과 같게 여기서도 해제한다 — 헌법 #19 경고 0.
#pragma warning disable CA1416

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbOverwriteReseedGate</b> — 20260910작1 A1: 「모두 지우고 새로 가져오기」 + 회사 뼈대 재시드.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 재나</b> — 초기화는 보존 목록 밖을 전부 비우므로 신규 설치 때 깔리던
/// <b>표준 계정과목 27 · 대표 사원 1 · 직급 6 · 근로 기준값 16 · 기본창고 1</b> 이 사라진다.
/// 되살리는 코드가 <b>0건</b>이었고(선행검증 20260909검1 §2-1), 사장님이 2026-09-10 재시드를 허락하셨다.
/// 이 게이트는 <b>「불렀나」가 아니라 「깔렸나」</b> 를 센다 — 반환값이 아니라 표의 행수를 읽는다.
/// </para>
/// <para>
/// 🔴 <b>대조군이 있다</b>(G-OW4) — 재시드를 <b>안 한</b> 회사는 같은 자리에서 0 이 나온다.
/// 이게 없으면 27·6·16 이 어디서 왔는지 증명되지 않는다(누적 24번 반복된 가짜 게이트 사고).
/// </para>
/// <para>
/// ⚠️ 실측 대상은 <c>hitpan_e2e</c> 안의 <b>이 게이트 전용 회사</b>다(헌법 #39 — 운영 무접촉).
/// 다른 회사의 자료는 한 줄도 읽지도 쓰지도 않는다. 끝나면 자기가 만든 회사만 지운다.
/// </para>
/// </remarks>
public sealed class MdbOverwriteReseedGateTests
{
    private const string TestDb = "hitpan_e2e";

    // 신규 설치 기준 수 — CompanyBootstrapProvisioner 의 시드 목록과 같아야 한다.
    private const int ExpectedAccounts = 27;
    private const int ExpectedPositions = 6;
    private const int ExpectedLaborPolicies = 16;

    // ────────────────────────────────────────────────────────────────────────
    //  G-OW1 — 재시드하면 뼈대가 **다시 선다**
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-OW1 — 초기화 직후와 같은 빈 회사에 재시드하면 계정과목 27 · 직급 6 · 근로기준 16 · 창고 1 · 대표 사원 1 이 선다.
    /// <para>무력화: <c>ReseedCompanySkeletonAsync</c> 안의 시드 호출을 하나라도 빼면 그 숫자가 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_OW1_재시드하면_회사뼈대가_다시_선다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_OW1_재시드하면_회사뼈대가_다시_선다)); return; }

        var tenantId = NewTenantId();
        using var db = Open();
        try
        {
            SeedParentUser(db, tenantId);

            var result = await NewProvisioner().ReseedCompanySkeletonAsync(tenantId, default);

            // 반환값이 아니라 **표를 다시 세어** 판정한다.
            Assert.Equal(ExpectedAccounts, CountOf(db, "accounts", tenantId));
            Assert.Equal(ExpectedPositions, CountOf(db, "positions", tenantId));
            Assert.Equal(ExpectedLaborPolicies, CountOf(db, "labor_policy_settings", tenantId));
            Assert.Equal(1, CountOf(db, "warehouses", tenantId));
            Assert.Equal(1, CountOf(db, "employees", tenantId));

            // 대표 사원이 **결재선에서 고를 수 있는 모습**으로 서 있나 (직급이 마스터에 있는 이름이어야 한다).
            var owner = db.QueryFirst<(string EmpNo, string Position)>(
                @"SELECT emp_no, position FROM employees WHERE tenant_id = @T",
                new { T = tenantId });
            Assert.Equal("0001", owner.EmpNo);
            Assert.Equal("대표이사", owner.Position);
            Assert.Equal(1, db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM positions WHERE tenant_id = @T AND name = @N",
                new { T = tenantId, N = owner.Position }));

            // 반환값도 표와 같은 말을 해야 한다(둘이 갈리면 화면이 거짓말을 한다).
            Assert.Equal(ExpectedAccounts, result.Accounts);
            Assert.Equal(1, result.Employees);
        }
        finally { CleanupTenant(db, tenantId); }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-OW2 — 되살리는 것이지 덮어쓰는 게 아니다 (멱등 + 사람이 고친 값 보존)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-OW2 — 두 번 불러도 행수가 안 늘고, 대표가 고친 계정과목 이름·지운 직급을 <b>되돌리지 않는다</b>.
    /// <para>무력화: 시드 SQL 의 <c>NOT EXISTS</c> 를 <c>REPLACE</c>·<c>ON DUPLICATE KEY UPDATE</c> 로 바꾸면 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_OW2_재시드는_멱등이고_사람이_고친_값을_되돌리지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_OW2_재시드는_멱등이고_사람이_고친_값을_되돌리지_않는다)); return; }

        var tenantId = NewTenantId();
        using var db = Open();
        try
        {
            SeedParentUser(db, tenantId);
            var provisioner = NewProvisioner();

            await provisioner.ReseedCompanySkeletonAsync(tenantId, default);

            // 대표가 화면에서 이름을 바꿨다고 하자.
            db.Execute("UPDATE accounts SET account_name = @N WHERE tenant_id = @T AND account_code = '10800'",
                new { T = tenantId, N = "받을돈(대표가고침)" });

            var before = Snapshot(db, tenantId);
            await provisioner.ReseedCompanySkeletonAsync(tenantId, default);
            var after = Snapshot(db, tenantId);

            Assert.Equal(before, after);   // 행수가 하나도 안 늘었다
            Assert.Equal("받을돈(대표가고침)", db.ExecuteScalar<string>(
                "SELECT account_name FROM accounts WHERE tenant_id = @T AND account_code = '10800'",
                new { T = tenantId }));
        }
        finally { CleanupTenant(db, tenantId); }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-OW3 — 재시드의 존재 이유: 기표가 착지할 계정이 있다
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-OW3 — 이관 뒤 첫 판매확정·수금·매입이 기표될 <b>상대계정이 실제로 있다</b>.
    /// <para>
    /// 뼈대를 안 깔면 <c>journal_lines → accounts</c> FK(<c>fk_jl_account</c>) 로 죽는다 —
    /// 그게 사장님께 "회계에 안 남는다" 로 보이는 그 증상이다. 여기서는 그 FK 가 가리킬 계정의 존재를 센다.
    /// </para>
    /// <para>무력화: 시드 목록에서 외상매출금(10800)·외상매입금(23200) 등을 빼면 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_OW3_기표가_착지할_상대계정이_있다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_OW3_기표가_착지할_상대계정이_있다)); return; }

        // 🔴 목록을 손으로 적지 않는다 — 자동 기표가 실제로 쓰는 상수(AutoJournalHelper)를 읽어 온다.
        //   나중에 누가 계정 상수를 하나 더 만들고 시드에 안 넣으면 그 순간 이 게이트가 빨간불이 된다.
        var required = AutoJournalAccountCodes();
        Assert.True(required.Count >= 13, $"자동 기표 계정 상수를 못 읽었다({required.Count}개) — 게이트가 헛돌고 있다.");

        var tenantId = NewTenantId();
        using var db = Open();
        try
        {
            SeedParentUser(db, tenantId);
            await NewProvisioner().ReseedCompanySkeletonAsync(tenantId, default);

            var have = db.Query<string>(
                "SELECT account_code FROM accounts WHERE tenant_id = @T", new { T = tenantId }).ToHashSet();

            var missing = required.Where(c => !have.Contains(c)).ToList();
            Assert.True(missing.Count == 0, $"기표가 착지할 계정이 없다: {string.Join(", ", missing)}");
        }
        finally { CleanupTenant(db, tenantId); }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-OW4 — 🔴 대조군: 재시드를 **안 하면** 같은 자리가 0 이다
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-OW4 — <b>대조군</b>. 재시드를 부르지 않은 회사는 계정과목·직급·근로기준·창고·사원이 전부 <b>0</b> 이다.
    /// <para>
    /// 이 시험이 <b>이 파일의 초록불이 어디서 오는지</b>를 증명한다. 만약 재시드 없이도 27 이 나온다면
    /// 위 게이트들은 다른 무언가(출하 DDL 시드 등)를 재고 있는 것이지 재시드를 재는 게 아니다.
    /// </para>
    /// </summary>
    [Fact]
    public void G_OW4_대조군_재시드를_안_하면_뼈대가_없다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G_OW4_대조군_재시드를_안_하면_뼈대가_없다)); return; }

        var tenantId = NewTenantId();
        using var db = Open();
        try
        {
            SeedParentUser(db, tenantId);   // 부모계정만 있고 재시드는 **부르지 않는다**

            Assert.Equal(0, CountOf(db, "accounts", tenantId));
            Assert.Equal(0, CountOf(db, "positions", tenantId));
            Assert.Equal(0, CountOf(db, "labor_policy_settings", tenantId));
            Assert.Equal(0, CountOf(db, "warehouses", tenantId));
            Assert.Equal(0, CountOf(db, "employees", tenantId));
        }
        finally { CleanupTenant(db, tenantId); }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-OW5 — 확인이 안 끝나면 **아무것도 지우지 않는다**
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-OW5 — 확인 문구가 다르거나 비번이 비면 <b>지우기 서비스를 부르지도 않고</b> 400 이다.
    /// <para>
    /// "막았다" 를 문구로 재지 않는다 — 가짜 지우기 서비스를 넣고 <b>불렸는지</b>를 증언하게 한다.
    /// 한 번이라도 불렸다면 그 순간 사장님 자료가 지워진 것이다.
    /// </para>
    /// <para>무력화: 확인 문구 검사를 지우면 <c>Called</c> 가 참이 되어 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("", "비밀번호있음")]          // 확인 문구 없음
    [InlineData("덮어쓰기아님", "비밀번호있음")] // 확인 문구 틀림
    [InlineData("덮어쓰기", "")]              // 비번 없음
    public async Task G_OW5_확인이_안_끝나면_지우기를_부르지도_않는다(string confirmText, string password)
    {
        var spy = new SpyDataResetService();
        var controller = NewController(spy);

        var result = await controller.StartMigrationJob(new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "overwrite",
            ConfirmText = confirmText,
            Password = password,
        }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(spy.Called, "확인이 끝나지 않았는데 지우기가 불렸다 — 이 경로로 사장님 자료가 지워진다.");
    }

    /// <summary>
    /// 🔴 G-OW6 — <b>이어서 가져오기</b>와 옛 동기 경로는 덮어쓰기를 받지 않는다.
    /// <para>
    /// <c>continue</c> 가 받으면 1단계에 이미 들어온 자료를 지우고 2단계를 얹는다.
    /// 동기 경로는 화면의 2단 확인을 거치지 않는다.
    /// </para>
    /// <para>무력화: <c>RejectOverwriteHere</c> 호출을 빼면 지우기가 불려 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_OW6_이어서가져오기와_옛경로는_덮어쓰기를_안_받는다()
    {
        var spy = new SpyDataResetService();
        var req = new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "overwrite",
            ConfirmText = "덮어쓰기",
            Password = "비밀번호있음",
        };

        var c1 = NewController(spy);
        Assert.IsType<BadRequestObjectResult>(await c1.ContinueMigrationJob("job-1", req));

        var c2 = NewController(spy);
        Assert.IsType<BadRequestObjectResult>(await c2.MigrateLegacyMdb(req, default));

        Assert.False(spy.Called, "시작 지점이 아닌 곳에서 지우기가 불렸다.");
    }

    /// <summary>
    /// 🔴 G-OW8 — 지우기는 <b>자료가 들어 있는 컴퓨터에서만</b> 된다(2026-08-11 사장님 지시 · 초기화 화면과 같은 규칙).
    /// <para>
    /// 초기화 화면은 컨트롤러 전체가 <c>MainPcOnly</c> 인데, 덮어쓰기는 <c>/start</c> 안의 분기라
    /// 같은 가드를 <b>따로 물어야</b> 한다. 안 물면 밖에서 주소만 알면 회사 장부를 통째로 지울 수 있다.
    /// </para>
    /// <para>무력화: <c>PrepareOverwriteAsync</c> 의 메인PC 검사를 빼면 지우기가 불려 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G_OW8_지우기는_자료가_있는_컴퓨터에서만_된다()
    {
        var spy = new SpyDataResetService();
        var controller = NewController(spy, fromOtherPc: true);

        var result = await controller.StartMigrationJob(new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "overwrite",
            ConfirmText = "덮어쓰기",
            Password = "비밀번호있음",
        }, default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        Assert.False(spy.Called, "다른 컴퓨터에서 온 요청인데 지우기가 불렸다 — 주소만 알면 장부가 사라진다.");
    }

    /// <summary>
    /// 🔴 G-OW9 — 「없는 것만 보태기」는 <b>막지 않는다</b>. 지우는 쪽만 컴퓨터를 가린다.
    /// <para>이 대조가 없으면 위 가드가 정상 업무까지 막아 놓고 초록불일 수 있다(헌법 #20 — 흐름을 끊지 않는다).</para>
    /// </summary>
    [Fact]
    public async Task G_OW9_보태기는_다른_컴퓨터에서도_막히지_않는다()
    {
        var spy = new SpyDataResetService();
        var controller = NewController(spy, fromOtherPc: true);

        // 보태기는 지우기 분기를 타지 않으므로 메인PC 검사에 걸리지 않는다.
        // (그 뒤 잡 생성에서 의존이 없어 터지는데, 그것이 곧 "403 으로 끊기지 않았다" 는 증거다.)
        var ex = await Record.ExceptionAsync(() => controller.StartMigrationJob(new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "merge",
        }, default));

        Assert.NotNull(ex);                 // 잡 저장소가 없어서 터진다 = 403 으로 걸러지지 않았다
        Assert.IsNotType<ObjectResult>(ex); // 403 을 돌려준 게 아니다
        Assert.False(spy.Called, "보태기인데 지우기가 불렸다.");
    }

    /// <summary>
    /// 🔴 G-OW7 — 초기화 화면과 덮어쓰기가 <b>같은 재시드</b>를 탄다.
    /// <para>
    /// 두 컨트롤러가 모두 <see cref="CompanyBootstrapProvisioner"/> 를 받아야 하고,
    /// 재시드 진입점은 <b>하나뿐</b>이어야 한다. 한쪽만 고치는 것이 이 프로젝트의 반복 사고다.
    /// </para>
    /// </summary>
    [Fact]
    public void G_OW7_초기화화면과_덮어쓰기가_같은_재시드를_탄다()
    {
        foreach (var t in new[] { typeof(DataResetController), typeof(MigrationController) })
        {
            var ctor = t.GetConstructors().Single();
            Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(CompanyBootstrapProvisioner));
        }

        var entries = typeof(CompanyBootstrapProvisioner)
            .GetMethods()
            .Where(m => m.Name.Contains("Reseed", StringComparison.Ordinal))
            .ToList();
        Assert.Single(entries);
        Assert.Equal(nameof(CompanyBootstrapProvisioner.ReseedCompanySkeletonAsync), entries[0].Name);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  받침대
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 자동 기표가 쓰는 계정과목 코드를 <see cref="HitPan.Application.Services.AutoJournalHelper"/> 에서 읽어 온다.
    /// 목록을 여기 다시 적으면 두 곳이 갈라진다 — 그 갈라짐이 이 프로젝트가 반복해 온 사고다.
    /// </summary>
    private static IReadOnlyCollection<string> AutoJournalAccountCodes()
    {
        // AutoJournalHelper 는 internal 이라 이름으로 집는다 — 이름이 바뀌면 여기서 바로 터진다(조용히 0개가 되지 않게).
        var helper = typeof(HitPan.Application.Services.MdbMigrationService).Assembly
            .GetType("HitPan.Application.Services.AutoJournalHelper", throwOnError: true)!;
        var types = new List<Type> { helper };
        types.AddRange(helper.GetNestedTypes(System.Reflection.BindingFlags.Public));

        return types
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Public
                                       | System.Reflection.BindingFlags.Static
                                       | System.Reflection.BindingFlags.FlattenHierarchy))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.GetRawConstantValue() as string)
            .Where(v => v is not null && v.Length == 5 && v.All(char.IsDigit))
            .Select(v => v!)
            .Distinct()
            .ToList();
    }

    /// <summary>지우기가 <b>불렸는지</b>를 증언하는 가짜. 실제로는 아무것도 지우지 않는다.</summary>
    private sealed class SpyDataResetService : IDataResetService
    {
        public bool Called { get; private set; }

        public Task<DataResetResponse> ResetAllAsync(
            DataResetRequest request, string tenantId, string userId, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(new DataResetResponse { Success = false, Error = "게이트용 가짜" });
        }
    }

    /// <summary>모드 검사·확인 검사만 타는 자리라 나머지 의존은 넣지 않는다 — 만지면 그 자리에서 터진다(그게 증명이다).</summary>
    /// <param name="fromOtherPc">참이면 터널을 지나온 요청처럼 꾸민다(= 자료 보관 컴퓨터가 아니다).</param>
    private static MigrationController NewController(IDataResetService dataReset, bool fromOtherPc = false)
    {
        var c = new MigrationController(
            migrationService: null!, logger: NullLogger<MigrationController>.Instance,
            jobStore: null!, scopeFactory: null!, progress: null!, reconciliation: null!,
            dataReset: dataReset, provisioner: null!, db: null!);

        var http = new DefaultHttpContext();
        http.Items["TenantId"] = "gate-tenant";
        http.Items["UserId"] = "gate-user";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        if (fromOtherPc) { http.Request.Headers["CF-Connecting-IP"] = "203.0.113.9"; }
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static CompanyBootstrapProvisioner NewProvisioner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnString(),
            })
            .Build();
        return new CompanyBootstrapProvisioner(config, NullLogger<CompanyBootstrapProvisioner>.Instance);
    }

    private static string NewTenantId() => "gate-ow-" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>재시드가 대표 사원을 만들려면 부모계정이 있어야 한다(초기화는 부모계정을 보존한다).</summary>
    private static void SeedParentUser(MySqlConnection db, string tenantId)
        => db.Execute(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name,
                               role, account_type, is_parent, is_active, failed_login_count,
                               created_at, updated_at, is_deleted, emp_name)
            VALUES (@U, @T, @E, 'x', '게이트대표', 'TenantAdmin', 'tenant_admin', 1, 1, 0,
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0, '게이트대표')",
            new { U = "u-" + tenantId, T = tenantId, E = tenantId + "@gate.local" });

    private static int CountOf(MySqlConnection db, string table, string tenantId)
        => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{table}` WHERE tenant_id = @T", new { T = tenantId });

    private static string Snapshot(MySqlConnection db, string tenantId)
        => string.Join("|", new[] { "accounts", "positions", "labor_policy_settings", "warehouses", "employees" }
            .Select(t => $"{t}={CountOf(db, t, tenantId)}"));

    /// <summary>자기가 만든 회사만 지운다 — 다른 회사는 건드리지 않는다(헌법 #2·#39).</summary>
    private static void CleanupTenant(MySqlConnection db, string tenantId)
    {
        foreach (var t in new[] { "employees", "positions", "labor_policy_settings", "warehouses", "accounts", "users" })
        {
            try { db.Execute($"DELETE FROM `{t}` WHERE tenant_id = @T", new { T = tenantId }); }
            catch (MySqlException ex)
            {
                // 삼키지 않는다(헌법 #15) — 남은 흔적이 다음 실행을 헷갈리게 하므로 반드시 보인다.
                Console.Error.WriteLine($"[게이트 뒷정리] {t} 지우기 실패 tenant={tenantId}: {ex.Message}");
            }
        }
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 (작14 W1)
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    private static void Skipped(string gate) => DbGateEnvironment.SkipOrFail(gate);

    private static MySqlConnection Open()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        return db;
    }

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "AllowUserVariables=true;GuidFormat=None;Connection Timeout=5;";
    }
}
