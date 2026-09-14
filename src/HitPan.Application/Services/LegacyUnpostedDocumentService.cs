using System.Data;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 20260915작1 갈래 H — 「이전 프로그램 장부에 반영되지 않은 명세서」 조회 서비스 (읽기 전용).
/// <list type="bullet">
///   <item>SELECT 만 한다. INSERT/UPDATE/DELETE 문장이 이 파일에 없다(보관 표 = 이관만 INSERT · 설계 §15).</item>
///   <item>모든 문장 첫 조건 = <c>u.tenant_id = @T</c>. 거래처 조인도 <c>p.tenant_id = u.tenant_id</c> 로 묶는다(다른 회사 거래처 이름이 붙지 않게).</item>
///   <item>조건 칸은 고정 문장의 <c>(@X IS NULL OR …)</c> 로만 켠다 — 문자열 조립 없음.</item>
///   <item>목록·합계·사유별 건수는 <c>QueryMultipleAsync</c> 한 번(헌법 #16 · 병렬 금지).</item>
/// </list>
/// DESCRIBE (헌법 #13 · 2026-09-15 격리 33306 f_new): legacy_unposted_documents(doc_id · tenant_id · io_type varchar(10) · doc_date date ·
/// legacy_dt varchar(8) · legacy_seq int · legacy_buy_code bigint · partner_id · reason varchar(30) · supply_amount·vat_amount decimal(15,2) ·
/// line_count int · memo varchar(500) · source_type · source_id · migrated_source_hash · created_at) ·
/// legacy_unposted_document_lines(line_id · tenant_id · doc_id · line_no int · item_id · item_name·spec varchar(200) · qty decimal(15,3) ·
/// unit_price decimal(15,4) · supply_amount·vat_amount decimal(15,2) · memo · stock_source_id · migrated_source_hash · created_at) ·
/// partners(partner_id · tenant_id · partner_name varchar(100)).
/// </summary>
public sealed class LegacyUnpostedDocumentService : ILegacyUnpostedDocumentService
{
    /// <summary>한 쪽 최대 줄 수 (악의·실수 방어).</summary>
    public const int MaxPageSize = 200;

    private const int CommandTimeoutSec = 60;

    private readonly IDbConnection _db;
    private readonly ILogger<LegacyUnpostedDocumentService> _logger;

    public LegacyUnpostedDocumentService(IDbConnection db, ILogger<LegacyUnpostedDocumentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private const string FilterSql = """
         WHERE u.tenant_id = @T
           AND (@From IS NULL OR u.doc_date >= @From)
           AND (@To IS NULL OR u.doc_date <= @To)
           AND (@Io IS NULL OR u.io_type = @Io)
           AND (@PartnerLike IS NULL OR p.partner_name LIKE @PartnerLike)
        """;

    private const string FromSql = """
          FROM legacy_unposted_documents u
          LEFT JOIN partners p ON p.partner_id = u.partner_id AND p.tenant_id = u.tenant_id
        """;

    private const string ReasonFilterSql = "   AND (@Reason IS NULL OR u.reason = @Reason)";

    private const string ListSql =
        "SELECT u.doc_id AS DocId, u.io_type AS IoType, u.doc_date AS DocDate, u.legacy_dt AS LegacyDt,\n" +
        "       u.legacy_seq AS LegacySeq, p.partner_name AS PartnerName, u.reason AS Reason,\n" +
        "       u.supply_amount AS SupplyAmount, u.vat_amount AS VatAmount, u.line_count AS LineCount, u.memo AS Memo\n" +
        FromSql + "\n" + FilterSql + "\n" + ReasonFilterSql + "\n" +
        " ORDER BY u.doc_date DESC, u.legacy_seq DESC, u.doc_id\n" +
        " LIMIT @Take OFFSET @Skip;\n" +
        "SELECT COUNT(*) AS Cnt, COALESCE(SUM(u.line_count), 0) AS LineTotal,\n" +
        "       COALESCE(SUM(u.supply_amount), 0) AS Supply, COALESCE(SUM(u.vat_amount), 0) AS Vat\n" +
        FromSql + "\n" + FilterSql + "\n" + ReasonFilterSql + ";\n" +
        "SELECT u.reason AS Reason, COUNT(*) AS Cnt\n" +
        FromSql + "\n" + FilterSql + "\n" +
        " GROUP BY u.reason\n" +
        " ORDER BY COUNT(*) DESC, u.reason;";

    private const string DetailSql = """
        SELECT u.doc_id AS DocId, u.io_type AS IoType, u.doc_date AS DocDate, u.legacy_dt AS LegacyDt,
               u.legacy_seq AS LegacySeq, p.partner_name AS PartnerName, u.reason AS Reason,
               u.supply_amount AS SupplyAmount, u.vat_amount AS VatAmount, u.line_count AS LineCount, u.memo AS Memo
          FROM legacy_unposted_documents u
          LEFT JOIN partners p ON p.partner_id = u.partner_id AND p.tenant_id = u.tenant_id
         WHERE u.tenant_id = @T AND u.doc_id = @DocId;
        SELECT l.line_no AS LineNo, l.item_name AS ItemName, l.spec AS Spec, l.qty AS Qty, l.unit_price AS UnitPrice,
               l.supply_amount AS SupplyAmount, l.vat_amount AS VatAmount, l.memo AS Memo
          FROM legacy_unposted_document_lines l
         WHERE l.tenant_id = @T AND l.doc_id = @DocId
         ORDER BY l.line_no, l.line_id;
        """;

    /// <inheritdoc />
    public async Task<LegacyUnpostedListResult> GetPagedAsync(string tenantId, LegacyUnpostedQuery query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var io = NormalizeIo(query.IoType);
        var reason = string.IsNullOrWhiteSpace(query.Reason) ? null : query.Reason.Trim();
        if (reason is { Length: > 30 }) reason = reason[..30];
        var partner = string.IsNullOrWhiteSpace(query.Partner) ? null : query.Partner.Trim();
        if (partner is { Length: > 100 }) partner = partner[..100];

        var args = new
        {
            T = tenantId,
            From = query.From?.Date,
            To = query.To?.Date,
            Io = io,
            Reason = reason,
            PartnerLike = partner is null ? null : "%" + EscapeLike(partner) + "%",
            Take = size,
            Skip = (page - 1) * size,
        };

        using var multi = await _db.QueryMultipleAsync(new CommandDefinition(
            ListSql, args, commandTimeout: CommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        var rows = (await multi.ReadAsync<DocRow>().ConfigureAwait(false)).ToList();
        var total = await multi.ReadSingleAsync<TotalRow>().ConfigureAwait(false);
        var reasons = (await multi.ReadAsync<ReasonRow>().ConfigureAwait(false)).ToList();

        _logger.LogDebug("[보관명세서] 목록 tenant={Tenant} page={Page} size={Size} total={Total}", tenantId, page, size, total.Cnt);

        return new LegacyUnpostedListResult
        {
            Page = new PagedResult<LegacyUnpostedDocumentItem>
            {
                Page = page,
                PageSize = size,
                TotalCount = (int)Math.Min(int.MaxValue, total.Cnt),
                Items = rows.Select(ToItem).ToList(),
            },
            TotalLines = total.LineTotal,
            TotalSupply = total.Supply,
            TotalVat = total.Vat,
            Reasons = reasons.Select(r => new LegacyUnpostedReasonCount
            {
                Reason = r.Reason,
                ReasonText = LegacyMdbMapping.UnpostedReasonText(r.Reason),
                Count = r.Cnt,
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<LegacyUnpostedDocumentDetail?> GetDetailAsync(string tenantId, string docId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (string.IsNullOrWhiteSpace(docId) || docId.Length > 36) return null;

        using var multi = await _db.QueryMultipleAsync(new CommandDefinition(
            DetailSql, new { T = tenantId, DocId = docId.Trim() }, commandTimeout: CommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        var doc = (await multi.ReadAsync<DocRow>().ConfigureAwait(false)).FirstOrDefault();
        var lines = (await multi.ReadAsync<LegacyUnpostedLineItem>().ConfigureAwait(false)).ToList();
        if (doc is null) return null;

        return new LegacyUnpostedDocumentDetail { Document = ToItem(doc), Lines = lines };
    }

    // ── 내부 ──

    /// <summary>판매·매입만 받는다. 그 외(빈칸 포함) → null(조건 끔).</summary>
    public static string? NormalizeIo(string? io)
    {
        var v = (io ?? string.Empty).Trim().ToLowerInvariant();
        return v is "sales" or "purchase" ? v : null;
    }

    /// <summary>LIKE 특수문자(<c>\ % _</c>)를 글자로 만든다.</summary>
    public static string EscapeLike(string s)
        => s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static LegacyUnpostedDocumentItem ToItem(DocRow r) => new()
    {
        DocId = r.DocId,
        IoType = r.IoType ?? string.Empty,
        IoTypeText = r.IoType switch { "sales" => "판매", "purchase" => "매입", _ => "구분 없음" },
        DocDate = r.DocDate,
        DateMissing = !LegacyMdbMapping.TryParseLegacyDate(r.LegacyDt, out _),
        LegacySeq = r.LegacySeq,
        PartnerName = r.PartnerName,
        Reason = r.Reason,
        ReasonText = LegacyMdbMapping.UnpostedReasonText(r.Reason),
        SupplyAmount = r.SupplyAmount,
        VatAmount = r.VatAmount,
        LineCount = r.LineCount,
        Memo = r.Memo,
    };

    private sealed class DocRow
    {
        public string DocId { get; set; } = string.Empty;
        public string? IoType { get; set; }
        public DateTime DocDate { get; set; }
        public string? LegacyDt { get; set; }
        public int? LegacySeq { get; set; }
        public string? PartnerName { get; set; }
        public string? Reason { get; set; }
        public decimal SupplyAmount { get; set; }
        public decimal VatAmount { get; set; }
        public int LineCount { get; set; }
        public string? Memo { get; set; }
    }

    private sealed class TotalRow
    {
        public long Cnt { get; set; }
        public long LineTotal { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
    }

    private sealed class ReasonRow
    {
        public string? Reason { get; set; }
        public long Cnt { get; set; }
    }
}
