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
/// 🔴 W-G1-51-후1 갈래 1(작업지시서 20261127작 §3 P1 ~ P6 · §5-1 · PM 결재 K1) — <b>전표 종이 6종의 품목은 그 회사의 품목만</b>.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — 견적 · 수주 · 거래명세 · 세금계산서 · 발주 · 매입 로더의 줄 SQL 이 <c>items</c> 를 <c>item_id</c> 하나로만 이었다.
///   <c>items</c> 는 <c>item_id</c> 단일 PK 라, 상세행이 다른 회사 품목 id 를 들고 있으면 그 회사의 품명 · 규격이
///   이 회사 종이(인쇄 · 메일 · 팩스 · 미리보기 PDF)에 찍혔다. 반품 2종은 #979 로 이미 막혔다(<see cref="ReturnPrintItemTenantGateDbTests"/>).
///   고침 = 조인에 회사 조건 한 조각 — 견적만 상세표에 <c>tenant_id</c> 칸이 없어 <c>i.tenant_id = @TenantId</c>,
///   나머지 5 = <c>i.tenant_id = 상세행.tenant_id</c>. LEFT JOIN 유지(줄 · 금액은 남고 품명 · 규격만 빈 칸).
/// 🔴 <b>동작으로 본다(글자 검사 아님)</b> — 실제 MariaDB 의 <b>TEMPORARY 표</b>(실표는 가리기만 · 헌법 #39)에 두 회사 자료를 넣고
///   메일 길과 같은 제품 공개 함수 <c>PdfRenderService.RenderDocumentAsync</c>(4인자 · overrides 없음)를 불러
///   그려진 PDF 를 PdfPig 로 읽는다. 비공개 로더를 반사로 부르지 않는다. 한 연결 · 직렬(#16).
/// 🔴 <b>사실 셋 × 6 key</b> — G-1 (가) 같은 회사 품목은 그대로(견적은 <c>@TenantId</c> 꼴이 실제로 찾아짐을 이 줄이 보인다 —
///   상세 칸 꼴로 잘못 쓰면 칸 없음 오류 → 줄 조회 실패 → 품명 0 으로 빨강) · G-2 (나) 다른 회사 품목은 품명 · 규격 0 이되 줄 · 금액은 남음
///   · G-0 대조군(그 회사로 그리면 그 품목이 보임 = G-2 의 「0」 이 빈 측정이 아님).
/// 🔴 변이(K5): 로더 조인의 회사 조각을 빼면 G-2 × 6 빨강 · G-1 · G-0 은 초록 그대로.
/// 🔴 <b>잴 글자는 ASCII</b>(#979 회송 교훈 · HQA6 개발명세서 덧붙임 1) — DB 게이트 잡(리눅스 러너)엔 제품 기본 글꼴(맑은 고딕)이 없어
///   PDF 의 한글이 <c>\0</c> 로 읽힌다 ⇒ 품명 · 규격 · 거래처 이름은 글꼴과 무관한 ASCII 토큰으로 두고,
///   「찾음」 증거(<see cref="AssertFound"/> = 전표 번호 + 거래처 ASCII 이름)를 함께 단다.
///   한글 안내문 0 검사는 곁에 둔다 — 윈도 러너에서만 뜻이 있고 리눅스에서는 늘 참이다.
/// 🔴 줄 금액은 <b>공급가액</b>으로 잰다 — 세금계산서 법정 서식은 줄마다 공급가액 · 세액만 찍고 합계를 안 찍는다(나머지 5종은 공급가액도 찍는다).
/// 🔴 표 칸 = 출하 DDL <c>installer/hitpan_db_clean.sql</c> 대조(#13 · #36) · 로더 · 회사 정보 읽기가 쓰는 칸만 둔 부분 집합(형 · NULL 여부 그대로).
///   <c>quotation_items</c> 는 DDL 대로 <c>tenant_id</c> 칸이 <b>없다</b>.
/// 🔴 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 이 게이트를 돌리지 않는다(CI 업무 로직 게이트 잡이 문다).
/// </remarks>
public sealed class VoucherPrintItemTenantGateDbTests
{
    static VoucherPrintItemTenantGateDbTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private const string TenantA = "GATE-VPIT-TENANT-A";
    private const string TenantB = "GATE-VPIT-TENANT-B";
    private const string PartnerA = "GATE-VPIT-PARTNER-A";
    private const string PartnerB = "GATE-VPIT-PARTNER-B";
    private const string ItemA1 = "GATE-VPIT-ITEM-A1";
    private const string ItemA2 = "GATE-VPIT-ITEM-A2";
    private const string ItemB1 = "GATE-VPIT-ITEM-B1";
    // 🔴 토큰끼리 서로의 부분 글자가 아니다(포함 판정이 섞이지 않게) · 공백 · 하이픈 0(칸 안 줄바꿈으로 토큰이 쪼개지지 않게)
    //   · 반품 시험 토큰(RPIT…)과 겹침 0.
    private const string NameA1 = "VPITNAMEAONE";
    private const string SpecA1 = "VPITSPECA77";
    private const string NameA2 = "VPITNAMEATWO";
    private const string SpecA2 = "VPITSPECA88";
    private const string NameB1 = "VPITSECRETB";
    private const string SpecB1 = "VPITSPECB99";
    private const string PartnerNameA = "VPITPARTNERA";
    private const string PartnerNameB = "VPITPARTNERB";
    private const string Missing = "원본 문서를 찾을 수 없습니다";

    private const string QuotationKey = "quotation";
    private const string SalesOrderKey = "sales_order";
    private const string DeliveryKey = "delivery";
    private const string TaxInvoiceKey = "tax_invoice";
    private const string PurchaseOrderKey = "purchase_order";
    private const string PurchaseReceiptKey = "purchase_receipt";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>줄 하나 — 금액은 줄마다 다르게 둬 줄 셈이 된다.</summary>
    private sealed record Line(string LineId, string ItemId, decimal Qty, decimal UnitPrice)
    {
        public decimal Supply => Qty * UnitPrice;
        public decimal Vat => Supply / 10m;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-1 (가) 같은 회사 — 그대로
    // ────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "후1 G-1 (가) 전표 종이 · 같은 회사 품목 = 품명 · 규격 · 줄 2 그대로 + 찾음 증거")]
    [InlineData(QuotationKey)]
    [InlineData(SalesOrderKey)]
    [InlineData(DeliveryKey)]
    [InlineData(TaxInvoiceKey)]
    [InlineData(PurchaseOrderKey)]
    [InlineData(PurchaseReceiptKey)]
    public async Task G1_SameTenantItem_Unchanged(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"VoucherPrintItemTenantGate G-1 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        // 줄마다 같은 회사의 <b>다른</b> 품목(A1 · A2)을 둬 각 품명 · 규격을 포함으로 잰다(HQA6 회송 [4] F-4 교훈 — 횟수 세기 꼴 안 씀).
        var lines = new[]
        {
            new Line("L1", ItemA1, 2m, 15850m),
            new Line("L2", ItemA2, 3m, 16300m),
        };
        SeedVoucher(db, key, TenantA, PartnerA, "VPIT-G1", "VPIT-G1-NO", lines);

        var text = await RenderText(db, TenantA, key, "VPIT-G1");

        AssertFound(text, "VPIT-G1-NO", PartnerNameA);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
        Assert.Contains(NameA1, text, StringComparison.Ordinal);
        Assert.Contains(SpecA1, text, StringComparison.Ordinal);
        Assert.Contains(NameA2, text, StringComparison.Ordinal);
        Assert.Contains(SpecA2, text, StringComparison.Ordinal);
        foreach (var l in lines)
            Assert.Contains(l.Supply.ToString("N0"), text, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-2 (나) 다른 회사 품목 — 품명 · 규격 0 · 줄은 남음
    // ────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "후1 G-2 🔴 (나) 전표 종이 · 다른 회사 품목 줄 = 품명 · 규격 글자 0 · 줄 금액은 남음")]
    [InlineData(QuotationKey)]
    [InlineData(SalesOrderKey)]
    [InlineData(DeliveryKey)]
    [InlineData(TaxInvoiceKey)]
    [InlineData(PurchaseOrderKey)]
    [InlineData(PurchaseReceiptKey)]
    public async Task G2_OtherTenantItem_Hidden(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"VoucherPrintItemTenantGate G-2 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var own = new Line("L1", ItemA1, 2m, 15850m);
        var foreign = new Line("L2", ItemB1, 3m, 16300m);   // 회사 A 상세행이 회사 B 품목 id 를 든 줄(쓰기 길 결함 재현)
        SeedVoucher(db, key, TenantA, PartnerA, "VPIT-G2", "VPIT-G2-NO", new[] { own, foreign });

        // 예외가 나면 이 줄에서 빨강(NULL 품명 렌더) — 잡지 않는다.
        var text = await RenderText(db, TenantA, key, "VPIT-G2");

        AssertFound(text, "VPIT-G2-NO", PartnerNameA);
        Assert.DoesNotContain(NameB1, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SpecB1, text, StringComparison.Ordinal);
        Assert.Contains(foreign.Supply.ToString("N0"), text, StringComparison.Ordinal);   // 줄은 남음(LEFT JOIN 유지)
        Assert.Contains(NameA1, text, StringComparison.Ordinal);
        Assert.Contains(SpecA1, text, StringComparison.Ordinal);
        Assert.Contains(own.Supply.ToString("N0"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  G-0 대조군 — 그 회사로 그리면 그 품목이 보인다
    // ────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "후1 G-0 대조군 · 전표 종이 · 회사 B 전표를 회사 B 로 그리면 B 품목이 보인다")]
    [InlineData(QuotationKey)]
    [InlineData(SalesOrderKey)]
    [InlineData(DeliveryKey)]
    [InlineData(TaxInvoiceKey)]
    [InlineData(PurchaseOrderKey)]
    [InlineData(PurchaseReceiptKey)]
    public async Task G0_Control_OwnTenantVisible(string key)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail($"VoucherPrintItemTenantGate G-0 ({key})"); return; }
        using var db = FreshDb();
        SeedMasters(db);
        var line = new Line("L1", ItemB1, 3m, 16300m);
        SeedVoucher(db, key, TenantB, PartnerB, "VPIT-G0", "VPIT-G0-NO", new[] { line });

        var text = await RenderText(db, TenantB, key, "VPIT-G0");

        AssertFound(text, "VPIT-G0-NO", PartnerNameB);
        Assert.DoesNotContain(Missing, text, StringComparison.Ordinal);
        Assert.Contains(NameB1, text, StringComparison.Ordinal);
        Assert.Contains(SpecB1, text, StringComparison.Ordinal);
        Assert.Contains(line.Supply.ToString("N0"), text, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 로더가 전표를 <b>찾았다</b>는 글꼴 무관 증거 — 찾으면 문서번호 칸(세금계산서는 승인번호 칸 · 미발행이면 거래명세 번호) = 전표 번호(<c>…-NO</c>)
    /// · 거래처 칸 = 거래처 이름, 못 찾으면 문서번호 칸 = 전표 id · 거래처 칸 = 한글 안내문(리눅스 러너에서는 \0 로 읽혀 <see cref="Missing"/> 검사만으로는 못 가른다).
    /// </summary>
    private static void AssertFound(string text, string docNo, string partnerName)
    {
        Assert.Contains(docNo, text, StringComparison.Ordinal);
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
            INSERT INTO items (item_id, tenant_id, item_code, item_name, unit, spec) VALUES
              ('{ItemA1}', '{TenantA}', 'VPIT-A1', '{NameA1}', 'EA', '{SpecA1}'),
              ('{ItemA2}', '{TenantA}', 'VPIT-A2', '{NameA2}', 'EA', '{SpecA2}'),
              ('{ItemB1}', '{TenantB}', 'VPIT-B1', '{NameB1}', 'EA', '{SpecB1}');
            """);
        Exec(db, $"""
            INSERT INTO partners (partner_id, tenant_id, partner_name) VALUES
              ('{PartnerA}', '{TenantA}', '{PartnerNameA}'),
              ('{PartnerB}', '{TenantB}', '{PartnerNameB}');
            """);
        Exec(db, $"""
            INSERT INTO local_company (tenant_id, tenant_code, company_name) VALUES
              ('{TenantA}', 'VPITA', '가회사'),
              ('{TenantB}', 'VPITB', '나회사');
            """);
    }

    /// <summary>
    /// 전표 머리 1 · 줄 n — key 마다 로더가 읽는 표 · 칸 이름이 다르다(출하 DDL 대조).
    /// 세금계산서 key 는 거래명세 표를 그대로 읽는다(품목 정본 = 거래명세 · <c>tax_invoices</c> 는 비워 둠 = 미발행 · 번호 = 거래명세 번호).
    /// </summary>
    private static void SeedVoucher(MySqlConnection db, string key, string tenant, string partner,
        string docId, string docNo, Line[] lines)
    {
        var supply = Sql(lines.Sum(l => l.Supply));
        var vat = Sql(lines.Sum(l => l.Vat));
        switch (key)
        {
            case QuotationKey:
                Exec(db, $"""
                    INSERT INTO quotations (quote_id, tenant_id, quote_no, partner_id, quote_date, status, total_amount, vat_amount)
                    VALUES ('{docId}', '{tenant}', '{docNo}', '{partner}', '2026-11-27', 'draft', {supply}, {vat});
                    """);
                var sort = 0;
                foreach (var l in lines)
                {
                    // 🔴 quotation_items 에는 tenant_id 칸이 없다(출하 DDL) — 회사는 머리(quotations)로만 묶인다.
                    Exec(db, $"""
                        INSERT INTO quotation_items (id, quote_id, item_id, qty, unit_price, amount, vat_amount, sort_order)
                        VALUES ('{docId}-{l.LineId}', '{docId}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)}, {sort++});
                        """);
                }
                break;

            case SalesOrderKey:
                Exec(db, $"""
                    INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, order_date, status, total_amount, vat_amount)
                    VALUES ('{docId}', '{tenant}', '{docNo}', '{partner}', '2026-11-27', 'confirmed', {supply}, {vat});
                    """);
                foreach (var l in lines)
                {
                    Exec(db, $"""
                        INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, unit_price, supply_amount, vat_amount)
                        VALUES ('{docId}-{l.LineId}', '{docId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                        """);
                }
                break;

            case DeliveryKey:
            case TaxInvoiceKey:
                Exec(db, $"""
                    INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, status, total_amount, vat_amount)
                    VALUES ('{docId}', '{tenant}', '{docNo}', '{partner}', '2026-11-27', 'confirmed', {supply}, {vat});
                    """);
                foreach (var l in lines)
                {
                    Exec(db, $"""
                        INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
                        VALUES ('{docId}-{l.LineId}', '{docId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                        """);
                }
                break;

            case PurchaseOrderKey:
                Exec(db, $"""
                    INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, po_date, status, total_amount, vat_amount)
                    VALUES ('{docId}', '{tenant}', '{docNo}', '{partner}', '2026-11-27', 'ordered', {supply}, {vat});
                    """);
                foreach (var l in lines)
                {
                    Exec(db, $"""
                        INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, unit_price, supply_amount, vat_amount)
                        VALUES ('{docId}-{l.LineId}', '{docId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                        """);
                }
                break;

            case PurchaseReceiptKey:
                Exec(db, $"""
                    INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, status, total_amount, vat_amount)
                    VALUES ('{docId}', '{tenant}', '{docNo}', '{partner}', '2026-11-27', 'confirmed', {supply}, {vat});
                    """);
                foreach (var l in lines)
                {
                    Exec(db, $"""
                        INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
                        VALUES ('{docId}-{l.LineId}', '{docId}', '{tenant}', '{l.ItemId}', {Sql(l.Qty)}, {Sql(l.UnitPrice)}, {Sql(l.Supply)}, {Sql(l.Vat)});
                        """);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "시험이 모르는 전표 key");
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
        // 연결 문자열 글자 대신 빌더(비밀 탐지 · 인계44 §6) — 값 · 옵션은 그대로.
        return new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), Database = TestDb, UserID = user, Password = pass,
            DefaultCommandTimeout = 90, GuidFormat = MySqlGuidFormat.None, AllowUserVariables = true,
        }.ConnectionString;
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
    /// 칸 = 출하 DDL 의 부분 집합(6 로더 · 세금계산서 머리 덮기 · 회사 정보 읽기가 쓰는 칸 · 형 · NULL 여부 그대로) · <c>items</c> 는 DDL 대로 <c>item_id</c> 단일 PK.
    /// 세금계산서 key 가 읽는 표 = <c>sales_deliveries</c> · <c>sales_delivery_items</c> · <c>items</c> · <c>partners</c>(대표자 · 업태 · 종목 · 전화 칸까지) · <c>tax_invoices</c> · <c>local_company</c>.
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
              biz_no varchar(12) DEFAULT NULL,
              ceo_name varchar(50) DEFAULT NULL,
              biz_type varchar(50) DEFAULT NULL,
              biz_item varchar(50) DEFAULT NULL,
              tel varchar(20) DEFAULT NULL,
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
            CREATE TEMPORARY TABLE quotations (
              quote_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              quote_no varchar(20) NOT NULL,
              partner_id varchar(36) NOT NULL,
              quote_date date NOT NULL,
              status enum('draft','submitted','accepted','rejected','expired','converted') NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (quote_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE quotation_items (
              id varchar(36) NOT NULL,
              quote_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              spec varchar(100) DEFAULT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              sort_order int(11) NOT NULL DEFAULT 0,
              PRIMARY KEY (id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_orders (
              order_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              order_no varchar(20) NOT NULL,
              partner_id varchar(36) NOT NULL,
              order_date date NOT NULL,
              status enum('draft','quotation','order','confirmed','partial','closed','invoiced','cancelled') NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (order_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_order_items (
              order_item_id varchar(36) NOT NULL,
              order_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              PRIMARY KEY (order_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_deliveries (
              delivery_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              delivery_no varchar(32) NOT NULL,
              order_id varchar(36) DEFAULT NULL,
              partner_id varchar(36) NOT NULL,
              delivery_date date NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (delivery_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_delivery_items (
              delivery_item_id varchar(36) NOT NULL,
              delivery_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              order_item_id varchar(36) DEFAULT NULL,
              item_id varchar(36) DEFAULT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              PRIMARY KEY (delivery_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE tax_invoices (
              invoice_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              delivery_id varchar(36) DEFAULT NULL,
              invoice_no varchar(32) NOT NULL,
              issued_at datetime(6) NOT NULL,
              status varchar(16) NOT NULL DEFAULT 'issued',
              hometax_approval_no varchar(50) DEFAULT NULL,
              amendment_reason varchar(200) DEFAULT NULL,
              remark1 varchar(100) DEFAULT NULL,
              PRIMARY KEY (invoice_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_orders (
              po_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              po_no varchar(20) NOT NULL,
              partner_id varchar(36) NOT NULL,
              po_date date NOT NULL,
              status enum('draft','ordered','partial','received','cancelled') NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              memo varchar(500) DEFAULT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (po_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_order_items (
              po_item_id varchar(36) NOT NULL,
              po_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              PRIMARY KEY (po_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipts (
              receipt_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              receipt_no varchar(32) NOT NULL,
              po_id varchar(36) DEFAULT NULL,
              partner_id varchar(36) NOT NULL,
              receipt_date date NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              memo varchar(500) DEFAULT NULL,
              PRIMARY KEY (receipt_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipt_items (
              receipt_item_id varchar(36) NOT NULL,
              receipt_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) DEFAULT NULL,
              qty decimal(15,3) NOT NULL,
              unit_price decimal(15,2) NOT NULL,
              supply_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              PRIMARY KEY (receipt_item_id)
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
