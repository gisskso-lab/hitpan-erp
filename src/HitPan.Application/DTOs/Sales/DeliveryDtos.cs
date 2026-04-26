namespace HitPan.Application.DTOs.Sales;

public class DeliveryListDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public string DeliveryNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class DeliveryDetailDto : DeliveryListDto
{
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
    public decimal PrevReceivable { get; set; }
    public decimal TodaySales { get; set; }
    public decimal TodayReceipt { get; set; }
}

public class DeliveryItemDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
    public int RowNo { get; set; }
}

public class UpdateDeliveryDto
{
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Memo { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
}

/// <summary>수주 목록 DTO</summary>
public class SalesOrderListDto
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

/// <summary>수주서 단건 상세(헤더+라인) — 목록 → 편집 로드용.</summary>
public class SalesOrderDetailDto
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<SalesOrderDetailItemDto> Items { get; set; } = new();
}

public class SalesOrderDetailItemDto
{
    public string OrderItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 거래명세서 확정 직후 안전재고 위반 후보 (사장님 지시 2026-04-26).
/// 프론트에서 "자동발주 하시겠습니까?" 다이얼로그를 띄울 때 사용.
/// </summary>
public class AutoOrderCandidateDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal CurrentQty { get; set; }
    public decimal SafetyQty { get; set; }
    public decimal SuggestedOrderQty { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>"out_of_stock" | "below_safety"</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>자동발주 생성 결과 — 공급처별 발주서 1건 또는 실패 사유.</summary>
public class AutoOrderResultDto
{
    public string? PoId { get; set; }
    public string? PoNo { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public List<string> ItemIds { get; set; } = new();
    public bool Success { get; set; }
    public string? Reason { get; set; }
}

public class PartnerSearchDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string? BizNo { get; set; }
    public string? Tel { get; set; }
    public string? Address { get; set; }
}
