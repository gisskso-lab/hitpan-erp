namespace HitPan.Application.DTOs.Leave;

/// <summary>
/// 연차 제안 결과 — <b>제안일 뿐 확정이 아니다.</b>
/// </summary>
/// <remarks>
/// 🔴 반자동 3단(사장님 2026-08-12: <i>"자동계산하되 수동으로 수정가능하도록"</i>)의
/// <b>①제안</b> 단계다. 이 값이 그대로 잔여에 반영되지 않는다 —
/// 사람이 보고 고치고(②) 확정해야(③) 반영된다.
/// </remarks>
public sealed class AnnualLeaveSuggestionDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>귀속 연도.</summary>
    public int GrantYear { get; set; }

    /// <summary>계산 시점의 근속 연수.</summary>
    public decimal ServiceYears { get; set; }

    /// <summary>자동 계산이 제안하는 일수.</summary>
    public decimal SuggestedDays { get; set; }

    /// <summary>
    /// 부여 종류: <c>annual</c> 연차 / <c>monthly</c> 1년 미만 월차.
    /// </summary>
    public string GrantType { get; set; } = "annual";

    /// <summary>
    /// 🔴 <b>어떤 기준값으로 이렇게 계산했는지</b> 사람이 읽을 수 있게 적는다.
    /// </summary>
    /// <remarks>
    /// 숫자만 보여주면 관리자가 맞는지 틀린지 판단할 수 없다.
    /// 법이 바뀐 뒤에도 <b>과거 계산을 설명</b>할 수 있어야 한다(설계도 §0 지침 ③).
    /// </remarks>
    public string CalcBasis { get; set; } = string.Empty;

    /// <summary>
    /// 이미 부여된 건이 있으면 그 상태. 없으면 null.
    /// </summary>
    public string? ExistingStatus { get; set; }

    /// <summary>이미 확정된 일수(있으면).</summary>
    public decimal? ExistingGrantedDays { get; set; }

    /// <summary>
    /// 🔴 <b>사람이 봐야 할 사정</b>. 자동 계산이 판단할 수 없어 넘기는 것들.
    /// </summary>
    /// <remarks>
    /// 예: "주당 소정근로시간이 정해지지 않았습니다", "상시근로자수가 5인 미만입니다".
    /// 비어 있으면 그냥 확정해도 되고, 있으면 <b>확정 전에 확인</b>해야 한다.
    /// </remarks>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>연차 확정 요청 — <b>②수정 + ③확정</b>.</summary>
public sealed class ConfirmAnnualLeaveRequest
{
    public string EmployeeId { get; set; } = string.Empty;
    public int GrantYear { get; set; }
    public string GrantType { get; set; } = "annual";

    /// <summary>자동 계산이 제안했던 일수(그대로 넘긴다 — 이력에 남긴다).</summary>
    public decimal SuggestedDays { get; set; }

    /// <summary>사람이 확정한 일수. 제안과 달라도 된다.</summary>
    public decimal GrantedDays { get; set; }

    /// <summary>
    /// 제안과 다르게 정했다면 <b>왜 그랬는지</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 수정 이력이 없으면 "내 연차가 왜 이거냐" 에 답할 수 없고,
    /// 노동청 다툼에서 근거가 없다(사장님 반자동 원칙: 수정 가능하면 이력 필수).
    /// </remarks>
    public string? AdjustReason { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal? ServiceYears { get; set; }
    public string? CalcBasis { get; set; }
}

/// <summary>연차 부여 이력 한 줄.</summary>
public sealed class AnnualLeaveGrantDto
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
/// 노무 기준값 한 줄. <b>법이 바뀌면 이 값만 갈아끼운다.</b>
/// </summary>
public sealed class LaborPolicyDto
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyKey { get; set; } = string.Empty;
    public decimal PolicyValue { get; set; }
    public string ValueUnit { get; set; } = "day";

    /// <summary>🔴 적용 시작일 — 과거분은 옛 값으로 계산한다.</summary>
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>1이면 법정 기준 — <b>줄이면 위법</b>이다.</summary>
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
}

/// <summary>기준값 수정 요청.</summary>
public sealed class SaveLaborPolicyRequest
{
    public string PolicyKey { get; set; } = string.Empty;
    public decimal PolicyValue { get; set; }

    /// <summary>
    /// 🔴 이 값이 <b>언제부터</b> 적용되는지. 법은 시행일이 있다.
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>왜 고쳤는지 — 법 개정인지 회사 규정 변경인지.</summary>
    public string? UpdatedReason { get; set; }
}
