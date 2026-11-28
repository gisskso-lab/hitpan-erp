using System;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-X-11 후7 · W-X-12 후9 갈래 1(작업지시서 20261129작 · 설계 §2 · §6-1) — 공용 검사 함수 <see cref="WarehouseTenantGuard"/> 를 직접 잰다.
/// </summary>
/// <remarks>
/// 🔴 <b>동작으로 본다</b> — 실제 MariaDB 의 <b>TEMPORARY</b> <c>warehouses</c> 표(실표는 가리기만 · 헌법 #39)에 두 회사 창고를 넣고
///   제품 공개 함수를 부른다(반사 0). 가짜 연결은 콜레이션 · <c>IN</c> 목록 펼침 · <c>is_active</c> 조건을 재지 못한다.
/// 🔴 사실 8 — G-1 WA 여럿 통과 · G-2 🔴 WB 섞임 거절 · G-3 🔴 없는 id 거절 · G-4 🔴 WA0(쉬게 한 창고) 거절(Q3)
///   · G-5 빈 · 공백만 = 통과(DB 안 감) · G-6 WA 대소문자 다른 둘 = 통과 · G-7 🔴 회사 값 빈 = 거절(DB 안 감) · G-8 🔴 앞뒤 공백 id = 거절.
/// 🔴 G-5 · G-7 의 「DB 안 감」 은 <b>닿을 수 없는 연결</b>(127.0.0.1:1)로 잰다 — DB 로 갔다면 연결 예외가 난다.
/// 🔴 G-8 은 실제 DB 로 잰다 — 끝 공백 id(<c>WA + " "</c>)는 콜레이션(PAD SPACE)이 WA 와 같게 세므로 ③ 거절이 빠지면 통과해 버린다(변이 B ⓑ).
/// 🔴 표 칸 = 출하 DDL <c>installer/hitpan_db_clean.sql</c> <c>warehouses</c> 의 부분 집합(#13 · #36) · <c>warehouse_id</c> 단일 PK · <c>utf8mb4_unicode_ci</c>.
/// 🔴 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 이 게이트를 돌리지 않는다(CI DB 게이트 잡이 문다).
/// </remarks>
public sealed class WarehouseTenantGuardGateDbTests
{
    private const string TenantA = "GATE-WTG-TENANT-A";
    private const string TenantB = "GATE-WTG-TENANT-B";
    private const string WhA = "GATE-WTG-WH-A";
    private const string WhA2 = "GATE-WTG-WH-A2";
    private const string WhA0 = "GATE-WTG-WH-A0";
    private const string WhB = "GATE-WTG-WH-B";
    private const string Missing = "GATE-WTG-WH-NONE";

    private static string Msg => WarehouseTenantGuard.NotUsableWarehouseMessage;

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    [Fact(DisplayName = "후9 G-1 같은 회사 활성 창고 여럿 = 통과")]
    public async Task G1_SameTenantActiveWarehouses_Pass()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-1"); return; }
        using var db = FreshDb();

        var ex = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA, WhA2 }, null, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "후9 G-2 🔴 다른 회사 창고 하나라도 섞이면 = 거절 · 문구 같음")]
    public async Task G2_OtherTenantWarehouseMixed_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-2"); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA, WhB }, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);

        // 대조군 — 회사 B 로 WB 만 보면 통과(G-2 의 거절이 표 · 씨앗 결함 탓이 아님)
        var control = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantB, new[] { WhB }, null, CancellationToken.None));
        Assert.Null(control);
    }

    [Fact(DisplayName = "후9 G-3 🔴 없는 창고 id = 거절 · 문구 같음(다른 회사 창고와 가르지 않음)")]
    public async Task G3_MissingWarehouse_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-3"); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA, Missing }, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);
    }

    [Fact(DisplayName = "후9 G-4 🔴 쉬게 한 내 창고(WA0) = 거절 · 문구 같음(Q3 · 저장 입구 = 활성까지)")]
    public async Task G4_InactiveOwnWarehouse_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-4"); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA, WhA0 }, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);
    }

    [Fact(DisplayName = "후9 G-5 빈 · 공백만인 id = 통과 · DB 안 감(닿을 수 없는 연결 · 서버 기본창고 길 보존)")]
    public async Task G5_BlankIdsOnly_PassWithoutDb()
    {
        using var unreachable = new MySqlConnection(UnreachableConnString());

        var ex = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            unreachable, TenantA, new string?[] { null, "", "   ", "\t" }, null, CancellationToken.None));
        Assert.Null(ex);

        // 빈 목록도 같다
        var ex2 = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            unreachable, TenantA, Array.Empty<string?>(), null, CancellationToken.None));
        Assert.Null(ex2);
    }

    [Fact(DisplayName = "후9 G-6 같은 창고 id 대소문자만 다른 둘 = 통과(콜레이션과 같은 셈)")]
    public async Task G6_SameIdDifferentCase_Pass()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-6"); return; }
        using var db = FreshDb();

        var ex = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA, WhA.ToLowerInvariant() }, null, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "후9 G-7 🔴 회사 값 빈 = 거절 · DB 안 감(조용히 통과 금지 · 헌법 #2)")]
    public async Task G7_EmptyTenant_Rejected()
    {
        using var unreachable = new MySqlConnection(UnreachableConnString());

        foreach (var tenant in new[] { "", "   " })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
                unreachable, tenant, new[] { WhA }, null, CancellationToken.None));
            Assert.Equal(Msg, ex.Message);
        }
    }

    [Fact(DisplayName = "후9 G-8 🔴 앞 · 뒤 공백이 붙은 창고 id = 거절(자르지 않음 · K3 (가))")]
    public async Task G8_PaddedId_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("WarehouseTenantGuardGate G-8"); return; }
        using var db = FreshDb();

        foreach (var padded in new[] { " " + WhA, WhA + " " })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
                db, TenantA, new[] { padded }, null, CancellationToken.None));
            Assert.Equal(Msg, ex.Message);
        }

        // 대조군 — 공백 없는 같은 id 는 통과(거절이 씨앗 결함 탓이 아님)
        var control = await Record.ExceptionAsync(() => WarehouseTenantGuard.EnsureWarehousesUsableAsync(
            db, TenantA, new[] { WhA }, null, CancellationToken.None));
        Assert.Null(control);
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 시험용 <c>TEMPORARY</c> <c>warehouses</c> 표(커넥션이 닫히면 서버가 지운다 · 실표는 가리기만 · 헌법 #39)
    /// + 씨앗 WA · WA2(회사 A 활성) · WA0(회사 A 쉬게 함) · WB(회사 B 활성).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE warehouses (
              warehouse_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              wh_code varchar(20) NOT NULL,
              wh_name varchar(50) NOT NULL,
              is_active tinyint(1) NOT NULL,
              PRIMARY KEY (warehouse_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, is_active) VALUES
              ('{WhA}', '{TenantA}', 'WTG-A', 'WTGNAMEA', 1),
              ('{WhA2}', '{TenantA}', 'WTG-A2', 'WTGNAMEATWO', 1),
              ('{WhA0}', '{TenantA}', 'WTG-A0', 'WTGNAMEAREST', 0),
              ('{WhB}', '{TenantB}', 'WTG-B', 'WTGSECRETB', 1);
            """);
        return db;
    }

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 (기존 게이트 관례).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        // ⚠️ Pooling=false — 임시 표가 풀에 남은 연결로 새어 다른 게이트에 보이지 않게 한다.
        return new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), Database = TestDb, UserID = user, Password = pass,
            AllowUserVariables = true, GuidFormat = MySqlGuidFormat.None, Pooling = false, ConnectionTimeout = 5,
        }.ConnectionString;
    }

    /// <summary>열리지 않는 연결 — 함수가 DB 로 가면 연결 예외가 나 「DB 안 감」 이 거짓임이 드러난다(비번 칸 0 · 1번 포트).</summary>
    private static string UnreachableConnString() =>
        new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = 1,
            Database = "none",
            UserID = "none",
            Pooling = false,
            ConnectionTimeout = 1,
        }.ConnectionString;

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
        try
        {
            using var c = new MySqlConnection(ConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
