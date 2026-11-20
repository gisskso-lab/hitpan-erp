using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Email;
using HitPan.Application.DTOs.Fax;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 갈래 §59 「고침 1」 P1-1 (가) — 업데이트 옮기기가 「저장된 칸 0」 양식지의 기본을 사본으로 넘기지 않는가.
/// <b>실제 MariaDB</b> · 제품 <see cref="FormTemplateService"/> 를 실제 연결로 만들어 기동 2회를 재현한다.
/// </summary>
/// <remarks>
/// 작업지시서 `docs/운영기록/20261120작_WG1-51_갈래59_고침1_작업지시서.md` §3 S1~S8 · §4 M5 후보 A(CI db-gate).
/// 🔴 기동 = <b>새 연결 · 새 서비스 개체</b>로 <c>CheckYMigrationReadinessAsync</c> → <c>Ready</c> 면 <c>RunYMigrationOnceAsync</c>
///   (API 기동 배경 루프와 같은 순서). 준비 ③ 용 업데이트 성공 기록 1행은 이 시험이 시험 DB 에 넣고 지운다(워치독 · 업데이트 코드 무접촉 · #43).
/// 🔴 연결 = <c>HITPAN_TEST_DB</c> 가 있을 때만 — <b>기본값 없음</b>. 개발 PC 기본값이 실물 DB 로 떨어지는 길을 막는다.
///   없거나 못 붙으면 <see cref="DbGateEnvironment.SkipOrFail"/>(CI db-gate = 던짐 · 건너뜀은 통과가 아니다).
/// 🔴 이 파일은 제품의 새 함수(<c>FormCoordsStored</c>)를 부르지 않는다 — 고침 전 머리(<c>189ad5f6</c>) 위에 그대로 얹어
///   재현 런(빨강)을 낼 수 있어야 한다(작업지시서 §4 절차 1). S7 의 기대값은 시험 안의 표 글자다.
/// 🔴 그림은 제품 그림 뿌리(<see cref="FormBackgroundPaths.TenantRoot"/>) 아래 실제 PNG — 쓰기가 막히면 「[환경]」 으로 이름 붙여 던진다.
/// 🔴 보탬 2(PM 결재 §92 · [3-V] · [4] P2-1) — 제품 옮기기는 회사 조건 없이 DB 전체를 고른다. 그래서 옮기기 전에
///   ⑴ 호스트 · 포트 · DB 이름을 환경변수로 명시 받았을 때만 ⑵ DB 이름 허용 목록(<c>hitpan_e2e</c>) ⑶ 시험 회사 밖 옮기기 대상 0 —
///   하나라도 어긋나면 옮기기를 부르지 않고 「[환경]」 으로 실패한다. DB 전역에 닿으므로 병렬 끔 묶음에서 돈다([4] P3-1).
/// </remarks>
[Collection(BoSignupTenantKeyGateCollection.Name)]   // 보탬 2([4] P3-1) — 기존 DB 게이트 묶음 · 정의 = BoSignupTenantKeyGateTests.cs `DisableParallelization = true`
public sealed class FormYMigrationNoCoordsGateDbTests : IDisposable
{
    private const string Gate = "FormYMigrationNoCoordsGate";
    private const string ApiVersion = "0.59.1";            // 시험 전용 판 글자 — 실제 판과 겹치지 않는다
    private const string SeedDetail = "W-G1-51 §59 고침 1 시험 시드";
    private const string FormType = "estimate";
    private const string DocType = "quotation";

    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string OneCoordJson = "[{\"key\":\"partner_business_no\",\"x_mm\":30,\"y_mm\":40}]";
    private const string OffPaperJson = "[{\"key\":\"partner_business_no\",\"x_mm\":500,\"y_mm\":40}]";

    private readonly ITestOutputHelper _out;
    private readonly List<string> _tenants = new();
    private bool _seededApplyStatus;

    public FormYMigrationNoCoordsGateDbTests(ITestOutputHelper output) => _out = output;

    // ═════════════════════════════════════════════════════════════
    // 자 — 연결 · 시드 · 기동 · 읽기
    // ═════════════════════════════════════════════════════════════

    // 🔴 보탬 2 ⑵(PM 결재 §92) — DB 이름 허용 목록 = CI db-gate env 의 시험 DB 이름 글자 그대로
    //   (.github/workflows/build-and-test.yml db-gate `HITPAN_TEST_DB: 'hitpan_e2e'`). 대소문자 · 공백까지 같아야 한다.
    private static readonly string[] AllowedTestDbs = { "hitpan_e2e" };

    // 🔴 보탬 2([4] P3-3) — MariaDB 의 CHECK 제약 거절 오류 번호 ER_CONSTRAINT_FAILED(MariaDB 11.4 mysqld_error.h).
    //   MySqlConnector 의 MySqlErrorCode 에 이 번호 이름이 없어 숫자로 견준다. MySQL 8 의 3819 는 ERP(MariaDB)에 안 쓴다.
    private const int MariaDbCheckConstraintFailed = 4025;

    /// <summary>
    /// 시험 DB 연결 글자. <c>HITPAN_TEST_DB</c> 가 비면 null(연결 0 → <see cref="DbGateEnvironment.SkipOrFail"/> · 종전 그대로).
    /// 🔴 보탬 2 ⑴ · ⑵ — 값이 있는데 호스트 · 포트를 명시하지 않았거나 DB 이름이 허용 목록 밖이면 연결 열기 전 「[환경]」 으로 던진다.
    /// </summary>
    private static string? ConnString()
    {
        var database = Environment.GetEnvironmentVariable("HITPAN_TEST_DB");
        if (string.IsNullOrWhiteSpace(database)) return null;
        // 🔴 보탬 2 ⑴ — 기본값(127.0.0.1:3306)으로 떨어지지 않는다. 이 PC 의 3306 은 실물 서버다.
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST");
        var portText = Environment.GetEnvironmentVariable("HITPAN_DB_PORT");
        if (string.IsNullOrWhiteSpace(host) || !uint.TryParse(portText, out var port))
        {
            throw new Xunit.Sdk.XunitException(
                $"[환경] HITPAN_TEST_DB='{database}' 인데 HITPAN_DB_HOST · HITPAN_DB_PORT 를 명시하지 않았다"
                + $"(호스트 '{host}' · 포트 '{portText}') — 기본값(127.0.0.1:3306)으로 붙지 않는다. "
                + "이 시험은 제품 옮기기를 회사 조건 없이 부르므로 시험 DB 를 세 환경변수로 명시해야 한다.");
        }
        // 🔴 보탬 2 ⑵ — 허용 목록 밖 DB 이름이면 연결을 열지 않는다.
        if (!AllowedTestDbs.Contains(database, StringComparer.Ordinal))
        {
            throw new Xunit.Sdk.XunitException(
                $"[환경] HITPAN_TEST_DB='{database}' 는 허용 목록({string.Join(", ", AllowedTestDbs)}) 밖이다 — 연결을 열지 않는다. "
                + "이 시험은 제품 옮기기를 회사 조건 없이 부른다(실물 · 공용 DB 보호).");
        }
        return new MySqlConnectionStringBuilder
        {
            // 종전(보탬 2 전): Server = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "127.0.0.1",
            // 종전(보탬 2 전): Port = uint.TryParse(Environment.GetEnvironmentVariable("HITPAN_DB_PORT"), out var p) ? p : 3306,
            Server = host,
            Port = port,
            UserID = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root",
            Password = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "",
            Database = database,
            CharacterSet = "utf8mb4",
            // 종전(보탬 3 전): 속성은 위 6개(Server · Port · UserID · Password · Database · CharacterSet)뿐이었다.
            // 🔴 보탬 3(PM 판단 §101) — 제품 연결(InfrastructureExtensions.cs `GuidFormat=None` 줄)과 질의 뜻을 같게 한다.
            //   GuidFormat 이 빠지면 MySqlConnector 기본이 char(36) 칸을 Guid 로 돌려줘, string 으로 받는 제품 Dapper 읽기가
            //   InvalidCastException(IConvertible) · Error parsing column … Guid 로 터진다(2026-08-12 양식템플릿 사고 ·
            //   M5 1차 CI 두 런 실패 10 = 제품 동작 전에 멈춘 무효 측정). 같은 레포 DB 게이트 builder 관례와 같은 세 값.
            //   AllowLoadLocalInfile 은 넣지 않는다 — 이 시험과 이 시험이 부르는 제품 서비스에 파일 적재가 없다.
            GuidFormat = MySqlGuidFormat.None,
            AllowUserVariables = true,
            DefaultCommandTimeout = 90,
        }.ConnectionString;
    }

    private async Task<MySqlConnection?> OpenAsync()
    {
        var cs = ConnString();
        if (cs is null)
        {
            _out.WriteLine("[gate59f1] HITPAN_TEST_DB 없음 — 연결을 열지 않는다(실물 DB 보호).");
            DbGateEnvironment.SkipOrFail(Gate);
            return null;
        }
        var c = new MySqlConnection(cs);
        try
        {
            await c.OpenAsync();
            return c;
        }
        catch (MySqlException ex)
        {
            _out.WriteLine($"[gate59f1] MariaDB 에 못 붙었다: {ex.Message}");
            await c.DisposeAsync();
            DbGateEnvironment.SkipOrFail(Gate);
            return null;
        }
    }

    private async Task<MySqlConnection> OpenRequiredAsync() =>
        await OpenAsync() ?? throw new Xunit.Sdk.XunitException("[gate59f1] 앞에서 붙은 DB 에 다시 못 붙었다.");

    private async Task SeedApplyStatusAsync(DbConnection db)
    {
        await db.ExecuteAsync(
            @"INSERT IGNORE INTO local_update_apply_status (tenant_id, applied_version, result, detail, applied_at)
              VALUES (NULL, @V, 'success', @D, NOW(3))",
            new { V = ApiVersion, D = SeedDetail });
        _seededApplyStatus = true;
    }

    private string NewTenant()
    {
        var t = Guid.NewGuid().ToString();
        _tenants.Add(t);
        return t;
    }

    private static string WritePng(string tenant)
    {
        var dir = FormBackgroundPaths.TenantRoot(tenant);
        try
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, $"gate59f1-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
            return p;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"[환경] 그림 폴더 쓰기 거부 — {dir} · 이 러너가 제품 그림 뿌리(FormBackgroundPaths.Root)를 못 쓴다. "
                + $"시험 판정 전 환경 문제다: {ex.Message}");
        }
    }

    private static async Task<string> InsertAsync(DbConnection db, string tenant, string name, string paperMode,
        bool printBackground, string? coords, string? background, bool isDefault, string? id = null)
    {
        id ??= Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            @"INSERT INTO form_templates
                (template_id, tenant_id, form_type, template_name, paper_mode,
                 field_coords_json, background_image_url, print_background, is_default, is_active)
              VALUES (@Id, @T, @F, @N, @M, @C, @B, @P, @D, 1)",
            new
            {
                Id = id, T = tenant, F = FormType, N = name, M = paperMode,
                C = coords, B = background, P = printBackground ? 1 : 0, D = isDefault ? 1 : 0
            });
        return id;
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Mode { get; set; } = "";
        public bool Pb { get; set; }
        public bool Def { get; set; }
        public string? Coords { get; set; }
        public string? Bg { get; set; }
    }

    private static async Task<List<Row>> RowsAsync(DbConnection db, string tenant) =>
        (await db.QueryAsync<Row>(
            @"SELECT template_id AS Id, template_name AS Name, paper_mode AS Mode,
                     print_background AS Pb, is_default AS Def,
                     field_coords_json AS Coords, background_image_url AS Bg
              FROM form_templates WHERE tenant_id = @T ORDER BY template_id",
            new { T = tenant })).AsList();

    private static string Snap(IEnumerable<Row> rows) =>
        string.Join(" / ", rows.Select(r => $"{r.Id}:{r.Mode}:찍기{(r.Pb ? 1 : 0)}:기본{(r.Def ? 1 : 0)}"));

    private sealed record Boot(FormTemplateService.YMigrationReadiness Ready, int Created,
        List<(LogLevel Level, string Message)> Logs);

    /// <summary>기동 1회 = 새 연결 · 새 서비스 개체 · 준비 확인 → 옮기기.</summary>
    private async Task<Boot> BootAsync()
    {
        await using var db = await OpenRequiredAsync();
        await RequireNoOutsideTargetsAsync(db);   // 🔴 보탬 2 ⑶ — 제품 준비 확인 · 옮기기 부르기 전
        var log = new ListLogger<FormTemplateService>();
        var svc = new FormTemplateService(new GateUnitOfWork(db), log);
        var ready = await svc.CheckYMigrationReadinessAsync(ApiVersion);
        var created = ready == FormTemplateService.YMigrationReadiness.Ready ? await svc.RunYMigrationOnceAsync() : 0;
        _out.WriteLine($"[gate59f1] 기동 — 준비 {ready} · 만든 수 {created} · 기록 {log.Entries.Count}");
        return new Boot(ready, created, log.Entries);
    }

    /// <summary>
    /// 🔴 보탬 2 ⑶(PM 결재 §92 · [3-V] · [4] P2-1) — 제품 옮기기는 회사 조건 없이 DB 전체의 대상(양식지 · 찍기 1 · 활성)을 고른다.
    /// 이 시험이 만든 회사 밖에 대상이 한 행이라도 있으면 옮기기를 부르지 않고 「[환경]」 으로 실패한다(남의 회사 행을 옮기지 않는다).
    /// 고르기 세 조건 = <c>FormTemplateService.RunYMigrationOnceAsync</c> 의 WHERE 글자 그대로(칸 = 출하 DDL form_templates · #13).
    /// </summary>
    private async Task RequireNoOutsideTargetsAsync(DbConnection db)
    {
        var outside = _tenants.Count == 0
            ? await db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(*) FROM form_templates
                  WHERE paper_mode = 'preprint' AND print_background = 1 AND is_active = 1")
            : await db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(*) FROM form_templates
                  WHERE paper_mode = 'preprint' AND print_background = 1 AND is_active = 1
                    AND tenant_id NOT IN @Tenants",
                new { Tenants = _tenants });
        if (outside != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"[환경] 시험 회사 밖 옮기기 대상(양식지 · 찍기 1 · 활성)이 {outside}행 있다 — 제품 옮기기(회사 조건 없음)를 부르지 않는다. "
                + "공용 시험 DB 에 다른 회사 양식지 행이 남았거나, 시험 DB 가 아닌 곳을 가리킨다.");
        }
        _out.WriteLine($"[gate59f1] 옮기기 전 확인 — 시험 회사 밖 대상 0 · 시험 회사 {_tenants.Count}");
    }

    private static void NoWarnings(Boot b)
    {
        var warns = b.Logs.Where(l => l.Level >= LogLevel.Warning).Select(l => l.Message).ToList();
        Assert.True(warns.Count == 0, "옮기기가 경고를 냈다(되돌림 · 1062 등): " + string.Join(" | ", warns));
    }

    private async Task<string?> DefaultIdAsync(string tenant)
    {
        await using var db = await OpenRequiredAsync();
        var svc = new FormTemplateService(new GateUnitOfWork(db), NullLogger<FormTemplateService>.Instance);
        return (await svc.GetDefaultAsync(tenant, FormType))?.TemplateId;
    }

    private sealed record Paper(int Pages, int Images, int Glyphs, string Text)
    {
        public bool HasPageNumber => Text.Contains("Page", StringComparison.Ordinal);
    }

    private static Paper Read(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var pages = doc.GetPages().ToList();
        return new Paper(
            pages.Count,
            pages.Sum(p => p.GetImages().Count()),
            pages.Sum(p => p.Letters.Count),
            string.Concat(pages.Select(p => p.Text)));
    }

    private static string Describe(Paper p) =>
        $"쪽 {p.Pages} · 그림 {p.Images} · 글리프 {p.Glyphs} · 쪽번호 {(p.HasPageNumber ? "있음" : "없음")}";

    /// <summary>
    /// 인쇄 전 안내 문 판정 + 전표 [인쇄] 문 꼴 렌더 — 양식은 <b>실제 DB</b>(<c>GetDefaultAsync</c>) · 전표 자료는 예시 자료
    /// (기존 §59 T1 · T2 꼴 · 견적 행 시드 없음 · 개발명세서 §0 U4).
    /// </summary>
    private async Task<(bool Notice, Paper Paper)> PrintAsync(string tenant)
    {
        await using var db = await OpenRequiredAsync();
        var forms = new FormTemplateService(new GateUnitOfWork(db), NullLogger<FormTemplateService>.Instance);
        var pdf = new PdfRenderService(new Mock<IDbConnection>().Object, NullLogger<PdfRenderService>.Instance, forms);
        var notice = await pdf.IsNoCoordsImageOnlyAsync(tenant, DocType);
        var (bytes, _) = await pdf.RenderDocumentAsync(tenant, DocType, "gate59f1-doc",
            new PdfPreviewOverrides { UseSampleData = true, NoCoordsImageOnly = true });
        return (notice, Read(bytes));
    }

    /// <summary>기동 2 — 이 회사 옮기기 대상 0 · 다시 돌려도 행 무변(1회성 = 원본 찍기 내림 · F1).</summary>
    private async Task SecondBootUnchangedAsync(DbConnection db, string tenant, List<Row> before)
    {
        var targets = await db.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM form_templates
              WHERE tenant_id = @T AND paper_mode = 'preprint' AND print_background = 1 AND is_active = 1",
            new { T = tenant });
        Assert.True(targets == 0, $"기동 1 뒤에도 이 회사 옮기기 대상이 {targets}행 남았다 — 다음 기동에 다시 돈다.");
        var b2 = await BootAsync();
        NoWarnings(b2);
        Assert.Equal(Snap(before), Snap(await RowsAsync(db, tenant)));
    }

    // ═════════════════════════════════════════════════════════════
    // S1~S7 — 옮기기 길
    // ═════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 고침 1 S1 🔴 칸 0 양식지(명시 기본) 옮기기 = 사본은 만들되 기본 = 원본 · 기동 2 무변 · 안내 거짓 · 데이터 있음")]
    public async Task S1_NoCoords_ExplicitDefault_StaysOriginal()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var src = await InsertAsync(db, t, "gate59f1 견적서", "preprint", true, null, WritePng(t), isDefault: true);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        var orig = Assert.Single(rows, r => r.Id == src);
        var copy = Assert.Single(rows, r => r.Id != src);
        Assert.Equal("plain", copy.Mode);
        Assert.True(copy.Pb, "사본(빈 A4)의 그림 찍기가 꺼져 있다.");
        Assert.False(copy.Def, "칸 0 양식지의 사본이 기본이 됐다 — 업데이트 뒤 전표 데이터가 사라진다(P1-1).");
        Assert.True(orig.Def, "원본(데이터만)이 기본에서 내려갔다.");
        Assert.False(orig.Pb, "원본 찍기가 안 내려갔다 — 다음 기동에 다시 옮긴다.");

        await SecondBootUnchangedAsync(db, t, rows);
        Assert.Equal(src, await DefaultIdAsync(t));
        var (notice, paper) = await PrintAsync(t);
        Assert.False(notice, "기본이 원본(데이터만)인데 「그림만」 안내가 뜬다.");
        Assert.True(paper.Glyphs > 0, $"인쇄 문 종이에 데이터가 없다 — {Describe(paper)}");
    }

    [Fact(DisplayName = "§59 고침 1 S2 🔴 이름순 실효 기본(명시 0) · 원본 이름 95자 = 원본이 명시 기본으로 올라감(I2)")]
    public async Task S2_NameOrderDefault_LongName_OriginalPromoted()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var name = "gate59f1-" + new string('가', 86);
        Assert.Equal(95, name.Length);
        var src = await InsertAsync(db, t, name, "preprint", true, null, WritePng(t), isDefault: false);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        var orig = Assert.Single(rows, r => r.Id == src);
        var copy = Assert.Single(rows, r => r.Id != src);
        _out.WriteLine($"[S2] 원본 이름 {orig.Name.Length}자 · 사본 이름 {copy.Name.Length}자 「{copy.Name}」");
        Assert.True(orig.Def, "이름순 실효 기본이던 원본이 명시 기본으로 안 올라갔다 — 사본 이름이 앞에 정렬되면 사본이 기본이 된다(I2).");
        Assert.False(copy.Def, "칸 0 양식지의 사본이 기본이 됐다.");
        Assert.Equal(src, await DefaultIdAsync(t));
        await SecondBootUnchangedAsync(db, t, rows);
    }

    [Fact(DisplayName = "§59 고침 1 S3 🔴 같은 종 양식지 2행(B<A · A 명시 기본) — B 가 만든 사본을 A 가 재사용해도 기본 = A")]
    public async Task S3_TwoSources_ReuseKeepsA()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var png = WritePng(t);
        var idB = "1" + Guid.NewGuid().ToString()[1..];
        var idA = "2" + Guid.NewGuid().ToString()[1..];
        await InsertAsync(db, t, "gate59f1 B", "preprint", true, null, png, isDefault: false, id: idB);
        await InsertAsync(db, t, "gate59f1 A", "preprint", true, null, png, isDefault: true, id: idA);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        Assert.True(rows.Count == 3, "사본이 하나가 아니다(재사용 길 실패): " + Snap(rows));
        var copy = Assert.Single(rows, r => r.Id != idA && r.Id != idB);
        var a = rows.Single(r => r.Id == idA);
        var b = rows.Single(r => r.Id == idB);
        Assert.False(copy.Def, "재사용 길이 사본을 기본으로 올렸다.");
        Assert.True(a.Def, "A(명시 기본)가 기본에서 내려갔다.");
        Assert.False(a.Pb || b.Pb, "원본 찍기가 안 내려갔다(거래 되돌림 · 1062 의심): " + Snap(rows));
        Assert.Equal(idA, await DefaultIdAsync(t));
        await SecondBootUnchangedAsync(db, t, rows);
    }

    [Fact(DisplayName = "§59 고침 1 S4 🔴 이미 있는 빈 A4 Y 꼴 행 재사용(Reused) — 그 행 기본 0 · 원본 기본 1 · 기동 2 무변")]
    public async Task S4_ExistingYRow_Reused_KeepsOriginal()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var y = await InsertAsync(db, t, "gate59f1 이미 있는 빈 A4", "plain", true, null, WritePng(t), isDefault: false);
        var src = await InsertAsync(db, t, "gate59f1 원본", "preprint", true, null, WritePng(t), isDefault: true);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        Assert.True(rows.Count == 2 && b1.Created == 0, $"재사용(Reused)이 아니다 — 만든 수 {b1.Created} · " + Snap(rows));
        Assert.False(rows.Single(r => r.Id == y).Def, "재사용 길이 이미 있는 빈 A4 행을 기본으로 올렸다.");
        Assert.True(rows.Single(r => r.Id == src).Def, "원본이 기본에서 내려갔다.");
        Assert.False(rows.Single(r => r.Id == src).Pb, "원본 찍기가 안 내려갔다.");
        await SecondBootUnchangedAsync(db, t, rows);
    }

    [Fact(DisplayName = "§59 고침 1 S5 회귀 🔴 칸 1개 정상 = 종전대로 사본이 기본 · 안내 거짓 · 그림 + 칸 값")]
    public async Task S5_OneCoord_CopyBecomesDefault()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var src = await InsertAsync(db, t, "gate59f1 칸1", "preprint", true, OneCoordJson, WritePng(t), isDefault: true);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        var orig = Assert.Single(rows, r => r.Id == src);
        var copy = Assert.Single(rows, r => r.Id != src);
        Assert.True(copy.Def, "칸 있는 양식지의 사본이 기본이 안 됐다 — 종전 옮기기가 깨졌다.");
        Assert.False(orig.Def);
        Assert.False(orig.Pb);
        Assert.Equal(copy.Id, await DefaultIdAsync(t));
        var (notice, paper) = await PrintAsync(t);
        Assert.False(notice, "칸 있는 사본에 「그림만」 안내가 뜬다.");
        Assert.True(paper.Glyphs > 0 && paper.Images >= 1, $"그림 + 칸 값이 아니다 — {Describe(paper)}");
        await SecondBootUnchangedAsync(db, t, rows);
    }

    [Fact(DisplayName = "§59 고침 1 S6 🔴 칸 = 종이 밖 1칸(E9) = 사본이 기본(칸 있음 취급) · 안내 거짓 · 기본 배치 데이터")]
    public async Task S6_OffPaperCoord_CopyDefault_PrintsData()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        var src = await InsertAsync(db, t, "gate59f1 종이밖", "preprint", true, OffPaperJson, WritePng(t), isDefault: true);

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        var copy = Assert.Single(rows, r => r.Id != src);
        Assert.True(copy.Def, "저장값 있는(무효 칸) 양식지의 사본이 기본이 안 됐다.");
        Assert.Equal(copy.Id, await DefaultIdAsync(t));
        var (notice, paper) = await PrintAsync(t);
        Assert.False(notice, "모두 무효 칸 사본에 「그림만」 안내가 뜬다(안내 거짓 · #25).");
        Assert.True(paper.Glyphs > 0 && paper.HasPageNumber && paper.Images >= 1,
            $"기본 배치 + 데이터 + 그림이 아니다(데이터 빠짐 · #20) — {Describe(paper)}");
    }

    [Theory(DisplayName = "§59 고침 1 S7 🔴 저장값 표 × 실제 DB — 옮기기 기본 안 넘김 · 손 기본 지정 뒤 안내 = 저장된 칸 0")]
    [InlineData("E1", null, true)]
    [InlineData("E4", "[]", true)]
    [InlineData("E5", " [ ] ", true)]
    [InlineData("E2", "", true)]
    [InlineData("E3", " ", true)]
    [InlineData("E10", "[", false)]
    public async Task S7_StoredEmptyTable(string label, string? coords, bool storedEmpty)
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        await SeedApplyStatusAsync(db);
        var t = NewTenant();
        string src;
        try
        {
            src = await InsertAsync(db, t, $"gate59f1 {label}", "preprint", true, coords, WritePng(t), isDefault: true);
        }
        // 종전(보탬 2 전): catch (MySqlException ex) when (label is "E2" or "E3" or "E10")
        // 🔴 보탬 2([4] P3-3) — CHECK 거절(4025)만 받는다. 연결 끊김 · 칸 이름 틀림 등 그 밖 MySql 오류는 밖으로(빨강 · 거짓 초록 0).
        catch (MySqlException ex) when ((label is "E2" or "E3" or "E10") && ex.Number == MariaDbCheckConstraintFailed)
        {
            // U1 실측 — 출하 DDL 의 CHECK(json_valid) 가 이 값을 거절했다. E1 · E4 · E5 는 여기로 못 온다(넣어져야 함).
            _out.WriteLine($"[S7] {label} = DB 에 못 들어감 — {ex.ErrorCode} · {ex.Message}");
            return;
        }
        _out.WriteLine($"[S7] {label} = DB 에 들어감 — 옮기기 · 안내 판정을 잰다.");

        var b1 = await BootAsync();
        Assert.Equal(FormTemplateService.YMigrationReadiness.Ready, b1.Ready);
        NoWarnings(b1);
        var rows = await RowsAsync(db, t);
        var orig = Assert.Single(rows, r => r.Id == src);
        var copy = Assert.Single(rows, r => r.Id != src);
        Assert.True(orig.Def == storedEmpty,
            $"{label}: 원본이 기본으로 {(storedEmpty ? "남아야" : "안 남아야")} 한다 — 옮기기 판정이 「저장된 칸 0」 과 다르다.");
        Assert.True(copy.Def == !storedEmpty, $"{label}: 사본 기본 {(copy.Def ? 1 : 0)} — 옮기기 판정이 「저장된 칸 0」 과 다르다.");

        // 사본을 손으로 기본 지정 → 인쇄 전 안내 문 판정이 같은 답을 내나.
        await db.ExecuteAsync("UPDATE form_templates SET is_default = 0 WHERE tenant_id = @T AND template_id = @S",
            new { T = t, S = src });
        await db.ExecuteAsync("UPDATE form_templates SET is_default = 1 WHERE tenant_id = @T AND template_id = @C",
            new { T = t, C = copy.Id });
        Assert.Equal(copy.Id, await DefaultIdAsync(t));
        var (notice, paper) = await PrintAsync(t);
        Assert.True(notice == storedEmpty,
            $"{label}: 안내 문 판정 {notice} — 「저장된 칸 0」({storedEmpty}) 과 다르다 · {Describe(paper)}");
    }

    // ═════════════════════════════════════════════════════════════
    // S8 — Y 단추 길 무변(PM 결재 ②)
    // ═════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 고침 1 S8 🔴 Y 단추 길(CreateYTemplateAsync) = 종전 그대로 — 칸 0 이어도 사본이 기본")]
    public async Task S8_YButton_Unchanged()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        var t = NewTenant();
        // Y 단추 길 차림 — 저장 문이 원본 찍기를 이미 0 으로 둔 꼴(FormTemplateService ⑶ 주석).
        var src = await InsertAsync(db, t, "gate59f1 Y단추", "preprint", false, null, WritePng(t), isDefault: true);

        await using var c2 = await OpenRequiredAsync();
        var svc = new FormTemplateService(new GateUnitOfWork(c2), NullLogger<FormTemplateService>.Instance);
        var r = await svc.CreateYTemplateAsync(t, src);

        Assert.Equal(YTemplateOutcome.Created, r.Outcome);
        var rows = await RowsAsync(db, t);
        Assert.True(rows.Single(x => x.Id == r.YTemplateId).Def, "Y 단추 길 사본이 기본이 안 됐다 — 고침이 옮기기 길 밖으로 샜다.");
        Assert.False(rows.Single(x => x.Id == src).Def);
    }

    // ═════════════════════════════════════════════════════════════
    // U5′ — 메일 · 팩스 보내기 동작 시험(PM 결재 고침 ⑧ · 음성 시험 = 고침 1 이 안 바꾸는 길)
    //   실제 DB 에 설정 1행을 넣고 제품 서비스의 SendDocumentAsync 를 실제로 부른다. 렌더만 기록하는 가짜다.
    //   🔴 메일 = SMTP 127.0.0.1 · 닫힌 포트 1 → 바깥 연결 0 · 바로 거절. 팩스 = 가짜 공급자(실제 송출 0).
    //   🔴 시험이 넣은 행은 finally 에서 그 회사 것만 지운다. 칸 이름 = 출하 DDL hitpan_db_clean.sql CREATE TABLE(#13).
    //   🔴 고침 1 의 새 함수를 부르지 않는다 — 189ad5f6 위에서도 빌드 · 초록(메일 · 팩스는 고침 1 이 안 바꿈).
    // ═════════════════════════════════════════════════════════════

    private const string MailDocId = "gate59f1-mail-doc";
    private const string FaxDocId = "gate59f1-fax-doc";
    private const string FaxProviderCode = "gate59f1";      // fax_settings.provider varchar(30)

    [Fact(DisplayName = "§59 고침 1 U5′ 🔴 메일 보내기(EmailService.SendDocumentAsync 실제 호출) = 4인자 판 1회 · 인쇄 문 표지 판 0회")]
    public async Task U5p_Mail_SendDocument_FourArgOnly()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        var t = NewTenant();
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO email_settings
                    (tenant_id, smtp_host, smtp_port, smtp_user, smtp_password_enc, use_ssl,
                     from_address, from_name, bcc_self, is_active)
                  VALUES (@T, '127.0.0.1', 1, '', NULL, 0, 'gate59f1-from@example.invalid', 'gate59f1', 0, 1)",
                new { T = t });

            var pdf = new RecordingPdf();
            var log = new ListLogger<EmailService>();
            var svc = new EmailService(db, new Mock<IPasswordEncryptor>(MockBehavior.Strict).Object, pdf, log);
            var resp = await svc.SendDocumentAsync(t, null, new SendDocumentEmailRequest
            {
                DocumentType = DocType,
                DocumentId = MailDocId,
                DocumentNo = "GATE59F1-M1",
                RecipientEmail = "gate59f1-to@example.invalid",
                Subject = "gate59f1 U5p mail",
                Body = "gate59f1",
                AttachPdf = true,
            });
            _out.WriteLine($"[gate59f1] U5′ 메일 — {pdf.Describe()} · 응답 성공 {resp.Success} · 오류 {resp.Error} · 기록 {log.Entries.Count}");

            Assert.True(pdf.FourArg == 1, $"메일 보내기가 4인자 판을 {pdf.FourArg}회 불렀다(기대 1) — {pdf.Describe()}");
            Assert.True(pdf.WithOverrides == 0,
                $"메일 보내기가 인쇄 문 표지 판(overrides 판)을 {pdf.WithOverrides}회 불렀다(기대 0) — {pdf.Describe()}");
            Assert.Equal((t, DocType, MailDocId), pdf.LastFourArg);
            Assert.False(resp.Success, "[환경] 127.0.0.1:1 SMTP 연결이 성공했다 — 닫힌 포트 전제가 깨졌다(바깥 연결 0 확인 불가).");
            var attached = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM email_send_history WHERE tenant_id = @T AND has_attachment = 1", new { T = t });
            Assert.True(attached == 1, $"메일 이력에 첨부 1 행이 {attached}행 — 4인자 판 렌더 결과가 첨부로 안 갔다.");
        }
        finally
        {
            await CleanupAsync(db, t,
                "DELETE FROM email_attachment_history WHERE tenant_id = @T",
                "DELETE FROM email_send_history WHERE tenant_id = @T",
                "DELETE FROM email_settings WHERE tenant_id = @T");
        }
    }

    [Fact(DisplayName = "§59 고침 1 U5′ 🔴 팩스 보내기(FaxService.SendDocumentAsync 실제 호출) = 4인자 판 1회 · 인쇄 문 표지 판 0회")]
    public async Task U5p_Fax_SendDocument_FourArgOnly()
    {
        await using var db = await OpenAsync();
        if (db is null) return;
        var t = NewTenant();
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO fax_settings
                    (tenant_id, provider, api_endpoint, api_key_enc, api_secret_enc,
                     sender_fax_no, sender_name, is_active)
                  VALUES (@T, @P, NULL, NULL, NULL, '0200000000', 'gate59f1', 1)",
                new { T = t, P = FaxProviderCode });

            var pdf = new RecordingPdf();
            var provider = new RecordingFaxProvider();
            var log = new ListLogger<FaxService>();
            var svc = new FaxService(db, new Mock<IPasswordEncryptor>(MockBehavior.Strict).Object, pdf,
                new IFaxProvider[] { provider }, log);
            var resp = await svc.SendDocumentAsync(t, null, new SendFaxRequest
            {
                DocumentType = DocType,
                DocumentId = FaxDocId,
                DocumentNo = "GATE59F1-F1",
                RecipientFaxNo = "0200000001",
                RecipientName = "gate59f1",
            });
            _out.WriteLine($"[gate59f1] U5′ 팩스 — {pdf.Describe()} · 송출 {provider.Sent.Count} · 응답 성공 {resp.Success} · 오류 {resp.Error} · 기록 {log.Entries.Count}");

            Assert.True(pdf.FourArg == 1, $"팩스 보내기가 4인자 판을 {pdf.FourArg}회 불렀다(기대 1) — {pdf.Describe()}");
            Assert.True(pdf.WithOverrides == 0,
                $"팩스 보내기가 인쇄 문 표지 판(overrides 판)을 {pdf.WithOverrides}회 불렀다(기대 0) — {pdf.Describe()}");
            Assert.Equal((t, DocType, FaxDocId), pdf.LastFourArg);
            Assert.True(provider.Sent.Count == 1, $"가짜 공급자 송출 {provider.Sent.Count}회(기대 1) — 설정된 공급자로 안 갔다.");
            Assert.Equal(RecordingPdf.Bytes, provider.Sent[0].DocumentBytes);
        }
        finally
        {
            await CleanupAsync(db, t,
                "DELETE FROM fax_send_history WHERE tenant_id = @T",
                "DELETE FROM fax_settings WHERE tenant_id = @T");
        }
    }

    /// <summary>U5′ 정리 — 그 회사 행만 지운다. 실패는 출력 1줄(시험 판정과 무관 · #15).</summary>
    private async Task CleanupAsync(DbConnection db, string tenant, params string[] deletes)
    {
        foreach (var sql in deletes)
        {
            try
            {
                await db.ExecuteAsync(sql, new { T = tenant });
            }
            catch (MySqlException ex)
            {
                _out.WriteLine($"[gate59f1] U5′ 시험 행 정리 실패(시험 판정과 무관): {ex.Message}");
            }
            catch (Exception ex)
            {
                // 🔴 보탬 2([3-V] P3-1) — 연결 끊긴 뒤 InvalidOperationException 등도 잡는다. finally 에서 던지면 앞의 원래 단언 실패가
                //   사라진다 → 정리는 밖으로 안 던지고 경고 1줄만(#15).
                _out.WriteLine($"[gate59f1] 경고 — U5′ 시험 행 정리 실패 {ex.GetType().Name}(시험 판정과 무관 · 원래 결과 유지): {ex.Message}");
            }
        }
    }

    /// <summary>기록하는 가짜 렌더 — 4인자 판과 overrides 판(인쇄 문 표지 판)을 따로 센다. 렌더는 하지 않는다.</summary>
    private sealed class RecordingPdf : IPdfRenderService
    {
        public static readonly byte[] Bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 gate59f1 U5p");

        public int FourArg { get; private set; }
        public int WithOverrides { get; private set; }
        public int PrintDoorFlag { get; private set; }
        public int NoticeAsked { get; private set; }
        public (string Tenant, string Type, string Id) LastFourArg { get; private set; }

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, CancellationToken ct = default)
        {
            FourArg++;
            LastFourArg = (tenantId, documentType, documentId);
            return Task.FromResult((Bytes, "gate59f1.pdf"));
        }

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
        {
            WithOverrides++;
            if (overrides?.NoCoordsImageOnly == true) PrintDoorFlag++;
            return Task.FromResult((Bytes, "gate59f1.pdf"));
        }

        public Task<bool> IsNoCoordsImageOnlyAsync(string tenantId, string documentType, CancellationToken ct = default)
        {
            NoticeAsked++;
            return Task.FromResult(false);
        }

        public string Describe() =>
            $"4인자 판 {FourArg} · overrides 판 {WithOverrides}(표지 {PrintDoorFlag}) · 안내 판정 {NoticeAsked}";
    }

    /// <summary>가짜 팩스 공급자 — 받은 요청만 적는다(실제 송출 0).</summary>
    private sealed class RecordingFaxProvider : IFaxProvider
    {
        public List<FaxProviderRequest> Sent { get; } = new();
        public string ProviderCode => FaxProviderCode;
        public bool CanSendReal => true;

        public Task<FaxProviderResult> SendAsync(FaxProviderRequest req, CancellationToken ct = default)
        {
            Sent.Add(req);
            return Task.FromResult(new FaxProviderResult { Success = true, JobId = "gate59f1-job", PageCount = 1 });
        }

        public Task<FaxProviderResult> TestAsync(FaxProviderRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("U5′ 는 연결 시험 길을 태우지 않는다.");
    }

    // ═════════════════════════════════════════════════════════════
    // 통로 — 연결을 제품 서비스에 넘겨준다(판정하지 않는다)
    // ═════════════════════════════════════════════════════════════

    private sealed class GateUnitOfWork : IUnitOfWork
    {
        private readonly DbConnection _conn;
        public GateUnitOfWork(DbConnection conn) => _conn = conn;

        public DbConnection GetDbConnection() => _conn;

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

        public IRepository<T> Repository<T>() where T : BaseEntity =>
            throw new NotSupportedException("게이트는 EF 리포지토리를 태우지 않는다(양식 옮기기 경로 아님).");

        public async Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            new GateTransaction(await _conn.BeginTransactionAsync(ct));

        public void Dispose() { }
    }

    private sealed class GateTransaction : ISharedTransaction
    {
        private readonly DbTransaction _tx;
        public GateTransaction(DbTransaction tx) => _tx = tx;
        public DbTransaction DbTransaction => _tx;
        public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
        public void Dispose() => _tx.Dispose();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " | " + exception.Message)));
    }

    public void Dispose()
    {
        // 종전(보탬 2 전): var cs = ConnString();
        // 종전(보탬 2 전): if (cs is not null && (_tenants.Count > 0 || _seededApplyStatus))
        // 🔴 보탬 2 — ConnString 은 ⑴ · ⑵ 어긋남에 「[환경]」 으로 던진다 → 시험 행이 있을 때만 · try 안에서 부른다.
        if (_tenants.Count > 0 || _seededApplyStatus)
        {
            try
            {
                var cs = ConnString() ?? throw new InvalidOperationException("시험 행이 있는데 HITPAN_TEST_DB 가 비었다(도중에 바뀜).");
                using var c = new MySqlConnection(cs);
                c.Open();
                foreach (var t in _tenants)
                {
                    c.Execute("DELETE FROM form_templates WHERE tenant_id = @T", new { T = t });
                }
                if (_seededApplyStatus)
                {
                    c.Execute("DELETE FROM local_update_apply_status WHERE applied_version = @V AND detail = @D",
                        new { V = ApiVersion, D = SeedDetail });
                }
            }
            catch (MySqlException ex)
            {
                _out.WriteLine($"[gate59f1] 시험 행 정리 실패(시험 판정과 무관): {ex.Message}");
            }
            catch (Exception ex)
            {
                // 🔴 보탬 2([3-V] P3-1) — 정리 실패가 원래 단언 실패를 덮지 않게 모든 예외를 잡고 경고 1줄(#15).
                _out.WriteLine($"[gate59f1] 경고 — 시험 행 정리 실패 {ex.GetType().Name}(시험 판정과 무관 · 원래 결과 유지): {ex.Message}");
            }
        }
        foreach (var t in _tenants)
        {
            var dir = FormBackgroundPaths.TenantRoot(t);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException ex)
            {
                _out.WriteLine($"[gate59f1] 시험 그림 폴더 정리 실패(시험 판정과 무관): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _out.WriteLine($"[gate59f1] 시험 그림 폴더 정리 거부(시험 판정과 무관): {ex.Message}");
            }
            catch (Exception ex)
            {
                // 🔴 보탬 2([3-V] P3-1) — 그 밖 예외도 잡고 경고 1줄(#15) · 원래 결과 유지.
                _out.WriteLine($"[gate59f1] 경고 — 시험 그림 폴더 정리 실패 {ex.GetType().Name}(시험 판정과 무관 · 원래 결과 유지): {ex.Message}");
            }
        }
    }
}
