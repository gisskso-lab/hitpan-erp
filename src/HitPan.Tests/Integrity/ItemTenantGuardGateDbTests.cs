using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 후1 갈래 2(작업지시서 20261127작 §4-1 · §5-3 · PM 결재 K2) — 공용 검사 함수 <see cref="ItemTenantGuard"/> 를 직접 잰다.
/// </summary>
/// <remarks>
/// 🔴 <b>동작으로 본다</b> — 실제 MariaDB 의 <b>TEMPORARY</b> <c>items</c> 표(실표는 가리기만 · 헌법 #39)에 두 회사 품목을 넣고
///   제품 공개 함수를 부른다(반사 0). 가짜 연결은 콜레이션 · <c>IN</c> 목록 펼침을 재지 못한다.
/// 🔴 사실 — F-1 같은 회사 여럿 통과 · F-2 🔴 B 품목 섞이면 거절(문구 같음) · F-3 없는 id 거절 · F-4 빈 · 공백 id 만 = 통과(DB 안 감)
///   · F-5 같은 id 대소문자 · 끝 공백만 다른 둘 = 통과 · F-6 회사 값 빈 = 거절(DB 안 감) · F-7 품목 200개 묶음 통과 / 하나 섞이면 거절(Q6).
/// 🔴 F-4 · F-6 의 「DB 안 감」 은 <b>닿을 수 없는 연결</b>(127.0.0.1:1)로 잰다 — DB 로 갔다면 연결 예외가 난다.
/// 🔴 표 칸 = 출하 DDL <c>installer/hitpan_db_clean.sql</c> <c>items</c> 의 부분 집합(#13 · #36) · <c>item_id</c> 단일 PK · <c>utf8mb4_unicode_ci</c>.
/// 🔴 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 이 게이트를 돌리지 않는다(CI DB 게이트 잡이 문다).
/// </remarks>
public sealed class ItemTenantGuardGateDbTests
{
    private const string TenantA = "GATE-ITG-TENANT-A";
    private const string TenantB = "GATE-ITG-TENANT-B";
    private const string ItemA1 = "GATE-ITG-ITEM-A1";
    private const string ItemA2 = "GATE-ITG-ITEM-A2";
    private const string ItemB1 = "GATE-ITG-ITEM-B1";
    private const string Missing = "GATE-ITG-ITEM-NONE";

    private static string Msg => ItemTenantGuard.NotThisTenantItemMessage;

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    [Fact(DisplayName = "후1 F-1 같은 회사 품목 여럿 = 통과")]
    public async Task F1_SameTenantItems_Pass()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("ItemTenantGuardGate F-1"); return; }
        using var db = FreshDb();

        var ex = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, new[] { ItemA1, ItemA2 }, null, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "후1 F-2 🔴 다른 회사 품목 하나라도 섞이면 = 거절 · 문구 같음")]
    public async Task F2_OtherTenantItemMixed_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("ItemTenantGuardGate F-2"); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, new[] { ItemA1, ItemB1 }, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);

        // 대조군 — 회사 B 로 B1 만 보면 통과(F-2 의 거절이 표 · 씨앗 결함 탓이 아님)
        var control = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantB, new[] { ItemB1 }, null, CancellationToken.None));
        Assert.Null(control);
    }

    [Fact(DisplayName = "후1 F-3 🔴 없는 품목 id = 거절 · 문구 같음(다른 회사 품목과 가르지 않음)")]
    public async Task F3_MissingItem_Rejected()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("ItemTenantGuardGate F-3"); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, new[] { ItemA1, Missing }, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);
    }

    [Fact(DisplayName = "후1 F-4 빈 · 공백 id 만 = 통과 · DB 안 감(닿을 수 없는 연결)")]
    public async Task F4_BlankIdsOnly_PassWithoutDb()
    {
        using var unreachable = new MySqlConnection(UnreachableConnString());

        var ex = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            unreachable, TenantA, new string?[] { null, "", "   ", "\t" }, null, CancellationToken.None));

        Assert.Null(ex);
        // 빈 목록도 같다
        var ex2 = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            unreachable, TenantA, Array.Empty<string?>(), null, CancellationToken.None));
        Assert.Null(ex2);
    }

    [Fact(DisplayName = "후1 F-5 같은 id 대소문자 · 끝 공백만 다른 둘 = 통과(콜레이션과 같은 셈)")]
    public async Task F5_SameIdDifferentCase_Pass()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("ItemTenantGuardGate F-5"); return; }
        using var db = FreshDb();

        var ex = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, new[] { ItemA1, ItemA1.ToLowerInvariant(), " " + ItemA1 + " ", ItemA2 }, null, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "후1 F-6 🔴 회사 값 빈 = 거절 · DB 안 감(조용히 통과 금지 · 헌법 #2)")]
    public async Task F6_EmptyTenant_Rejected()
    {
        using var unreachable = new MySqlConnection(UnreachableConnString());

        foreach (var tenant in new[] { "", "   " })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
                unreachable, tenant, new[] { ItemA1 }, null, CancellationToken.None));
            Assert.Equal(Msg, ex.Message);
        }
    }

    [Fact(DisplayName = "후1 F-7 품목 200개 묶음 = 통과 · 다른 회사 하나 섞으면 = 거절(IN 목록 한 번)")]
    public async Task F7_LargeBatch()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("ItemTenantGuardGate F-7"); return; }
        using var db = FreshDb();
        var many = Enumerable.Range(1, 200).Select(i => $"GATE-ITG-BULK-{i:000}").ToList();
        Exec(db, "INSERT INTO items (item_id, tenant_id, item_code, item_name) VALUES "
               + string.Join(",", many.Select((id, i) => $"('{id}', '{TenantA}', 'ITGB{i:000}', 'ITGBULK{i:000}')")) + ";");

        var ok = await Record.ExceptionAsync(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, many, null, CancellationToken.None));
        Assert.Null(ok);

        var mixed = new List<string>(many) { ItemB1 };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ItemTenantGuard.EnsureItemsBelongToTenantAsync(
            db, TenantA, mixed, null, CancellationToken.None));
        Assert.Equal(Msg, ex.Message);
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 시험용 <c>TEMPORARY</c> <c>items</c> 표(커넥션이 닫히면 서버가 지운다 · 실표는 가리기만 · 헌법 #39) + 씨앗 A1 · A2(회사 A) · B1(회사 B).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_code varchar(30) NOT NULL,
              item_name varchar(100) NOT NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1,
              PRIMARY KEY (item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        // A2 는 쉬게 한 품목 — is_active 를 보지 않으므로 F-1 에서 통과해야 한다(§4-1 · 헌법 #20).
        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, is_active) VALUES
              ('{ItemA1}', '{TenantA}', 'ITG-A1', 'ITGNAMEAONE', 1),
              ('{ItemA2}', '{TenantA}', 'ITG-A2', 'ITGNAMEATWO', 0),
              ('{ItemB1}', '{TenantB}', 'ITG-B1', 'ITGSECRETB', 1);
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
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "AllowUserVariables=true;GuidFormat=None;Pooling=false;Connection Timeout=5;";
    }

    /// <summary>열리지 않는 연결 — 함수가 DB 로 가면 연결 예외가 나 「DB 안 감」 이 거짓임이 드러난다.</summary>
    /// 🔴 20261127 PM 고침 — 종전엔 연결 문자열 글자를 그대로 적어 비밀 탐지(TruffleHog SQLServer)에 걸렸다(자격증명 모양 금지).
    ///   빌더로 만들고 비번 칸은 아예 두지 않는다 — 1번 포트라 어차피 열리지 않는다.
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
