using HitPan.Application.DTOs.Backoffice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

// 작8 백오피스 P0 — 라이선스 발급·갱신·취소 (W2 매니저 가도)
// 헌법 #7 PlatformOnly · #22 본사 메타만 · #29 PM 스켈레톤 박제만, 실제 발급 흐름은 매니저
[ApiController]
[Route("api/admin/licenses")]
[Authorize(Policy = "PlatformOnly")]
public class AdminLicenseController : ControllerBase
{
    private readonly ILogger<AdminLicenseController> _logger;

    public AdminLicenseController(ILogger<AdminLicenseController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetLicenses(
        [FromQuery] string? status, [FromQuery] string? tenantId,
        [FromQuery] string? planType, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        // 매니저 가도 영역 — IService 주입 후 실제 조회 (licenses 테이블 신규)
        return Ok(new
        {
            success = true,
            data = new AdminLicenseListResponse { Items = new(), TotalCount = 0, Page = page, Size = size, TotalPages = 0 },
            note = "W2 매니저 가도 영역 — 백엔드 매니저 6/4 발진"
        });
    }

    [HttpGet("{licenseId}")]
    public IActionResult GetLicense(string licenseId, CancellationToken ct)
    {
        return NotFound(new { success = false, message = "W2 매니저 가도 영역 (licenses 테이블 신규 발진 후 동작)" });
    }

    [HttpPost]
    public IActionResult IssueLicense([FromBody] AdminIssueLicenseRequest request, CancellationToken ct)
    {
        var adminId = User.FindFirst("admin_id")?.Value ?? "";
        _logger.LogInformation("[AdminLicense] 발급 요청 박제 — Tenant={Tid} Plan={Plan} Admin={Aid}",
            request.TenantId, request.PlanType, adminId);
        return Accepted(new
        {
            success = true,
            message = "발급 요청 박제 완료 — 실제 발급은 W2 매니저 가도 (LicenseIssueService 신규)",
            tenantId = request.TenantId
        });
    }

    [HttpPatch("{licenseId}/renew")]
    public IActionResult RenewLicense(string licenseId, [FromBody] AdminRenewLicenseRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[AdminLicense] 갱신 요청 박제 — License={Lid} ExpiresAt={At}", licenseId, request.ExpiresAt);
        return Accepted(new { success = true, message = "갱신 요청 박제 완료 — W2 매니저 가도" });
    }

    [HttpPatch("{licenseId}/revoke")]
    public IActionResult RevokeLicense(string licenseId, [FromBody] AdminRevokeLicenseRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[AdminLicense] 취소 요청 박제 — License={Lid} Reason={R}", licenseId, request.Reason);
        return Accepted(new { success = true, message = "취소 요청 박제 완료 — W2 매니저 가도" });
    }
}
