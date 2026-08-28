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
    /// 🔴 20260828작14 W4 (사장님 결재 7) — 판매라인 어휘 통일.
    ///   수주완료 → 판매완료 → 판매확정 → 계산서발행 → 전자발행
    ///   DB enum 값은 한 글자도 안 바꾼다. 여기(표시 계층)에서만 이름을 준다 ⇒ 마이그 부담 0.
    public static string SalesOrder(string? status, bool isAuto = false)
    {
        if (isAuto) return "자동생성";
        return (status ?? "").Trim().ToLowerInvariant() switch
        {
            "draft"     => "임시저장",
            "confirmed" => "수주완료",   // 수주서 전용 — 원장 무접촉
            "partial"   => "부분출고",
            "delivered" => "판매완료",
            "closed"    => "판매완료",   // 새 정의에 closed 라는 이름이 없다 — 진행 단계로 흡수
            "cancelled" => "취소",
            _           => status ?? ""
        };
    }

    /// 거래명세서 (sales_deliveries) 상태 + 수금 진행도 결합 라벨.
    /// 수금 미완 = "판매확정 (미수)", 수금 완료 = "수금완료". 사장님 헌법.
    ///
    /// 🔴 20260828작14 W4 (결재 7) — draft 는 "판매완료"(작성·저장, 원장 무접촉),
    ///   confirmed 는 "판매확정"(재고 OUT · 미수금 ↑ · 분개)이다.
    ///   두 상태의 차이가 곧 되돌리기 방법의 차이다(판매완료→삭제 / 판매확정→취소).
    public static string Delivery(string? status, decimal totalAmount = 0, decimal collectedAmount = 0)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        if (s == "confirmed")
        {
            if (totalAmount <= 0) return "판매확정";
            if (collectedAmount >= totalAmount) return "수금완료";
            if (collectedAmount > 0) return "부분수금";
            return "판매확정 (미수)";
        }
        return s switch
        {
            "draft"     => "판매완료",
            "cancelled" => "취소",
            _           => status ?? ""
        };
    }

    /// 매출반품 (sales_returns) 상태.
    /// 🔴 20260828작14 W4 (결재 7) — 반품완료(마이너스 전표 O · 국세청 미발송)
    ///   → 반품확정(국세청 발송, 상계 완결).
    /// ⚠️ 철자 — sales_returns 는 canceled(l 하나). 명세서는 cancelled(l 둘).
    ///   둘 다 받는다 — 어느 쪽이 와도 화면에 영문이 노출되면 안 된다.
    public static string SalesReturn(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "draft"     => "반품완료",
        "confirmed" => "반품확정",
        "canceled"  => "취소",
        "cancelled" => "취소",
        _           => status ?? ""
    };

    /// <summary>
    /// 분할출고 뱃지 (결재 7 추가결정 ②).
    /// 🔴 <b>상태가 아니라 표시다.</b> 거래조건이 다양해서(잔금까지 받은 경우·계약금만)
    /// 상태로 쪼개면 조합이 폭발한다 ⇒ <c>판매완료 [분할출고]</c> 처럼 옆에 붙인다.
    /// 컬럼 신설 0건 — 잔량으로 파생한다.
    /// </summary>
    public static string? SplitShipmentBadge(decimal orderedQty, decimal deliveredQty) =>
        deliveredQty > 0 && deliveredQty < orderedQty ? "분할출고" : null;

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
