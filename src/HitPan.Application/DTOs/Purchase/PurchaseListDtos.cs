namespace HitPan.Application.DTOs.Purchase;

/// <summary>
/// 발주 목록 조회 응답 DTO.
/// </summary>
public class PurchaseOrderListDto
{
    public string PoId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }

    /// <summary>작성자 이름 — employees.user_id = created_by 조인 (20260825작16).
    /// 판매 전표의 「작성자」와 같은 축이다. 담당사원(EmployeeId)과는 다른 값 —
    /// 담당자는 지정하는 사람, 작성자는 실제로 전표를 친 사람이다.
    /// ⚠️ 과거 전표는 비어 있다. 매입 경로가 created_by 를 쓰기 시작한 게 이번이라서다.</summary>
    public string? CreatedByName { get; set; }
}

/// <summary>
/// 매입명세 목록 조회 응답 DTO.
/// </summary>
public class PurchaseReceiptListDto
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }

    /// <summary>
    /// 반품 상태 — 이 매입명세서로 만들어진 <b>살아있는 반품서</b>의 상태 (20260826작5).
    /// <c>null</c>=반품 없음 · <c>"draft"</c>=반품 작성중 · <c>"confirmed"</c>=반품 확정.
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 지시(2026-08-26): <i>"매입명세서 목록에 반품처리된 전표는 상태처리를 「반품」이라고
    /// 표기할것. 전표에 전부 매입확정이라고만 나옴"</i>
    ///
    /// <para>
    /// ⚠️ <b>이 값은 <c>purchase_receipts</c> 에 저장되지 않는다.</b> 반품확정은 그 표를 한 줄도
    /// UPDATE 하지 않는다 — <b>반품 전후가 바이트 동일</b>하다(20260825작16 에서 예고된 자리).
    /// 그래서 <c>purchase_returns</c> 를 <b>조회 시점에 되짚어</b> 채운다.
    /// </para>
    /// <para>
    /// 🔴 <b>왜 매입명세서 <c>status</c> 에 덮어쓰지 않는가</b> — 매입확정과 반품은 <b>다른 축</b>이다.
    /// 반품했다고 매입이 취소된 게 아니다(샀다가 돌려준 것이지, <b>안 산 게 아니다</b>).
    /// <c>status='returned'</c> 로 덮으면 <b>매입확정 사실이 사라져</b> 원장·집계·부가세가 어긋난다.
    /// ⇒ <b>두 축을 각각 보여준다</b>: 매입확정 + 반품.
    /// </para>
    /// </remarks>
    public string? ReturnStatus { get; set; }

    /// <summary>작성자 이름 — employees.user_id = created_by 조인 (20260825작16).
    /// 판매 전표의 「작성자」와 같은 축이다. 담당사원(EmployeeId)과는 다른 값 —
    /// 담당자는 지정하는 사람, 작성자는 실제로 전표를 친 사람이다.
    /// ⚠️ 과거 전표는 비어 있다. 매입 경로가 created_by 를 쓰기 시작한 게 이번이라서다.</summary>
    public string? CreatedByName { get; set; }
}

/// <summary>
/// 발주서 단건 상세(헤더+라인) — 목록 → 편집 로드용.
/// </summary>
public class PurchaseOrderDetailDto
{
    public string PoId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseOrderDetailItemDto> Items { get; set; } = new();
}

public class PurchaseOrderDetailItemDto
{
    public string PoItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 매입반품 단건 상세(헤더+라인) — 목록 → 편집 로드용.
/// </summary>
public class PurchaseReturnDetailDto
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string? ReceiptId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseReturnDetailItemDto> Items { get; set; } = new();
}

public class PurchaseReturnDetailItemDto
{
    public string ReturnItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 매입명세서 단건 상세(헤더+라인) — 목록 → 편집 로드용.
/// </summary>
public class PurchaseReceiptDetailDto
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string? PoId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseReceiptDetailItemDto> Items { get; set; } = new();
}

public class PurchaseReceiptDetailItemDto
{
    public string ReceiptItemId { get; set; } = string.Empty;
    public string? PoItemId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

public class PurchaseReturnListDto
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }

    /// <summary>작성자 이름 — employees.user_id = created_by 조인 (20260825작16).
    /// 판매 전표의 「작성자」와 같은 축이다. 담당사원(EmployeeId)과는 다른 값 —
    /// 담당자는 지정하는 사람, 작성자는 실제로 전표를 친 사람이다.
    /// ⚠️ 과거 전표는 비어 있다. 매입 경로가 created_by 를 쓰기 시작한 게 이번이라서다.</summary>
    public string? CreatedByName { get; set; }
}
