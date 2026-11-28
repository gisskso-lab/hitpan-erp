namespace HitPan.Application.DTOs.Settings;

public sealed class UpdateTenantSettingsDto
{
    public bool AllowForcePriceInput { get; set; } = true;

    public bool AllowForceVatInput { get; set; }

    public bool AllowZeroPrice { get; set; }

    public bool AllowPastEdit { get; set; }

    public bool AllowForceStockAdjust { get; set; } = true;

    public bool AllowCreditOverride { get; set; }

    public int PriceDeviationLimit { get; set; } = 50;

    public bool ForceEditRequirePassword { get; set; } = true;

    /// <summary>비워 두면 기존 해시 유지, 빈 문자열이면 해시 제거, 값이 있으면 새로 해시 저장.</summary>
    public string? PastEditPassword { get; set; }

    public string StockEvalMethod { get; set; } = "moving_avg";

    public bool UseMultiWarehouse { get; set; }

    public bool StockShortageAlert { get; set; } = true;

    public bool AllowMinusStock { get; set; }

    public string PriceInputType { get; set; } = "net";

    public bool AutoVatAdjust { get; set; } = true;

    public string VatRoundType { get; set; } = "round";

    public decimal PriceARate { get; set; } = 1.00m;

    public decimal PriceBRate { get; set; } = 1.10m;

    public decimal PriceCRate { get; set; } = 1.20m;

    public decimal PriceDRate { get; set; } = 1.30m;

    public decimal PriceERate { get; set; } = 1.50m;

    public bool UseCreditLimit { get; set; } = true;

    public decimal CreditLimitAmount { get; set; } = 1000000m;

    public bool ShowPurchasePrice { get; set; }

    public bool UseSalesByEmployee { get; set; } = true;

    public bool UsePersonalInfoProtect { get; set; } = true;

    public string IndustryType { get; set; } = "retail";

    // ── 20260928작4 쿨타임 §5-1 · DB-132 (추가 · #1) ─────────────────────────────
    // 🔴 세 칸 모두 「null = 그대로 둔다」 — 이 칸을 모르는 화면(옛 판)이 저장해도 켜 둔 쿨타임이 꺼지지 않는다.
    //    시각 두 칸은 빈 문자열 = 비운다(과거수정 비밀번호 칸과 같은 약속).

    /// <summary>자동 로그아웃 켬/끔. <c>null</c> = 그대로.</summary>
    public bool? CooltimeEnabled { get; set; }

    /// <summary>자동 로그아웃 시각 <c>"HH:mm"</c>. <c>null</c> = 그대로 · <c>""</c> = 비움.</summary>
    public string? CooltimeAt { get; set; }

    /// <summary>정시 퇴근 시각 <c>"HH:mm"</c>. <c>null</c> = 그대로 · <c>""</c> = 비움.</summary>
    public string? RegularLeaveTime { get; set; }
}
