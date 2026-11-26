using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51-후1 갈래 1(작업지시서 20261127작 §3 S1 · §5-2 · PM 결재 K1) — <b>판매 반품 상세 화면의 품목은 그 상세행 회사의 품목만</b>.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — 판매 반품 상세 읽기(<c>SalesService.GetSalesReturnDetailAsync</c>)의 줄 SQL 이 <c>items</c> 를 <c>item_id</c> 하나로만 이었다.
///   상세행이 다른 회사 품목 id 를 들고 있으면 그 회사의 품명 · 규격 · 단위가 이 회사 반품 화면에 보였다(HQA §247 「화면 표시도 점검 대상」).
///   고침 = 조인에 <c>i.tenant_id = sri.tenant_id</c> 한 조각 · LEFT JOIN 유지(줄 · 금액은 남고 품명은 빈 글자 · 규격 · 단위는 빈 값).
/// 🔴 <b>동작으로 본다</b> — 실제 MariaDB 의 <b>TEMPORARY 표</b>(실표는 가리기만 · 헌법 #39)에 두 회사 자료를 넣고
///   제품 <b>공개</b> 함수 <c>GetSalesReturnDetailAsync</c> 를 그대로 부른다(반사 0 · 생성 꼴 = <c>LegacyBalanceFormulaGateTests</c> 선례). 한 연결 · 직렬(#16).
/// 🔴 <b>사실 셋</b> — D-1 같은 회사 = 품명 · 규격 있음 · D-2 🔴 다른 회사 품목 줄 = 품명 · 규격 빈 칸 · 행 · 금액 남음
///   · D-0 대조군 = 회사 B 반품을 회사 B 로 읽으면 B 품목이 보임(D-2 의 빈 칸이 빈 측정이 아님).
/// 🔴 글자 비교라 글꼴과 무관하다 — 토큰은 전표 종이 시험과 같은 결로 ASCII 로 맞춘다(접두 SRDI · 다른 게이트 토큰과 겹침 0).
/// 🔴 표 칸 = 출하 DDL <c>installer/hitpan_db_clean.sql</c> 대조(#13 · #36) · 상세 읽기가 쓰는 칸만 둔 부분 집합.
///   <c>is_loss</c> 칸은 제품이 <c>information_schema</c>(실표 메타 · 읽기만)로 있나 보고 고른다 — TEMPORARY 표에 칸을 두어 어느 쪽 길이든 같은 SQL 이 돈다.
/// 🔴 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 이 게이트를 돌리지 않는다(CI 업무 로직 게이트 잡이 문다).
/// </remarks>
public sealed class SalesReturnDetailItemTenantGateDbTests
{
    private const string TenantA = "GATE-SRDI-TENANT-A";
    private const string TenantB = "GATE-SRDI-TENANT-B";
    private const string PartnerA = "GATE-SRDI-PARTNER-A";
    private const string PartnerB = "GATE-SRDI-PARTNER-B";
    private const string ItemA1 = "GATE-SRDI-ITEM-A1";
    private const string ItemA2 = "GATE-SRDI-ITEM-A2";
    private const string ItemB1 = "GATE-SRDI-ITEM-B1";
    private const string NameA1 = "SRDINAMEAONE";
    private const string SpecA1 = "SRDISPECA77";
    private const string NameA2 = "SRDINAMEATWO";
    private const string SpecA2 = "SRDISPECA88";
    private const string NameB1 = "SRDISECRETB";
    private const string SpecB1 = "SRDISPECB99";
    private const string UnitA = "SRDIUA";
    private const string UnitB = "SRDIUB";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>줄 하나 — 금액은 줄마다 다르게 둬 줄을 가른다.</summary>
    private sealed record Line(string LineId, string ItemId, decimal Qty, decimal UnitPrice)
    {
        public string ReturnItemId(string returnId) => $"{returnId}-{LineId}";
        public decimal Supply => Qty * UnitPrice;
        public decimal Vat => Supply / 10m;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  D-1 같은 회사 — 그대로
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "후1 D-1 판매 반품 상세 · 같은 회사 품목 = 품명 · 규격 · 단위 · 금액 그대로")]
    public async Task D1_SameTenantItem_Unchanged()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("SalesReturnDetailItemTenantGate D-1"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var l1 = new Line("L1", ItemA1, 2m, 15850m);
        var l2 = new Line("L2", ItemA2, 3m, 16300m);
        SeedReturn(db, TenantA, PartnerA, "SRDI-D1", "SRDI-D1-NO", new[] { l1, l2 });

        var detail = await Sales(db, TenantA).GetSalesReturnDetailAsync("SRDI-D1", TenantA);

        Assert.NotNull(detail);
        Assert.Equal("SRDI-D1-NO", detail!.ReturnNo);
        Assert.Equal(2, detail.Items.Count);
        var r1 = Row(detail, l1.ReturnItemId("SRDI-D1"));
        Assert.Equal(NameA1, r1.ItemName);
        Assert.Equal(SpecA1, r1.Spec);
        Assert.Equal(UnitA, r1.Unit);
        Assert.Equal(l1.Supply, r1.SupplyAmount);
        var r2 = Row(detail, l2.ReturnItemId("SRDI-D1"));
        Assert.Equal(NameA2, r2.ItemName);
        Assert.Equal(SpecA2, r2.Spec);
        Assert.Equal(l2.Supply, r2.SupplyAmount);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  D-2 🔴 다른 회사 품목 — 품명 · 규격 빈 칸 · 줄은 남음
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "후1 D-2 🔴 판매 반품 상세 · 다른 회사 품목 줄 = 품명 · 규격 · 단위 빈 칸 · 행 · 금액은 남음")]
    public async Task D2_OtherTenantItem_Hidden()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("SalesReturnDetailItemTenantGate D-2"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var own = new Line("L1", ItemA1, 2m, 15850m);
        var foreign = new Line("L2", ItemB1, 3m, 16300m);   // 회사 A 상세행이 회사 B 품목 id 를 든 줄(쓰기 길 결함 재현)
        SeedReturn(db, TenantA, PartnerA, "SRDI-D2", "SRDI-D2-NO", new[] { own, foreign });

        var detail = await Sales(db, TenantA).GetSalesReturnDetailAsync("SRDI-D2", TenantA);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Items.Count);   // 행 남음(LEFT JOIN 유지)
        var f = Row(detail, foreign.ReturnItemId("SRDI-D2"));
        Assert.Equal(string.Empty, f.ItemName);   // 제품 SQL 이 COALESCE(i.item_name,'') — 못 찾으면 빈 글자
        Assert.Null(f.Spec);
        Assert.Null(f.Unit);
        Assert.Equal(ItemB1, f.ItemId);            // 상세행 값은 그대로 돌려줌(이미 들어간 자료 손 0)
        Assert.Equal(foreign.Supply, f.SupplyAmount);
        Assert.Equal(foreign.Vat, f.VatAmount);
        var o = Row(detail, own.ReturnItemId("SRDI-D2"));
        Assert.Equal(NameA1, o.ItemName);
        Assert.Equal(SpecA1, o.Spec);
        Assert.Equal(own.Supply, o.SupplyAmount);
        // 회사 B 품목의 어떤 글자도 응답 어디에도 없다
        foreach (var it in detail.Items)
        {
            Assert.NotEqual(NameB1, it.ItemName);
            Assert.NotEqual(SpecB1, it.Spec);
            Assert.NotEqual(UnitB, it.Unit);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  D-0 대조군 — 그 회사로 읽으면 그 품목이 보인다
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "후1 D-0 대조군 · 판매 반품 상세 · 회사 B 반품을 회사 B 로 읽으면 B 품목이 보인다")]
    public async Task D0_Control_OwnTenantVisible()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail("SalesReturnDetailItemTenantGate D-0"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var line = new Line("L1", ItemB1, 3m, 16300m);
        SeedReturn(db, TenantB, PartnerB, "SRDI-D0", "SRDI-D0-NO", new[] { line });

        var detail = await Sales(db, TenantB).GetSalesReturnDetailAsync("SRDI-D0", TenantB);

        Assert.NotNull(detail);
        var r = Assert.Single(detail!.Items);
        Assert.Equal(NameB1, r.ItemName);
        Assert.Equal(SpecB1, r.Spec);
        Assert.Equal(UnitB, r.Unit);
        Assert.Equal(line.Supply, r.SupplyAmount);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ────────────────────────────────────────────────────────────────────────

    private static SalesReturnDetailItemDto Row(SalesReturnDetailDto d, string returnItemId) =>
        Assert.Single(d.Items, x => x.ReturnItemId == returnItemId);

    /// <summary>제품 서비스를 그대로 만든다(선례 <c>LegacyBalanceFormulaGateTests</c>) — 상세 읽기는 넘긴 연결과 인자 tenantId 만 쓴다.</summary>
    private static SalesService Sales(MySqlConnection db, string tenant)
    {
        var current = new Mock<ICurrentTenant>();
        current.SetupGet(x => x.TenantId).Returns(tenant);
        current.SetupGet(x => x.UserId).Returns("srdi-user");
        return new SalesService(Mock.Of<IUnitOfWork>(), current.Object, db, null!, Mock.Of<IAuditService>(), null!);
    }

    /// <summary>두 회사 · 거래처 · 품목 A1 · A2(회사 A) · B1(회사 B).</summary>
    private static void SeedMasters(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, unit, spec) VALUES
              ('{ItemA1}', '{TenantA}', 'SRDI-A1', '{NameA1}', '{UnitA}', '{SpecA1}'),
              ('{ItemA2}', '{TenantA}', 'SRDI-A2', '{NameA2}', '{UnitA}', '{SpecA2}'),
              ('{ItemB1}', '{TenantB}', 'SRDI-B1', '{NameB1}', '{UnitB}', '{SpecB1}');
            """);
        Exec(db, $"""
            INSERT INTO partners (partner_id, tenant_id, partner_name) VALUES
              ('{PartnerA}', '{TenantA}', 'SRDIPARTNERA'),
              ('{PartnerB}', '{TenantB}', 'SRDIPARTNERB');
            """);
    }

    /// <summary>판매 반품 머리 1 · 줄 n(draft · 원 거래명세 없음).</summary>
    private static void SeedReturn(MySqlConnection db, string tenant, string partner,
        string returnId, string returnNo, Line[] lines)
    {
        Exec(db, $"""
            INSERT INTO sales_returns (return_id, tenant_id, return_no, partner_id, return_date, status, total_amount, vat_amount)
            VALUES ('{returnId}', '{tenant}', '{returnNo}', '{partner}', '2026-11-27', 'draft',
                    {Sql(lines.Sum(l => l.Supply))}, {Sql(lines.Sum(l => l.Vat))});
            """);
        foreach (var l in lines)
        {
            Exec(db, $"""
                INSERT INTO sales_return_items (return_item_id, return_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
                VALUES ('{l.ReturnItemId(returnId)}', '{returnId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                """);
        }
    }

    /// <summary>SQL 숫자 글자 — 문화권과 상관없이 소수점 「.」.</summary>
    private static string Sql(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 (기존 게이트 관례).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

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

    /// <summary>
    /// 시험용 <c>TEMPORARY</c> 표. 커넥션이 닫히면 서버가 지운다 —
    /// 이 DB 의 실제 표는 <b>가리기만 하고 건드리지 않는다</b>(헌법 #39).
    /// 칸 = 출하 DDL 의 부분 집합(상세 읽기 머리 · 줄 SQL 이 쓰는 칸 · 형 · NULL 여부 그대로) · <c>items</c> 는 DDL 대로 <c>item_id</c> 단일 PK.
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
              unit varchar(10) NOT NULL,
              spec varchar(100) DEFAULT NULL,
              PRIMARY KEY (item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE partners (
              partner_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              partner_name varchar(100) NOT NULL,
              PRIMARY KEY (partner_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_returns (
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              return_no varchar(20) NOT NULL,
              delivery_id varchar(36) DEFAULT NULL,
              partner_id varchar(36) NOT NULL,
              return_date date NOT NULL,
              return_reason varchar(30) NOT NULL DEFAULT 'customer_return',
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              return_reason_memo varchar(500) DEFAULT NULL,
              PRIMARY KEY (return_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_return_items (
              return_item_id varchar(36) NOT NULL,
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              delivery_item_id varchar(36) DEFAULT NULL,
              item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              warehouse_id varchar(36) DEFAULT NULL,
              is_loss tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (return_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
