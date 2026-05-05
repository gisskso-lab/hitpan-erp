using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Stock;

// ── 재고 실사·조정 ──

/// <summary>재고 실사 조정 요청</summary>
public class StockAdjustRequest
{
    public string ItemId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "실사 수량은 음수일 수 없습니다.")]
    public decimal ActualQty { get; set; }      // 실사 수량

    public string? Reason { get; set; }          // 조정 사유
}

/// <summary>재고 실사 조정 결과</summary>
public class StockAdjustResultDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public decimal BeforeQty { get; set; }       // 조정 전 수량
    public decimal ActualQty { get; set; }       // 실사 수량
    public decimal DiffQty { get; set; }         // 차이 (실사 - 장부)
    public string AdjustType { get; set; } = string.Empty; // increase/decrease/match
    public DateTime AdjustedAt { get; set; }
}

// ── 재고 이송 ──

/// <summary>재고 이송 요청</summary>
public class StockTransferRequest
{
    public string ItemId { get; set; } = string.Empty;
    public string FromWarehouseId { get; set; } = string.Empty;
    public string ToWarehouseId { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "이송 수량은 0보다 커야 합니다.")]
    public decimal Qty { get; set; }

    public string? Memo { get; set; }
}

/// <summary>재고 이송 이력</summary>
public class StockTransferDto
{
    public long LedgerId { get; set; }
    public DateTime TransferDate { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string FromWarehouse { get; set; } = string.Empty;
    public string ToWarehouse { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? Memo { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

// ── 창고 분리 ──

/// <summary>창고 분리(자사/위탁) 현황</summary>
public class WarehouseSplitDto
{
    public string WarehouseId { get; set; } = string.Empty;
    public string WhCode { get; set; } = string.Empty;
    public string WhName { get; set; } = string.Empty;
    public string WhType { get; set; } = string.Empty;  // normal, consign, defect, return
    public string Location { get; set; } = string.Empty;
    public int ItemCount { get; set; }       // 보관 품목 수
    public decimal TotalQty { get; set; }    // 총 재고 수량
    public decimal TotalValue { get; set; }  // 총 재고 금액
}
