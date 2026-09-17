using System.Collections;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using M = HitPan.Application.Services.LegacyMdbMapping;

namespace HitPan.Application.Services;

/// <summary>
/// 🆕 <b>20260915작1 갈래 B — DOCFB 줄을 「장부 반영 명세서」와 「보관 표」로 나누고, 보관 표에 적재한다.</b>
///
/// <para>
/// 진실원: 작업지시서 <c>docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md</c> §11-1·§11-2(B)·§11-3(G2)·§14-1·§14-3 ·
/// 설계 §14(판정·3경로)·§15(DDL) · 개발명세서 <c>docs/개발/erp/20260915작1_갈래B_이관연결_개발명세서.md</c>.
/// </para>
///
/// <para>
/// 🔴 세 경로 규칙 (뺀 줄 0)
/// <list type="bullet">
///   <item><b>명세서 경로</b> — 판정(<see cref="M.ClassifyPosting"/>) Posted/Unclassified 이고 날짜가 있는 묶음만 sales_deliveries/purchase_receipts.</item>
///   <item><b>보관 표</b> — 판정 Unposted 묶음 <b>전 줄</b> + 날짜 없는 묶음(IJ_DT 빈값/00000000 · 명세서 경로의 종전 skip) <b>전 줄</b>. 날짜 = 기준일.</item>
///   <item><b>재고원장</b> — 판정과 <b>무관하게</b> DOCFB 전 줄(R1·R3). 날짜 없는 줄 = 기준일(<see cref="LedgerLine"/>).</item>
/// </list>
/// IJ_IO 가 1·2 가 아닌 묶음(실측 0)은 종전대로 명세서에서 빼고 개수만 센다 — 보관 표 io_type 이 sales/purchase 뿐이라서다.
/// </para>
///
/// <para>
/// ⚠️ 판정은 갈래 A(<see cref="LegacyMdbMapping"/>) 함수만 쓴다. 이 클래스는 <b>나누기·줄 만들기·적재</b>만 한다.
/// 나누기·줄 만들기는 순수 함수(게이트 G2 가 합성 DataTable 로 부른다) · 적재(<see cref="WriteAsync"/>)만 DB 를 쓴다.
/// </para>
/// </summary>
public static class MdbLegacyUnpostedArchive
{
    /// <summary>보관 표 source_type — 이관만 넣는다.</summary>
    public const string SourceType = "migration";

    /// <summary>DOCFB 묶음 키 — 서비스 종전 GroupBy(<c>MdbMigrationService.MigrateDeliveriesAndReceiptsAsync</c>)와 같은 모양(Dt 원문 · Io 정수).</summary>
    public readonly record struct DocfbGroupKey(string Dt, int Io, int Seq, int Buy);

    /// <summary>DOCFB 묶음 하나 (입력 순서 보존). 서비스 루프가 <c>g.Key.*</c> · <c>foreach (var r in g)</c> 를 그대로 쓰도록 IGrouping 을 구현한다.</summary>
    public sealed class DocfbGroup : IGrouping<DocfbGroupKey, DataRow>
    {
        internal DocfbGroup(DocfbGroupKey key) => Key = key;

        /// <inheritdoc />
        public DocfbGroupKey Key { get; }

        /// <summary>묶음 줄 (입력 순서).</summary>
        public List<DataRow> Rows { get; } = new();

        /// <summary>판정 결과 (날짜 없는 묶음도 판정값은 남긴다 — 보관 사유 집계용).</summary>
        public M.LegacyPostingStatus Status { get; internal set; }

        /// <summary>IJ_DT 빈값/00000000 — 명세서 경로 skip 대상(<c>:4948</c>) → 보관 표.</summary>
        public bool Undated { get; internal set; }

        /// <inheritdoc />
        public IEnumerator<DataRow> GetEnumerator() => Rows.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>DOCFB 나누기 결과. 🔴 불변식: <c>StatementLines + ArchivedLines + UnknownIoLines == InputLines</c>.</summary>
    public sealed class DocfbPartition
    {
        /// <summary>명세서 경로로 가는 묶음.</summary>
        public List<DocfbGroup> Statements { get; } = new();

        /// <summary>보관 표로 가는 묶음 (Unposted + 날짜 없음).</summary>
        public List<DocfbGroup> Archived { get; } = new();

        /// <summary>IJ_IO 1·2 밖 묶음 (실측 0 · 명세서·보관 표 어디에도 안 간다 — 재고원장도 종전대로 제외).</summary>
        public List<DocfbGroup> UnknownIo { get; } = new();

        /// <summary>입력 줄 수.</summary>
        public int InputLines { get; internal set; }

        /// <summary>DOCFE 없음/0행 → 분류 못 함(R5 · 전부 반영).</summary>
        public bool Unclassified { get; internal set; }

        /// <summary>명세서 줄 수.</summary>
        public int StatementLines => Statements.Sum(g => g.Rows.Count);

        /// <summary>보관 줄 수.</summary>
        public int ArchivedLines => Archived.Sum(g => g.Rows.Count);

        /// <summary>IJ_IO 미지값 줄 수.</summary>
        public int UnknownIoLines => UnknownIo.Sum(g => g.Rows.Count);

        /// <summary>보관 묶음 중 날짜 없음으로 간 묶음 수.</summary>
        public int ArchivedUndatedGroups => Archived.Count(g => g.Undated);

        /// <summary>보관 묶음 중 판정 Unposted 로 간 묶음 수(날짜 있음).</summary>
        public int ArchivedUnpostedGroups => Archived.Count(g => !g.Undated);
    }

    /// <summary>보관 표 머리 1행 (<c>legacy_unposted_documents</c> · DESCRIBE 2026-09-15 격리 DB f_new).</summary>
    public sealed class ArchiveDocRow
    {
        public string DocId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string IoType { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string? LegacyDt { get; set; }
        public int LegacySeq { get; set; }
        public long LegacyBuyCode { get; set; }
        public string? PartnerId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal SupplyAmount { get; set; }
        public decimal VatAmount { get; set; }
        public int LineCount { get; set; }
        public string? Memo { get; set; }
        public string SourceId { get; set; } = string.Empty;
        public string MigratedSourceHash { get; set; } = string.Empty;
        public List<ArchiveLineRow> Lines { get; } = new();
    }

    /// <summary>보관 표 줄 1행 (<c>legacy_unposted_document_lines</c>).</summary>
    public sealed class ArchiveLineRow
    {
        public string LineId { get; set; } = string.Empty;
        public string DocId { get; set; } = string.Empty;
        public int LineNo { get; set; }
        public int LegacySun { get; set; }
        public string? ItemId { get; set; }
        public string? ItemName { get; set; }
        public string? Spec { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal SupplyAmount { get; set; }
        public decimal VatAmount { get; set; }
        public string? Memo { get; set; }
        public string? StockSourceId { get; set; }
        public string MigratedSourceHash { get; set; } = string.Empty;
    }

    // ════════════════════════════════════════════════════════════════
    // 나누기 (순수)
    // ════════════════════════════════════════════════════════════════

    /// <summary>명세서 경로 종전 skip 조건(<c>MdbMigrationService.cs:4948</c>) 그대로 — IJ_DT 빈값 또는 <c>00000000</c>.</summary>
    public static bool IsUndated(string? ijDt) => string.IsNullOrWhiteSpace(ijDt) || ijDt == "00000000";

    /// <summary>
    /// DOCFB 표를 묶음으로 나눠 세 갈래에 담는다. 묶음 키·순서는 서비스 종전 GroupBy 와 같다.
    /// 판정 = <see cref="M.ClassifyPosting"/> (IO 는 "1"/"2" 글자로 넘긴다).
    /// </summary>
    public static DocfbPartition Partition(
        DataTable docfb,
        IReadOnlySet<M.LegacyDocKey>? headerKeys,
        IReadOnlySet<M.LegacyLedgerLinkKey>? salesLinks,
        IReadOnlySet<M.LegacyLedgerLinkKey>? purchaseLinks)
    {
        ArgumentNullException.ThrowIfNull(docfb);
        var p = new DocfbPartition
        {
            InputLines = docfb.Rows.Count,
            Unclassified = headerKeys is not { Count: > 0 },
        };

        var order = new List<DocfbGroup>();
        var byKey = new Dictionary<DocfbGroupKey, DocfbGroup>();
        foreach (DataRow r in docfb.Rows)
        {
            var key = new DocfbGroupKey(Str(r, "IJ_DT"), Int(r, "IJ_IO"), Int(r, "IJ_SEQ"), Int(r, "IJ_BUY"));
            if (!byKey.TryGetValue(key, out var g))
            {
                g = new DocfbGroup(key);
                byKey[key] = g;
                order.Add(g);
            }
            g.Rows.Add(r);
        }

        foreach (var g in order)
        {
            if (g.Key.Io != 1 && g.Key.Io != 2)
            {
                p.UnknownIo.Add(g);
                continue;
            }
            var docKey = M.LegacyDocKey.Of(g.Key.Dt, g.Key.Io.ToString(CultureInfo.InvariantCulture), g.Key.Seq, g.Key.Buy);
            g.Status = M.ClassifyPosting(docKey, headerKeys, salesLinks, purchaseLinks);
            g.Undated = IsUndated(g.Key.Dt);
            if (g.Undated || g.Status == M.LegacyPostingStatus.Unposted)
                p.Archived.Add(g);
            else
                p.Statements.Add(g);
        }
        return p;
    }

    /// <summary>
    /// 명세서 <c>legacy_tax_no</c> — 묶음 안 <b>한 줄이라도</b> IJ_TAXNO 가 0('00000000')·빈값·99999999 가 아니면 그 번호(첫 실번호).
    /// 🔴 실번호가 없고 <c>99999999</c> 가 있으면 <b>원값 그대로</b> 돌려준다(R-B4 · 판정은 <c>MigratedDocumentLock</c> 몫).
    /// 전부 0/빈값이면 0 (종전 첫 줄 0 저장과 같다 · G 판정에서 0 = 미발행).
    /// </summary>
    /// <remarks>
    /// 🆕 20260915작1 3판 R3 (설계 §24 · §14-9 ③): 묶음 안 <b>실번호(0·99999999 아님) 먼저</b> → 없으면 99999999 → 없으면 0.
    /// 종전(첫 비0)은 99999999 줄이 실번호 줄보다 앞에 있으면 실제 발행된 명세서를 「발행 안 함」으로 저장했다.
    /// </remarks>
    public static int LegacyTaxNo(IEnumerable<DataRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var notIssued = false;
        foreach (var r in rows)
        {
            var no = TaxNo(r);
            if (no == LegacyTaxNoNotIssuedValue)
            {
                notIssued = true;
                continue;
            }
            if (no != 0) return no;
        }
        return notIssued ? LegacyTaxNoNotIssuedValue : 0;
    }

    /// <summary>레거시 IJ_TAXNO 「발행 안 함」 값 — <see cref="MigratedDocumentLock.LegacyTaxNoNotIssued"/> 와 같은 값.</summary>
    private const int LegacyTaxNoNotIssuedValue = MigratedDocumentLock.LegacyTaxNoNotIssued;

    /// <summary>
    /// 재고원장 경로 한 줄 — <b>판정과 무관</b>(R1·R3). IJ_IO 가 "1"/"2" 면 넣는다(종전 미지값 제외 규칙 그대로).
    /// 날짜 = <see cref="M.ResolveLegacyDate"/>(IJ_DT, 기준일) — 종전 <c>ParseLegacyDate ?? now</c>(<c>:2306</c>) 대체.
    /// </summary>
    public static (bool Include, DateTime LedgerDate) LedgerLine(string? ijIo, string? ijDt, DateTime baseDate)
    {
        var io = (ijIo ?? string.Empty).Trim();
        return (io is "1" or "2", M.ResolveLegacyDate(ijDt, baseDate));
    }

    // ════════════════════════════════════════════════════════════════
    // 보관 줄 만들기 (순수)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 보관 묶음 → 머리·줄 행. 날짜 = <see cref="M.ResolveLegacyDate"/> (날짜 없는 묶음 = 기준일) · 원문 IJ_DT 는 legacy_dt.
    /// 🔴 줄 멱등 해시에 <b>줄 위치</b>(IJ_SUN · 같은 내용 줄의 순번)를 넣는다(갈래 F 조건) — 내용이 같은 두 줄이 한 줄로 IGNORE 되지 않게.
    ///   「같은 내용 순번」은 입력 순서가 바뀌어도 같은 값이 나오도록 (source, IJ_SUN, 내용) 이 같은 줄끼리만 센다.
    /// </summary>
    /// <param name="tenantId">JWT 에서 온 테넌트(서비스 호출자 몫).</param>
    /// <param name="archived"><see cref="DocfbPartition.Archived"/>.</param>
    /// <param name="baseDate">기준일.</param>
    /// <param name="partnerMap">레거시 거래처 코드 → 거래처 id (못 찾으면 NULL · 폴백 거래처 안 씀).</param>
    /// <param name="itemIdOf">(품명, 규격) → 상품 id. 품명·규격이 둘 다 비면 부르지 않고 NULL.</param>
    public static List<ArchiveDocRow> BuildArchiveRows(
        string tenantId,
        IEnumerable<DocfbGroup> archived,
        DateTime baseDate,
        IReadOnlyDictionary<int, string> partnerMap,
        Func<string, string, string?> itemIdOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(archived);
        ArgumentNullException.ThrowIfNull(partnerMap);
        ArgumentNullException.ThrowIfNull(itemIdOf);

        var docs = new List<ArchiveDocRow>();
        foreach (var g in archived)
        {
            var (dt, io, seq, buy) = (g.Key.Dt, g.Key.Io, g.Key.Seq, g.Key.Buy);
            var sourceId = $"mig-docfb-{dt}-{io}-{seq}-{buy}";
            decimal supply = 0m, vat = 0m, absSupply = 0m, absVat = 0m;
            foreach (var r in g.Rows)
            {
                var a = Dec(r, "IJ_AMT");
                var v = Dec(r, "IJ_VAT");
                supply += a; vat += v; absSupply += Math.Abs(a); absVat += Math.Abs(v);
            }

            var doc = new ArchiveDocRow
            {
                DocId = StableId($"{tenantId}:legacy_unposted_doc:{sourceId}"),
                TenantId = tenantId,
                IoType = LegacyMdbMapping.DeliveryKind(io),
                DocDate = M.ResolveLegacyDate(dt, baseDate),
                LegacyDt = Cut(dt, 8),
                LegacySeq = seq,
                LegacyBuyCode = buy,
                PartnerId = partnerMap.TryGetValue(buy, out var pid) ? pid : null,
                Reason = M.UnpostedReason(dt, buy, seq, absSupply, absVat),
                SupplyAmount = supply,
                VatAmount = vat,
                LineCount = g.Rows.Count,
                Memo = NullIfBlank(Cut(g.Rows.Select(r => Str(r, "IJ_REM")).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)), 500)),
                SourceId = sourceId,
            };

            var sameContent = new Dictionary<string, int>(StringComparer.Ordinal);
            var lineNo = 0;
            foreach (var r in g.Rows)
            {
                lineNo++;
                var sun = Int(r, "IJ_SUN");
                var pum = Str(r, "IJ_PUM");
                var ku = Str(r, "IJ_KU");
                var qty = Dec(r, "IJ_QTY");
                var dan = Dec(r, "IJ_DAN");
                var amt = Dec(r, "IJ_AMT");
                var v = Dec(r, "IJ_VAT");
                var rem = Str(r, "IJ_REM");
                var content = string.Join('\u001F', sourceId, sun.ToString(CultureInfo.InvariantCulture), pum, ku,
                    qty.ToString(CultureInfo.InvariantCulture), dan.ToString(CultureInfo.InvariantCulture),
                    amt.ToString(CultureInfo.InvariantCulture), v.ToString(CultureInfo.InvariantCulture),
                    rem, TaxNo(r).ToString(CultureInfo.InvariantCulture));
                var occurrence = sameContent.TryGetValue(content, out var n) ? n + 1 : 0;
                sameContent[content] = occurrence;
                var lineHash = Sha256Hex($"legacy_unposted_line:{content}#{occurrence}");

                var ledgerId = $"mb-{dt}-{io}-{seq}-{buy}-{sun}";
                doc.Lines.Add(new ArchiveLineRow
                {
                    LineId = StableId($"{tenantId}:legacy_unposted_line:{lineHash}"),
                    DocId = doc.DocId,
                    LineNo = lineNo,
                    LegacySun = sun,
                    ItemId = string.IsNullOrWhiteSpace(pum) && string.IsNullOrWhiteSpace(ku) ? null : itemIdOf(pum, ku),
                    ItemName = NullIfBlank(Cut(pum, 200)),
                    Spec = NullIfBlank(Cut(ku, 200)),
                    Qty = qty,
                    UnitPrice = dan,
                    SupplyAmount = amt,
                    VatAmount = v,
                    Memo = NullIfBlank(Cut(rem, 500)),
                    // 재고원장 source_id(LegacyMdbMapping.LedgerSourceId 와 같은 글자) · 36자 넘으면 원장에도 없으므로 NULL.
                    StockSourceId = ledgerId.Length <= M.LedgerSourceIdMaxLength ? ledgerId : null,
                    MigratedSourceHash = lineHash,
                });
            }

            doc.MigratedSourceHash = Sha256Hex(
                $"legacy_unposted_doc:{sourceId}:{supply.ToString(CultureInfo.InvariantCulture)}:{vat.ToString(CultureInfo.InvariantCulture)}:{doc.LineCount}");
            docs.Add(doc);
        }
        return docs;
    }

    // ════════════════════════════════════════════════════════════════
    // 적재 (DB)
    // ════════════════════════════════════════════════════════════════

    private const int ChunkRows = 200;

    /// <summary>
    /// 보관 표 적재 — <c>INSERT IGNORE</c> (멱등: 머리 UNIQUE(tenant_id, source_id) · 줄 UNIQUE(tenant_id, migrated_source_hash)).
    /// doc_id·line_id 는 테넌트+키 해시로 만든 고정값이라 재이관에도 줄이 같은 머리를 가리킨다.
    /// 🔴 INSERT 만 한다 — UPDATE/DELETE 없음(보관 표 = 이관만 INSERT · 읽기 전용).
    /// 칼럼 근거: DESCRIBE legacy_unposted_documents / legacy_unposted_document_lines (2026-09-15 · 격리 인스턴스 33306 f_new · 헌법 #13).
    /// </summary>
    /// <returns>(머리 INSERT 행, 줄 INSERT 행) — IGNORE 된 행은 세지 않는다.</returns>
    public static async Task<(int Docs, int Lines)> WriteAsync(
        IDbConnection db, IDbTransaction? tx, IReadOnlyList<ArchiveDocRow> docs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(docs);
        if (docs.Count == 0) return (0, 0);

        var docCount = 0;
        for (var off = 0; off < docs.Count; off += ChunkRows)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = docs.Skip(off).Take(ChunkRows).ToList();
            var sb = new StringBuilder(
                "INSERT IGNORE INTO legacy_unposted_documents " +
                "(doc_id, tenant_id, io_type, doc_date, legacy_dt, legacy_seq, legacy_buy_code, partner_id, reason, " +
                "supply_amount, vat_amount, line_count, memo, source_type, source_id, migrated_source_hash) VALUES ");
            var dp = new DynamicParameters();
            for (var i = 0; i < chunk.Count; i++)
            {
                var d = chunk[i];
                if (i > 0) sb.Append(',');
                sb.Append($"(@D{i},@T{i},@Io{i},@Dd{i},@Ldt{i},@Ls{i},@Lb{i},@P{i},@R{i},@Sa{i},@Va{i},@Lc{i},@M{i},@St{i},@Si{i},@H{i})");
                dp.Add($"D{i}", d.DocId); dp.Add($"T{i}", d.TenantId); dp.Add($"Io{i}", d.IoType);
                dp.Add($"Dd{i}", d.DocDate.Date); dp.Add($"Ldt{i}", d.LegacyDt); dp.Add($"Ls{i}", d.LegacySeq);
                dp.Add($"Lb{i}", d.LegacyBuyCode); dp.Add($"P{i}", d.PartnerId); dp.Add($"R{i}", d.Reason);
                dp.Add($"Sa{i}", d.SupplyAmount); dp.Add($"Va{i}", d.VatAmount); dp.Add($"Lc{i}", d.LineCount);
                dp.Add($"M{i}", d.Memo); dp.Add($"St{i}", SourceType); dp.Add($"Si{i}", d.SourceId);
                dp.Add($"H{i}", d.MigratedSourceHash);
            }
            docCount += await db.ExecuteAsync(new CommandDefinition(sb.ToString(), dp, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        }

        var lines = docs.SelectMany(d => d.Lines.Select(l => (d.TenantId, Line: l))).ToList();
        var lineCount = 0;
        for (var off = 0; off < lines.Count; off += ChunkRows)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = lines.Skip(off).Take(ChunkRows).ToList();
            var sb = new StringBuilder(
                "INSERT IGNORE INTO legacy_unposted_document_lines " +
                "(line_id, tenant_id, doc_id, line_no, item_id, item_name, spec, qty, unit_price, " +
                "supply_amount, vat_amount, memo, stock_source_id, migrated_source_hash) VALUES ");
            var dp = new DynamicParameters();
            for (var i = 0; i < chunk.Count; i++)
            {
                var (tid, l) = chunk[i];
                if (i > 0) sb.Append(',');
                sb.Append($"(@L{i},@T{i},@D{i},@N{i},@I{i},@Nm{i},@Sp{i},@Q{i},@U{i},@Sa{i},@Va{i},@M{i},@Ss{i},@H{i})");
                dp.Add($"L{i}", l.LineId); dp.Add($"T{i}", tid); dp.Add($"D{i}", l.DocId); dp.Add($"N{i}", l.LineNo);
                dp.Add($"I{i}", l.ItemId); dp.Add($"Nm{i}", l.ItemName); dp.Add($"Sp{i}", l.Spec);
                dp.Add($"Q{i}", l.Qty); dp.Add($"U{i}", l.UnitPrice); dp.Add($"Sa{i}", l.SupplyAmount);
                dp.Add($"Va{i}", l.VatAmount); dp.Add($"M{i}", l.Memo); dp.Add($"Ss{i}", l.StockSourceId);
                dp.Add($"H{i}", l.MigratedSourceHash);
            }
            lineCount += await db.ExecuteAsync(new CommandDefinition(sb.ToString(), dp, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        return (docCount, lineCount);
    }

    // ── 내부 도우미 (DataRow 읽기 규칙은 MdbMigrationService.GetStr/GetInt/GetDec 와 같다) ──

    private static string Str(DataRow r, string c)
    {
        if (!r.Table.Columns.Contains(c)) return string.Empty;
        var v = r[c];
        return v == DBNull.Value ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int Int(DataRow r, string c)
    {
        if (!r.Table.Columns.Contains(c)) return 0;
        var v = r[c];
        return v == DBNull.Value ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    private static decimal Dec(DataRow r, string c)
    {
        if (!r.Table.Columns.Contains(c)) return 0m;
        var v = r[c];
        return v == DBNull.Value ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    /// <summary>IJ_TAXNO — 글자("00000000")·숫자 둘 다 · 빈값/숫자 아님 → 0.</summary>
    private static int TaxNo(DataRow r)
    {
        var s = Str(r, "IJ_TAXNO").Trim();
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static string? Cut(string? s, int max) => s is null ? null : (s.Length > max ? s[..max] : s);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>키 해시 앞 16바이트 → GUID 글자(36자). 같은 키 = 같은 id.</summary>
    private static string StableId(string key)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(h.AsSpan(0, 16)).ToString();
    }
}
