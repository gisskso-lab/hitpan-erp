using System.Data;
using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;
using A = HitPan.Application.Services.MdbLegacyUnpostedArchive;
using M = HitPan.Application.Services.LegacyMdbMapping;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbPostingAllPathsGate (G2)</b> — 20260915작1 갈래 B · DOCFB 줄이 명세서·보관 표·재고원장 세 경로에서 <b>한 줄도 안 빠진다</b>.
/// </summary>
/// <remarks>
/// <para>
/// 진실원: 작업지시서 <c>docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md</c> §11-3 G2 · §14-1 · §14-3 ·
/// 개발명세서 <c>docs/개발/erp/20260915작1_갈래B_이관연결_개발명세서.md</c>.
/// </para>
/// <para>
/// 이 게이트는 서비스가 실제로 부르는 함수(<see cref="MdbLegacyUnpostedArchive"/> Partition · BuildArchiveRows · LegacyTaxNo · LedgerLine · WriteAsync)에
/// <b>합성 DOCFB DataTable</b> 을 넣고 결과를 본다. G2g 만 DB(보관 표 멱등 적재) — <c>HITPAN_REQUIRE_DB</c> 에서 SKIP = FAIL.
/// </para>
/// <para>
/// ⚠️ 한계: <c>MdbMigrationService</c> 가 이 함수들을 그 자리(명세서 루프·원장 루프)에서 부르는지는 OleDb(MDB) 없이 못 부른다 —
/// MDB 사본 재이관 실측([4])이 최종 확인이다.
/// </para>
/// </remarks>
public sealed class MdbPostingAllPathsGateTests
{
    private static readonly DateTime BaseDate = new(2026, 2, 28);

    private static DataTable NewDocfb()
    {
        var t = new DataTable("DOCFB");
        t.Columns.Add("IJ_DT", typeof(string));
        t.Columns.Add("IJ_IO", typeof(string));
        t.Columns.Add("IJ_SEQ", typeof(int));
        t.Columns.Add("IJ_BUY", typeof(int));
        t.Columns.Add("IJ_SUN", typeof(int));
        t.Columns.Add("IJ_PUM", typeof(string));
        t.Columns.Add("IJ_KU", typeof(string));
        t.Columns.Add("IJ_QTY", typeof(decimal));
        t.Columns.Add("IJ_DAN", typeof(decimal));
        t.Columns.Add("IJ_AMT", typeof(decimal));
        t.Columns.Add("IJ_VAT", typeof(decimal));
        t.Columns.Add("IJ_REM", typeof(string));
        t.Columns.Add("IJ_TAXNO", typeof(string));
        return t;
    }

    private static void Add(DataTable t, string dt, string io, int seq, int buy, int sun,
        string pum = "품목A", decimal qty = 1m, decimal amt = 1000m, decimal vat = 100m, string taxNo = "00000000", string rem = "")
        => t.Rows.Add(dt, io, seq, buy, sun, pum, "", qty, amt / (qty == 0 ? 1 : qty), amt, vat, rem, taxNo);

    /// <summary>
    /// 합성 MDB: 판매 머리O(2줄) · 매입 연결O(1줄) · 판매 둘 다 X(같은 내용 2줄) · 날짜 없음 00000000(머리O지만 날짜 없음 · 1줄) ·
    /// 매입 둘 다 X(1줄) · 판매 연결은 수금 줄만(미반영 · 1줄). 입력 8줄 · 명세서 3줄 · 보관 5줄.
    /// </summary>
    private static (DataTable Docfb, HashSet<M.LegacyDocKey> Headers, HashSet<M.LegacyLedgerLinkKey>? Sales, HashSet<M.LegacyLedgerLinkKey>? Purchase) Sample()
    {
        var t = NewDocfb();
        Add(t, "20260105", "2", 11, 100, 1);                    // 판매 머리 O
        Add(t, "20260105", "2", 11, 100, 2, pum: "품목B");
        Add(t, "20260106", "1", 12, 200, 1);                    // 매입 연결 O (S_GU A)
        Add(t, "20260107", "2", 13, 300, 1, amt: 500m, vat: 50m); // 판매 둘 다 X · 같은 내용 두 줄
        Add(t, "20260107", "2", 13, 300, 1, amt: 500m, vat: 50m);
        Add(t, "00000000", "2", 0, 0, 1, pum: "");                // 날짜 없음 (머리표에 있어도 보관 표)
        Add(t, "20260108", "1", 14, 400, 1);                    // 매입 둘 다 X
        Add(t, "20260109", "2", 15, 500, 1);                    // 판매 — 같은 키에 수금 줄(S_GU 2)만 → 미반영

        var headers = new HashSet<M.LegacyDocKey>
        {
            M.LegacyDocKey.Of("20260105", "2", 11, 100),
            M.LegacyDocKey.Of("00000000", "2", 0, 0),
        };
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[]
        {
            new M.LegacyPartnerLedgerRow(200, "20260106", "A", 0, 1100, 12),
            new M.LegacyPartnerLedgerRow(500, "20260109", "2", 0, 1100, 15),
        });
        return (t, headers, sales, purchase);
    }

    // ─────────────── 명세서 줄 + 보관 줄 = 입력 줄 ───────────────

    /// <summary>
    /// G2a 명세서 줄 + 보관 줄 = 입력 줄 · 판정 결과별 갈래가 맞다.
    /// <para>무력화: Partition 이 Unposted 를 명세서로 보내면(판정 무시) 보관 4줄 → 0 으로 빨간불 · 날짜 없는 묶음을 버리면 합계가 입력과 달라 빨간불.</para>
    /// </summary>
    [Fact]
    public void G2a_명세서줄_더하기_보관줄은_입력줄()
    {
        var (t, h, s, p) = Sample();
        var part = A.Partition(t, h, s, p);

        Assert.Equal(8, part.InputLines);
        Assert.Equal(part.InputLines, part.StatementLines + part.ArchivedLines + part.UnknownIoLines);
        Assert.Equal(3, part.StatementLines);
        Assert.Equal(5, part.ArchivedLines);
        Assert.Equal(0, part.UnknownIoLines);
        Assert.Equal(new[] { 11, 12 }, part.Statements.Select(g => g.Key.Seq).ToArray());
        Assert.Equal(new[] { 13, 0, 14, 15 }, part.Archived.Select(g => g.Key.Seq).ToArray());
        Assert.Equal(1, part.ArchivedUndatedGroups);
        Assert.Equal(3, part.ArchivedUnpostedGroups);
        Assert.False(part.Unclassified);
    }

    /// <summary>G2b DOCFE 없음(R5) → 날짜 있는 묶음은 전부 명세서 · 날짜 없는 묶음만 보관 표 · 합계 = 입력.</summary>
    [Fact]
    public void G2b_머리표없음_전부반영_날짜없음만_보관()
    {
        var (t, _, s, p) = Sample();
        var part = A.Partition(t, null, s, p);

        Assert.True(part.Unclassified);
        Assert.Equal(7, part.StatementLines);
        Assert.Equal(1, part.ArchivedLines);
        Assert.True(part.Archived.Single().Undated);
        Assert.Equal(part.InputLines, part.StatementLines + part.ArchivedLines + part.UnknownIoLines);
    }

    // ─────────────── 원장 = 입력 전 줄 ───────────────

    /// <summary>
    /// G2c 재고원장은 판정과 무관하게 입력 전 줄 · 날짜 없는 줄은 기준일(이관일 아님).
    /// <para>무력화: LedgerLine 날짜 폴백을 <c>DateTime.UtcNow</c> 로 되돌리면 빨간불 · 보관 표 줄을 원장에서 빼면 개수가 달라 빨간불.</para>
    /// </summary>
    [Fact]
    public void G2c_원장은_입력전줄_날짜없음은_기준일()
    {
        var (t, h, s, p) = Sample();
        var part = A.Partition(t, h, s, p);

        var ledger = t.Rows.Cast<DataRow>()
            .Select(r => (Row: r, Line: A.LedgerLine((string)r["IJ_IO"], (string)r["IJ_DT"], BaseDate)))
            .Where(x => x.Line.Include)
            .ToList();
        Assert.Equal(t.Rows.Count, ledger.Count);

        // 보관 표로 간 줄도 원장엔 전부 있다
        var archivedRows = part.Archived.SelectMany(g => g.Rows).ToHashSet();
        Assert.Equal(archivedRows.Count, ledger.Count(x => archivedRows.Contains(x.Row)));

        var undated = ledger.Single(x => (string)x.Row["IJ_DT"] == "00000000");
        Assert.Equal(BaseDate, undated.Line.LedgerDate);
        Assert.Equal(new DateTime(2026, 1, 5), ledger.First().Line.LedgerDate);
        Assert.Equal(BaseDate, A.LedgerLine("2", "00000001", BaseDate).LedgerDate);
        Assert.False(A.LedgerLine("3", "20260101", BaseDate).Include);
    }

    // ─────────────── 보관 표 줄 ───────────────

    /// <summary>G2d 날짜 없는 묶음 = 보관 표 날짜 기준일 · legacy_dt 원문 · 사유 이름표(갈래 A).</summary>
    [Fact]
    public void G2d_날짜없는줄_보관표_기준일()
    {
        var (t, h, s, p) = Sample();
        var part = A.Partition(t, h, s, p);
        var docs = A.BuildArchiveRows("tenant-g2", part.Archived, BaseDate, new Dictionary<int, string> { [300] = "partner-300" }, (_, _) => "item-x");

        var undated = docs.Single(d => d.LegacyDt == "00000000");
        Assert.Equal(BaseDate, undated.DocDate);
        Assert.Equal("danga", undated.Reason);
        Assert.Null(undated.Lines.Single().ItemId);            // 품명 없는 줄 = NULL (DDL 주석)
        Assert.Equal("sales", undated.IoType);

        var dated = docs.Single(d => d.LegacySeq == 13);
        Assert.Equal(new DateTime(2026, 1, 7), dated.DocDate);
        Assert.Equal("partner-300", dated.PartnerId);
        Assert.Null(docs.Single(d => d.LegacySeq == 14).PartnerId);   // 못 찾으면 NULL (폴백 거래처 안 씀)
        Assert.Equal("purchase", docs.Single(d => d.LegacySeq == 14).IoType);
        Assert.Equal(docs.Sum(d => d.LineCount), docs.Sum(d => d.Lines.Count));
        Assert.Equal(part.ArchivedLines, docs.Sum(d => d.Lines.Count));
        Assert.Equal(1000m, dated.SupplyAmount);
        Assert.Equal(100m, dated.VatAmount);
    }

    /// <summary>
    /// G2e 같은 내용 두 줄 → 보관 줄 2개(해시·id 다름) · 다시 만들어도 같은 해시(멱등) · 내용 다른 줄은 순서가 바뀌어도 같은 해시.
    /// <para>무력화: 줄 해시에서 「같은 내용 순번」을 빼면 두 줄 해시가 같아 빨간불(UNIQUE 에 한 줄 IGNORE — 갈래 F 조건).</para>
    /// </summary>
    [Fact]
    public void G2e_같은내용_두줄은_보관2줄_멱등()
    {
        var (t, h, s, p) = Sample();
        var docs1 = A.BuildArchiveRows("tenant-g2", A.Partition(t, h, s, p).Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null);
        var docs2 = A.BuildArchiveRows("tenant-g2", A.Partition(t, h, s, p).Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null);

        var twin = docs1.Single(d => d.LegacySeq == 13);
        Assert.Equal(2, twin.Lines.Count);
        Assert.NotEqual(twin.Lines[0].MigratedSourceHash, twin.Lines[1].MigratedSourceHash);
        Assert.NotEqual(twin.Lines[0].LineId, twin.Lines[1].LineId);
        Assert.Equal(docs1.SelectMany(d => d.Lines).Count(), docs1.SelectMany(d => d.Lines).Select(l => l.MigratedSourceHash).Distinct().Count());

        Assert.Equal(docs1.SelectMany(d => d.Lines).Select(l => l.MigratedSourceHash), docs2.SelectMany(d => d.Lines).Select(l => l.MigratedSourceHash));
        Assert.Equal(docs1.Select(d => d.DocId), docs2.Select(d => d.DocId));
        Assert.All(twin.Lines, l => Assert.Equal(twin.DocId, l.DocId));
        Assert.All(twin.Lines, l => Assert.Equal(64, l.MigratedSourceHash.Length));
        Assert.All(docs1, d => Assert.Equal(36, d.DocId.Length));

        // 같은 묶음 안 내용 다른 두 줄: 입력 순서를 뒤집어도 해시 집합이 같다
        var a = NewDocfb();
        Add(a, "20260110", "2", 16, 600, 1, pum: "X");
        Add(a, "20260110", "2", 16, 600, 1, pum: "Y");
        var b = NewDocfb();
        Add(b, "20260110", "2", 16, 600, 1, pum: "Y");
        Add(b, "20260110", "2", 16, 600, 1, pum: "X");
        var ha = A.BuildArchiveRows("t", A.Partition(a, h, null, null).Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null)
            .SelectMany(d => d.Lines).Select(l => l.MigratedSourceHash).OrderBy(x => x).ToArray();
        var hb = A.BuildArchiveRows("t", A.Partition(b, h, null, null).Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null)
            .SelectMany(d => d.Lines).Select(l => l.MigratedSourceHash).OrderBy(x => x).ToArray();
        Assert.Equal(2, ha.Length);
        Assert.Equal(ha, hb);
    }

    // ─────────────── legacy_tax_no ───────────────

    /// <summary>
    /// G2f 혼재 묶음 = 첫 비0 번호 · 99999999 원값 · 전부 0/빈값 = 0.
    /// <para>무력화: 첫 줄만 보면(종전) 혼재 묶음이 0 이라 빨간불 · 99999999 → NULL/0 변환(종전)이면 빨간불.</para>
    /// </summary>
    [Fact]
    public void G2f_혼재묶음_계산서번호_비0_99999999_원값()
    {
        var t = NewDocfb();
        Add(t, "20260111", "2", 17, 700, 1, taxNo: "00000000");
        Add(t, "20260111", "2", 17, 700, 2, taxNo: "12345678");
        Add(t, "20260111", "2", 17, 700, 3, taxNo: "87654321");
        Assert.Equal(12345678, A.LegacyTaxNo(t.Rows.Cast<DataRow>()));

        var n = NewDocfb();
        Add(n, "20260112", "2", 18, 700, 1, taxNo: "00000000");
        Add(n, "20260112", "2", 18, 700, 2, taxNo: "99999999");
        Assert.Equal(99999999, A.LegacyTaxNo(n.Rows.Cast<DataRow>()));

        var z = NewDocfb();
        Add(z, "20260113", "2", 19, 700, 1, taxNo: "00000000");
        Add(z, "20260113", "2", 19, 700, 2, taxNo: "");
        Assert.Equal(0, A.LegacyTaxNo(z.Rows.Cast<DataRow>()));

        // 서비스 루프가 넘기는 모양(IGrouping) 그대로
        var g = A.Partition(t, null, null, null).Statements.Single();
        Assert.Equal(12345678, A.LegacyTaxNo(g));
    }

    // ─────────────── 보관 표 적재 (DB) ───────────────

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};DefaultCommandTimeout=90;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다(작14 W1)
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

    /// <summary>
    /// G2g 보관 표 실제 적재(DB-123 표 정의 그대로) — 같은 내용 두 줄 = 2행 · 재적재 = 0행 추가(멱등) · 줄 수 = 보관 줄.
    /// <para>무력화: 해시에서 같은 내용 순번을 빼면 UNIQUE 에 한 줄 IGNORE 돼 4행으로 빨간불.</para>
    /// </summary>
    [Fact]
    public async Task G2g_보관표_적재_같은내용두줄_재적재멱등()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G2g_보관표_적재_같은내용두줄_재적재멱등)); return; }

        var dbName = "hitpan_g2_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = new MySqlConnection(ServerConnString()))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        try
        {
            // DB-123 파일의 보관 표 두 개(1)·2) 절)를 그대로 실행 — 표 정의를 게이트에 베껴 두지 않는다.
            var sql = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-123_legacy_unposted_documents.sql"));
            var cut = sql.IndexOf("-- ── 3)", StringComparison.Ordinal);
            Assert.True(cut > 0, "DB-123 파일에서 3) 절 표시를 못 찾았다 — 표 정의 위치가 바뀌었다.");

            await using var db = new MySqlConnection(ServerConnString().Replace("User=", $"Database={dbName};User="));
            await db.OpenAsync();
            await db.ExecuteAsync(sql[..cut]);

            var (t, h, s, p) = Sample();
            var part = A.Partition(t, h, s, p);
            var docs = A.BuildArchiveRows("tenant-g2", part.Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null);

            await using (var tx = await db.BeginTransactionAsync())
            {
                var (d1, l1) = await A.WriteAsync(db, tx, docs, CancellationToken.None);
                await tx.CommitAsync();
                Assert.Equal(4, d1);
                Assert.Equal(5, l1);
            }

            var again = A.BuildArchiveRows("tenant-g2", A.Partition(t, h, s, p).Archived, BaseDate, new Dictionary<int, string>(), (_, _) => null);
            var (d2, l2) = await A.WriteAsync(db, null, again, CancellationToken.None);
            Assert.Equal(0, d2);
            Assert.Equal(0, l2);

            Assert.Equal(part.ArchivedLines, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM legacy_unposted_document_lines WHERE tenant_id = 'tenant-g2'"));
            Assert.Equal(2, await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM legacy_unposted_document_lines l JOIN legacy_unposted_documents d ON d.doc_id = l.doc_id AND d.tenant_id = l.tenant_id WHERE d.legacy_seq = 13"));
            Assert.Equal(BaseDate, await db.ExecuteScalarAsync<DateTime>("SELECT doc_date FROM legacy_unposted_documents WHERE legacy_dt = '00000000'"));
            Assert.Equal(0, await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM legacy_unposted_document_lines l LEFT JOIN legacy_unposted_documents d ON d.doc_id = l.doc_id WHERE d.doc_id IS NULL"));
        }
        finally
        {
            await using var admin = new MySqlConnection(ServerConnString());
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{dbName}`;");
        }
    }
}
