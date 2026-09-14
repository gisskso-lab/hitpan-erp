using HitPan.Application.Common;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 20260915작1 갈래 H — 「이전 프로그램 장부에 반영되지 않은 명세서」 조회 (읽기 전용).
/// 표: <c>legacy_unposted_documents</c> · <c>legacy_unposted_document_lines</c> (DB-123 · 이관만 INSERT).
/// <para>쓰기 메서드는 두지 않는다 — 보관 표는 이관만 넣고 화면·API 는 읽기만 한다(설계 §15).</para>
/// <para>tenant_id 는 호출자(컨트롤러)가 JWT 에서 꺼내 넘긴다(헌법 #2). 이 서비스는 파라미터로 받은 값을 그대로 WHERE 에만 쓴다.</para>
/// </summary>
public interface ILegacyUnpostedDocumentService
{
    /// <summary>목록 한 쪽 + 조건 전체 합계 + 사유별 건수.</summary>
    Task<LegacyUnpostedListResult> GetPagedAsync(string tenantId, LegacyUnpostedQuery query, CancellationToken ct = default);

    /// <summary>명세서 한 건 + 줄. 다른 회사 것이거나 없으면 null.</summary>
    Task<LegacyUnpostedDocumentDetail?> GetDetailAsync(string tenantId, string docId, CancellationToken ct = default);
}

/// <summary>목록 조건. 비어 있는 칸은 조건에서 뺀다.</summary>
public sealed class LegacyUnpostedQuery
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    /// <summary>거래처 이름 일부.</summary>
    public string? Partner { get; init; }
    /// <summary><c>sales</c>(판매) / <c>purchase</c>(매입) — 그 외 값은 무시.</summary>
    public string? IoType { get; init; }
    /// <summary>보관 사유 코드(<see cref="HitPan.Application.Services.LegacyMdbMapping.UnpostedReason"/>).</summary>
    public string? Reason { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>목록 응답.</summary>
public sealed class LegacyUnpostedListResult
{
    public PagedResult<LegacyUnpostedDocumentItem> Page { get; init; } = new();
    /// <summary>조건에 맞는 전체(쪽 아님) 줄 수 · 공급가 · 부가세.</summary>
    public long TotalLines { get; init; }
    public decimal TotalSupply { get; init; }
    public decimal TotalVat { get; init; }
    /// <summary>사유별 건수 — 사유 조건만 뺀 같은 조건.</summary>
    public IReadOnlyList<LegacyUnpostedReasonCount> Reasons { get; init; } = Array.Empty<LegacyUnpostedReasonCount>();
}

/// <summary>명세서 한 건 (머리).</summary>
public sealed class LegacyUnpostedDocumentItem
{
    public string DocId { get; set; } = string.Empty;
    public string IoType { get; set; } = string.Empty;
    /// <summary>판매 / 매입.</summary>
    public string IoTypeText { get; set; } = string.Empty;
    public DateTime DocDate { get; set; }
    /// <summary>이전 프로그램에 날짜가 없어 기준일로 둔 명세서.</summary>
    public bool DateMissing { get; set; }
    public int? LegacySeq { get; set; }
    /// <summary>히트판 거래처 이름 — 못 찾으면 null.</summary>
    public string? PartnerName { get; set; }
    public string? Reason { get; set; }
    /// <summary>사유 글자(사람 말).</summary>
    public string ReasonText { get; set; } = string.Empty;
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public int LineCount { get; set; }
    public string? Memo { get; set; }
}

/// <summary>명세서 줄.</summary>
public sealed class LegacyUnpostedLineItem
{
    public int LineNo { get; set; }
    public string? ItemName { get; set; }
    public string? Spec { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
}

/// <summary>상세 응답.</summary>
public sealed class LegacyUnpostedDocumentDetail
{
    public LegacyUnpostedDocumentItem Document { get; init; } = new();
    public IReadOnlyList<LegacyUnpostedLineItem> Lines { get; init; } = Array.Empty<LegacyUnpostedLineItem>();
}

/// <summary>사유별 건수.</summary>
public sealed class LegacyUnpostedReasonCount
{
    public string? Reason { get; set; }
    public string ReasonText { get; set; } = string.Empty;
    public long Count { get; set; }
}
