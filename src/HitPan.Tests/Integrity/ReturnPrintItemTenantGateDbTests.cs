using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 HQA ⑥ 보완(작업지시서 20261126작 §4 · PM 결재 K1 ~ K6) — <b>반품 종이의 품목은 그 상세행 회사의 품목만</b>.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — 매입 반품 · 판매 반품 로더의 줄 SQL 이 <c>items</c> 를 <c>item_id</c> 하나로만 이었다.
///   <c>items</c> 는 <c>item_id</c> 단일 PK 라, 상세행이 다른 회사 품목 id 를 들고 있으면 그 회사의 품명 · 규격이
///   이 회사 반품 종이(인쇄 · 메일 PDF)에 찍혔다(HQA ⑥). 고침 = 조인에 <c>i.tenant_id = 상세행.tenant_id</c> 한 조각.
/// 🔴 <b>동작으로 본다(글자 검사 아님)</b> — 실제 MariaDB 의 <b>TEMPORARY 표</b>(실표는 가리기만 · 헌법 #39)에 두 회사 자료를 넣고
///   메일 길과 같은 제품 공개 함수 <c>PdfRenderService.RenderDocumentAsync</c>(4인자 · overrides 없음)를 불러
///   그려진 PDF 를 PdfPig 로 읽는다. 비공개 로더를 반사로 부르지 않는다. 가짜 연결은 조인 조건을 재지 못한다(HQA ⑥).
/// 🔴 <b>여섯 사실</b> — G-1a/b (가) 같은 회사 품목은 그대로 · G-2a/b (나) 다른 회사 품목은 품명 · 규격 0 이되 줄 · 금액은 남음
///   · G-0a/b 대조군(그 회사로 그리면 그 품목이 보임 = G-2 의 「0」 이 빈 측정이 아님).
/// 🔴 변이(K4): 로더 조인의 회사 조각을 빼면 G-2a · G-2b 빨강 · G-1 · G-0 은 초록 그대로.
/// 🔴 표 칸 = 출하 DDL <c>installer/hitpan_db_clean.sql</c> 대조(#13 · #36) · 로더가 읽는 칸만 둔 부분 집합.
/// 🔴 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 이 게이트를 돌리지 않는다(CI 업무 로직 게이트 잡이 문다).
/// 🔴 <b>2026-11-26 회송 고침 — 잴 글자는 ASCII</b>: 첫 CI 런(DB 게이트 잡 = 리눅스 러너)에서 6건이 모두 빨강이었다.
///   PDF 글자를 보니 문서번호 · 날짜 · 금액(ASCII)은 읽혔고 한글은 전부 <c>\0</c> 였다 — 제품 기본 글꼴(맑은 고딕)이
///   그 러너에 없어 한글 글리프가 빈 글리프로 그려지고 PdfPig 가 글자를 되찾지 못한 것(시험 장치 결함 · 제품 조건과 무관).
///   그래서 품명 · 규격 · 거래처 이름처럼 <b>있다 / 없다를 재는 글자</b>는 글꼴과 상관없는 ASCII 토큰으로 둔다.
///   한글 안내문 0 검사는 남기되(윈도 러너에서는 뜻이 있다), 리눅스에서도 뜻이 있는 「찾음」 증거를 ASCII 로 함께 단다
///   (<see cref="AssertFound"/> — 못 찾으면 문서번호 칸이 반품 번호가 아니라 전표 id 로 그려진다).
/// </remarks>
public sealed class ReturnPrintItemTenantGateDbTests
{
    static ReturnPrintItemTenantGateDbTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private const string TenantA = "GATE-RPIT-TENANT-A";
    private const string TenantB = "GATE-RPIT-TENANT-B";
    private const string PartnerA = "GATE-RPIT-PARTNER-A";
    private const string PartnerB = "GATE-RPIT-PARTNER-B";
    private const string ItemA1 = "GATE-RPIT-ITEM-A1";
    private const string ItemA2 = "GATE-RPIT-ITEM-A2";
    private const string ItemB1 = "GATE-RPIT-ITEM-B1";
    // 종전(회송 고침 전 · 한글 = 리눅스 러너에서 \0 로 읽혀 6건 모두 빈 측정): NameA1 = "가회사부품" · SpecA1 = "A-규격77" · NameB1 = "나회사비밀품" · SpecB1 = "B-규격99"
    // 🔴 토큰끼리 서로의 부분 글자가 아니다(포함 판정이 섞이지 않게) · 공백 · 하이픈 0(칸 안 줄바꿈으로 토큰이 쪼개지지 않게).
    private const string NameA1 = "RPITNAMEAONE";
    private const string SpecA1 = "RPITSPECA77";
    private const string NameA2 = "RPITNAMEATWO";
    private const string SpecA2 = "RPITSPECA88";
    private const string NameB1 = "RPITSECRETB";
    private const string SpecB1 = "RPITSPECB99";
    private const string PartnerNameA = "RPITPARTNERA";
    private const string PartnerNameB = "RPITPARTNERB";
    private const string Missing = "원본 문서를 찾을 수 없습니다";

    private const string PurchaseKey = "purchase_return";
    private const string SalesKey = "sales_return";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>줄 하나 — 금액은 줄마다 다르게 둬 줄 셈이 된다.</summary>
    private sealed record Line(string LineId, string ItemId, decimal Qty, decimal UnitPrice)
    {
        public decimal Supply => Qty * UnitPrice;
        public decimal Vat => Supply / 10m;
        public decimal Total => Supply + Vat;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-1 (가) 같은 회사 — 그대로
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HQA⑥ G-1a (가) 매입 반품 · 같은 회사 품목 = 품명 · 규격 · 줄 2 · 합계 그대로")]
    public Task G1a_PurchaseReturn_SameTenantItem_Unchanged() => SameTenantUnchanged(PurchaseKey);

    [Fact(DisplayName = "HQA⑥ G-1b (가) 판매 반품 · 같은 회사 품목 = 품명 · 규격 · 줄 2 · 합계 그대로")]
    public Task G1b_SalesReturn_SameTenantItem_Unchanged() => SameTenantUnchanged(SalesKey);

    // ────────────────────────────────────────────────────────────────────────
    //  G-2 (나) 다른 회사 품목 — 품명 · 규격 0 · 줄은 남음
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HQA⑥ G-2a 🔴 (나) 매입 반품 · 다른 회사 품목 줄 = 품명 · 규격 글자 0 · 줄 금액은 남음")]
    public Task G2a_PurchaseReturn_OtherTenantItem_Hidden() => OtherTenantHidden(PurchaseKey);

    [Fact(DisplayName = "HQA⑥ G-2b 🔴 (나) 판매 반품 · 다른 회사 품목 줄 = 품명 · 규격 글자 0 · 줄 금액은 남음")]
    public Task G2b_SalesReturn_OtherTenantItem_Hidden() => OtherTenantHidden(SalesKey);

    // ────────────────────────────────────────────────────────────────────────
    //  G-0 대조군 — 그 회사로 그리면 그 품목이 보인다
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HQA⑥ G-0a 대조군 · 매입 반품 · 회사 B 반품을 회사 B 로 그리면 B 품목이 보인다")]
    public Task G0a_PurchaseReturn_Control_OwnTenantVisible() => ControlOwnTenantVisible(PurchaseKey);

    [Fact(DisplayName = "HQA⑥ G-0b 대조군 · 판매 반품 · 회사 B 반품을 회사 B 로 그리면 B 품목이 보인다")]
    public Task G0b_SalesReturn_Control_OwnTenantVisible() => ControlOwnTenantVisible(SalesKey);

    // ────────────────────────────────────────────────────────────────────────
    //  본문
    // ────────────────────────────────────────────────────────────────────────

    private static async Task SameTenantUnchanged(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"ReturnPrintItemTenantGate G-1 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        // 종전(회송 고침 전 · [4] F-4 「품명 정확히 2번」 꼴 취약): 두 줄 모두 ItemA1 · Assert.Equal(2, Regex.Matches(text, Regex.Escape(NameA1)).Count)
        //   → 줄마다 같은 회사의 <b>다른</b> 품목을 둬 「줄 1 = A1 품명 · 규격」 · 「줄 2 = A2 품명 · 규격」 을 각각 포함으로 잰다
        //     (글자 뽑기의 줄바꿈 · 낱말 끊기 · 다른 칸 우연 등장에 횟수가 흔들리지 않는다 · 뜻 = 같은 회사 품목 그대로).
        var lines = new[]
        {
            new Line("L1", ItemA1, 2m, 15850m),
            new Line("L2", ItemA2, 3m, 16300m),
        };
        var headTotal = SeedReturn(db, key, TenantA, PartnerA, "RPIT-G1", "RPIT-G1-NO", lines);

        var text = await RenderText(db, TenantA, key, "RPIT-G1");

        AssertFound(text, "RPIT-G1-NO", PartnerNameA);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
        Assert.Contains(NameA1, text, StringComparison.Ordinal);
        Assert.Contains(SpecA1, text, StringComparison.Ordinal);
        Assert.Contains(NameA2, text, StringComparison.Ordinal);
        Assert.Contains(SpecA2, text, StringComparison.Ordinal);
        foreach (var l in lines)
            Assert.Contains(l.Total.ToString("N0"), text, StringComparison.Ordinal);
        Assert.Contains(headTotal.ToString("N0"), text, StringComparison.Ordinal);
    }

    private static async Task OtherTenantHidden(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"ReturnPrintItemTenantGate G-2 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var own = new Line("L1", ItemA1, 2m, 15850m);
        var foreign = new Line("L2", ItemB1, 3m, 16300m);   // 회사 A 상세행이 회사 B 품목 id 를 든 줄(쓰기 길 결함 재현)
        SeedReturn(db, key, TenantA, PartnerA, "RPIT-G2", "RPIT-G2-NO", new[] { own, foreign });

        // 예외가 나면 이 줄에서 빨강(Q1 — NULL 품명 렌더) — 잡지 않는다.
        var text = await RenderText(db, TenantA, key, "RPIT-G2");

        AssertFound(text, "RPIT-G2-NO", PartnerNameA);
        Assert.DoesNotContain(NameB1, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SpecB1, text, StringComparison.Ordinal);
        Assert.Contains(foreign.Total.ToString("N0"), text, StringComparison.Ordinal);   // 줄은 남음(LEFT JOIN 유지)
        Assert.Contains(NameA1, text, StringComparison.Ordinal);
        Assert.Contains(own.Total.ToString("N0"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
    }

    private static async Task ControlOwnTenantVisible(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"ReturnPrintItemTenantGate G-0 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var line = new Line("L1", ItemB1, 3m, 16300m);
        SeedReturn(db, key, TenantB, PartnerB, "RPIT-G0", "RPIT-G0-NO", new[] { line });

        var text = await RenderText(db, TenantB, key, "RPIT-G0");

        AssertFound(text, "RPIT-G0-NO", PartnerNameB);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
        Assert.Contains(NameB1, text, StringComparison.Ordinal);
        Assert.Contains(SpecB1, text, StringComparison.Ordinal);
        Assert.Contains(line.Total.ToString("N0"), text, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 로더가 반품을 <b>찾았다</b>는 글꼴 무관 증거 — 찾으면 문서번호 칸 = 반품 번호(<c>…-NO</c>) · 거래처 칸 = 거래처 이름,
    /// 못 찾으면 문서번호 칸 = 전표 id · 거래처 칸 = 한글 안내문(리눅스 러너에서는 \0 로 읽혀 <see cref="Missing"/> 검사만으로는 못 가른다).
    /// </summary>
    private static void AssertFound(string text, string returnNo, string partnerName)
    {
        Assert.Contains(returnNo, text, StringComparison.Ordinal);
        Assert.Contains(partnerName, text, StringComparison.Ordinal);
    }

    /// <summary>메일 길과 같은 호출 꼴(4인자 · overrides 없음) — 양식은 빈 값(Moq) · PDF 글자를 돌려준다.</summary>
    private static async Task<string> RenderText(MySqlConnection db, string tenant, string key, string id)
    {
        var forms = new Mock<IFormTemplateService>();
        forms.Setup(f => f.GetDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FormTemplateDto?)null);
        var svc = new PdfRenderService(db, NullLogger<PdfRenderService>.Instance, forms.Object);

        var (bytes, _) = await svc.RenderDocumentAsync(tenant, key, id, CancellationToken.None);

        using var read = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return string.Join("\n", read.GetPages().Select(p => p.Text));
    }

    /// <summary>두 회사 · 거래처 · 품목 A1 · A2(회사 A) · B1(회사 B) · 회사 정보.</summary>
    private static void SeedMasters(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, spec) VALUES
              ('{ItemA1}', '{TenantA}', 'RPIT-A1', '{NameA1}', '{SpecA1}'),
              ('{ItemA2}', '{TenantA}', 'RPIT-A2', '{NameA2}', '{SpecA2}'),
              ('{ItemB1}', '{TenantB}', 'RPIT-B1', '{NameB1}', '{SpecB1}');
            """);
        // 종전(회송 고침 전): 거래처 이름 '가거래처' · '나거래처'(한글 — 리눅스 러너에서 못 읽어 「찾음」 증거로 못 쓴다)
        Exec(db, $"""
            INSERT INTO partners (partner_id, tenant_id, partner_name) VALUES
              ('{PartnerA}', '{TenantA}', '{PartnerNameA}'),
              ('{PartnerB}', '{TenantB}', '{PartnerNameB}');
            """);
        Exec(db, $"""
            INSERT INTO local_company (tenant_id, tenant_code, company_name) VALUES
              ('{TenantA}', 'RPITA', '가회사'),
              ('{TenantB}', 'RPITB', '나회사');
            """);
    }

    /// <summary>반품 머리 1 · 줄 n. 머리 합계 = 줄 공급가 합(공급가 규약) · 돌려주는 값 = 종이의 총 합계(공급가 + 부가세).</summary>
    private static decimal SeedReturn(MySqlConnection db, string key, string tenant, string partner,
        string returnId, string returnNo, Line[] lines)
    {
        var supply = lines.Sum(l => l.Supply);
        var vat = lines.Sum(l => l.Vat);
        var (headTable, itemTable) = key == PurchaseKey
            ? ("purchase_returns", "purchase_return_items")
            : ("sales_returns", "sales_return_items");

        Exec(db, $"""
            INSERT INTO {headTable} (return_id, tenant_id, return_no, partner_id, return_date, status, total_amount, vat_amount)
            VALUES ('{returnId}', '{tenant}', '{returnNo}', '{partner}', '2026-11-26', 'confirmed', {Sql(supply)}, {Sql(vat)});
            """);
        foreach (var l in lines)
        {
            Exec(db, $"""
                INSERT INTO {itemTable} (return_item_id, return_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
                VALUES ('{returnId}-{l.LineId}', '{returnId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                """);
        }
        return supply + vat;
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
    /// 칸 = 출하 DDL 의 부분 집합(로더 · 회사 정보 읽기가 쓰는 칸 · 형 · NULL 여부 그대로) · <c>items</c> 는 DDL 대로 <c>item_id</c> 단일 PK.
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
              spec varchar(100) DEFAULT NULL,
              PRIMARY KEY (item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE partners (
              partner_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              partner_name varchar(100) NOT NULL,
              biz_no varchar(12) DEFAULT NULL,
              address varchar(200) DEFAULT NULL,
              manager_name varchar(50) DEFAULT NULL,
              PRIMARY KEY (partner_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE local_company (
              tenant_id varchar(36) NOT NULL,
              tenant_code varchar(20) NOT NULL,
              company_name varchar(100) NOT NULL,
              biz_no varchar(12) DEFAULT NULL,
              ceo_name varchar(50) DEFAULT NULL,
              biz_type varchar(50) DEFAULT NULL,
              biz_item varchar(100) DEFAULT NULL,
              tel varchar(20) DEFAULT NULL,
              address varchar(200) DEFAULT NULL,
              logo_url varchar(200) DEFAULT NULL,
              seal_url varchar(200) DEFAULT NULL,
              PRIMARY KEY (tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_returns (
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              return_no varchar(20) NOT NULL,
              receipt_id varchar(36) DEFAULT NULL,
              partner_id varchar(36) NOT NULL,
              return_date date NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (return_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_return_items (
              return_item_id varchar(36) NOT NULL,
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              PRIMARY KEY (return_item_id)
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
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (return_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_return_items (
              return_item_id varchar(36) NOT NULL,
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
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
