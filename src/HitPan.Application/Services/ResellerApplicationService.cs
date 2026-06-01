using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Application.DTOs.Landing;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 대리점 신청 처리 (사장님 결재 2026-06-01)
/// 헌법 #15·#23 정합: 검증 + 감사 로그
/// </summary>
public class ResellerApplicationService : IResellerApplicationService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ResellerApplicationService> _logger;

    public ResellerApplicationService(IUnitOfWork uow, ILogger<ResellerApplicationService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ResellerApplicationResult> SubmitAsync(ResellerApplicationRequest request, CancellationToken ct = default)
    {
        if (request is null)
            return new ResellerApplicationResult("", "rejected", "요청 비어 있음");

        var validationError = Validate(request);
        if (validationError is not null)
            return new ResellerApplicationResult("", "rejected", validationError);

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        var applicationId = Guid.NewGuid().ToString();

        try
        {
            await db.ExecuteAsync(@"
                INSERT INTO reseller_applications
                  (application_id, company_name, representative_name, contact_name, contact_email, contact_phone,
                   business_no, region, sales_channel, expected_customers, motivation, status, submitted_at)
                VALUES
                  (@ApplicationId, @CompanyName, @RepresentativeName, @ContactName, @ContactEmail, @ContactPhone,
                   @BusinessNo, @Region, @SalesChannel, @ExpectedCustomers, @Motivation, 'pending', UTC_TIMESTAMP())",
                new
                {
                    ApplicationId = applicationId,
                    request.CompanyName,
                    request.RepresentativeName,
                    request.ContactName,
                    request.ContactEmail,
                    request.ContactPhone,
                    request.BusinessNo,
                    request.Region,
                    request.SalesChannel,
                    request.ExpectedCustomers,
                    request.Motivation
                });

            _logger.LogInformation("Reseller application submitted: {ApplicationId} from {Email}",
                applicationId, request.ContactEmail);

            return new ResellerApplicationResult(
                applicationId,
                "pending",
                "신청이 접수되었습니다. 본사 심사 후 2영업일 내 연락드리겠습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reseller application submit failed for {Email}", request.ContactEmail);
            return new ResellerApplicationResult("", "rejected", $"신청 실패: {ex.Message}");
        }
    }

    private static string? Validate(ResellerApplicationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyName)) return "회사명을 입력해주세요";
        if (string.IsNullOrWhiteSpace(req.RepresentativeName)) return "대표자명을 입력해주세요";
        if (string.IsNullOrWhiteSpace(req.ContactName)) return "담당자명을 입력해주세요";
        if (string.IsNullOrWhiteSpace(req.ContactEmail)) return "이메일을 입력해주세요";
        if (!Regex.IsMatch(req.ContactEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return "올바른 이메일 형식이 아닙니다";
        if (string.IsNullOrWhiteSpace(req.ContactPhone)) return "연락처를 입력해주세요";
        if (req.CompanyName.Length > 120) return "회사명은 120자 이하";
        if (req.ExpectedCustomers is < 0) return "예상 고객사 수는 0 이상";
        return null;
    }

    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
            await c.OpenAsync(ct).ConfigureAwait(false);
        else
            db.Open();
    }
}
