namespace HitPan.Web.Models;

/// <summary>
/// 연차 제안 한 줄 — <b>제안일 뿐 확정이 아니다.</b>
/// </summary>
/// <remarks>
/// ⚠️ 타입은 API 응답 그대로여야 한다. 단계0 에서 <c>decimal</c> vs <c>decimal?</c> 하나 어긋나
/// <c>JsonException</c> 이 나고 <c>catch</c> 가 삼켜 목록 전체가 빈 화면이 됐다.
/// </remarks>
public sealed class AnnualLeaveSuggestionModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int GrantYear { get; set; }
    public decimal ServiceYears { get; set; }

    /// <summary>자동 계산이 제안한 일수(원본 — 바뀌지 않는다).</summary>
    public decimal SuggestedDays { get; set; }

    public string GrantType { get; set; } = "annual";
    public string CalcBasis { get; set; } = string.Empty;

    public string? ExistingStatus { get; set; }
    public decimal? ExistingGrantedDays { get; set; }
    public List<string> Warnings { get; set; } = new();

    // ── 화면에서만 쓰는 것(서버로 안 보낸다) ──

    /// <summary>
    /// ② 사람이 고치는 값. 처음엔 제안값으로 채워진다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal EditDays { get; set; }

    /// <summary>제안과 다르게 정한 이유.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AdjustReason { get; set; }

    /// <summary>이미 확정된 건인가 — 확정분은 다시 주지 않는다.</summary>
    public bool IsConfirmed => ExistingStatus == "confirmed";

    /// <summary>사람이 제안과 다르게 고쳤나.</summary>
    public bool IsAdjusted => EditDays != SuggestedDays;

    public string GrantTypeLabel => GrantType switch
    {
        "annual"  => "연차",
        "monthly" => "월차(1년 미만)",
        _         => GrantType
    };
}

/// <summary>연차 확정 요청.</summary>
public sealed class ConfirmAnnualLeaveModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public int GrantYear { get; set; }
    public string GrantType { get; set; } = "annual";
    public decimal SuggestedDays { get; set; }
    public decimal GrantedDays { get; set; }
    public string? AdjustReason { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal? ServiceYears { get; set; }
    public string? CalcBasis { get; set; }
}

/// <summary>연차 부여 이력 한 줄.</summary>
public sealed class AnnualLeaveGrantModel
{
    public string GrantId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int GrantYear { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal SuggestedDays { get; set; }
    public decimal GrantedDays { get; set; }
    public bool IsAdjusted { get; set; }
    public string? AdjustReason { get; set; }
    public string GrantType { get; set; } = "annual";
    public decimal? ServiceYears { get; set; }
    public string? CalcBasis { get; set; }
    public string Status { get; set; } = "draft";
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public string StatusLabel => Status switch
    {
        "draft"     => "제안됨",
        "confirmed" => "확정",
        "cancelled" => "취소",
        _           => Status
    };

    public string GrantTypeLabel => GrantType switch
    {
        "annual"    => "연차",
        "monthly"   => "월차(1년 미만)",
        "adjust"    => "조정",
        "carryover" => "이월",
        _           => GrantType
    };
}

/// <summary>
/// 노무 기준값 한 줄 — <b>법이 바뀌면 이 값만 갈아끼운다.</b>
/// </summary>
public sealed class LaborPolicyModel
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyKey { get; set; } = string.Empty;
    public decimal PolicyValue { get; set; }
    public string ValueUnit { get; set; } = "day";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsStatutory { get; set; }
    public string? UpdatedReason { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string UnitLabel => ValueUnit switch
    {
        "day"   => "일",
        "hour"  => "시간",
        "rate"  => "%",
        "count" => "",
        _       => ValueUnit
    };

    /// <summary>화면에서 고치는 값.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal EditValue { get; set; }
}

/// <summary>기준값 수정 요청.</summary>
public sealed class SaveLaborPolicyModel
{
    public string PolicyKey { get; set; } = string.Empty;
    public decimal PolicyValue { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;
    public string? UpdatedReason { get; set; }
}
