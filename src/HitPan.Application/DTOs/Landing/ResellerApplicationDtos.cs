namespace HitPan.Application.DTOs.Landing;

/// <summary>
/// 대리점 신청 요청 DTO (사장님 결재 2026-06-01)
/// 랜딩 /reseller/signup 에서 전송
/// </summary>
public record ResellerApplicationRequest(
    string CompanyName,
    string RepresentativeName,
    string ContactName,
    string ContactEmail,
    string ContactPhone,
    string? BusinessNo,
    string? Region,
    string? SalesChannel,
    int? ExpectedCustomers,
    string? Motivation
);

public record ResellerApplicationResult(
    string ApplicationId,
    string Status,
    string Message
);
