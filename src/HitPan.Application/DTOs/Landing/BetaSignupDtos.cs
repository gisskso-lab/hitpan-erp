namespace HitPan.Application.DTOs.Landing;

// ─────────────────────────────────────────────────────────────────
// 히트판 베타 신청 DTOs — 랜딩페이지 공개 API (AllowAnonymous)
// tenant_id 없음 — 공개 신청서이므로 멀티테넌트 불필요
// ─────────────────────────────────────────────────────────────────

/// <summary>베타 신청 요청</summary>
public class BetaSignupRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? EmployeeCnt { get; set; }
    public string? Message { get; set; }
}

/// <summary>베타 신청 응답</summary>
public class BetaSignupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
