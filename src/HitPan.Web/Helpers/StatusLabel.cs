namespace HitPan.Web.Helpers;

/// 사장님 헌법 (2026-04-27): "전표 목록 상태표시를 영어로 하지말고 현장감 있는 한글로."
///
/// ERP 전 영역의 status 코드(영문)를 현장 직원이 즉시 이해할 한글 라벨로 변환.
/// DB enum 값은 그대로 두고(코드 호환), 표시만 한글화. 한 곳에서 관리해 일관성 유지.
public static class StatusLabel
{
    /// 거래명세서·매입명세서 등 비즈니스 문서 공용 상태.
    public static string Document(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "confirmed" => "확정",
        "cancelled" => "취소",
        "deleted"   => "삭제",
        _           => status ?? ""
    };

    /// 발주서 (purchase_orders) 상태.
    /// DB enum: draft / ordered / partial / received / cancelled.
    public static string PurchaseOrder(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "ordered"   => "발주완료",
        "partial"   => "부분입고",
        "received"  => "입고완료",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// 발주 라인 item_status: pending / partial / closed.
    public static string PurchaseOrderItem(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "pending" => "대기",
        "partial" => "부분입고",
        "closed"  => "입고완료",
        _         => status ?? ""
    };

    /// 매입명세서 (purchase_receipts) 상태.
    public static string PurchaseReceipt(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "confirmed" => "확정",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// 견적서 (quotations) 상태.
    public static string Quotation(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "issued"    => "발송",
        "confirmed" => "수주전환",
        "cancelled" => "취소",
        "expired"   => "기한만료",
        _           => status ?? ""
    };

    /// 수주서 (sales_orders) 상태.
    public static string SalesOrder(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "confirmed" => "확정",
        "partial"   => "부분출고",
        "delivered" => "출고완료",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// 거래명세서 (sales_deliveries) 상태 + 수금 진행도 결합 라벨.
    /// 수금 미완 = "확정 (미수)", 수금 완료 = "수금완료". 사장님 헌법.
    public static string Delivery(string? status, decimal totalAmount = 0, decimal collectedAmount = 0)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        if (s == "confirmed")
        {
            if (totalAmount <= 0) return "확정";
            if (collectedAmount >= totalAmount) return "수금완료";
            if (collectedAmount > 0) return "부분수금";
            return "확정 (미수)";
        }
        return s switch
        {
            "draft"     => "임시저장",
            "cancelled" => "취소",
            _           => status ?? ""
        };
    }

    /// 세금계산서 (tax_invoices) 상태.
    public static string TaxInvoice(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "임시저장",
        "issued"    => "발행",
        "sent"      => "전송",
        "approved"  => "승인",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// 결재 문서.
    public static string Approval(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "pending"   => "결재대기",
        "approved"  => "승인",
        "rejected"  => "반려",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// 안전재고 알림 (stock_alerts).
    public static string StockAlert(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "pending"   => "발주대기",
        "ordered"   => "발주완료",
        "dismissed" => "무시",
        _           => status ?? ""
    };

    /// 결재선 라인 상태.
    public static string ApprovalLine(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "pending"  => "대기",
        "approved" => "승인",
        "rejected" => "반려",
        _          => status ?? ""
    };
}
