using HitPan.Application.DTOs.Backoffice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

// 작8 백오피스 P0 — 구독 결재 상태 관리 (W2 매니저 가도)
// 헌법 #7 PlatformOnly · #22 본사 메타만 (금액 메타만 — PG는 토스/카카오/네이버 영역)
[ApiController]
[Route("api/admin/subscriptions")]
[Authorize(Policy = "PlatformOnly")]
public class AdminSubscriptionController : ControllerBase
{
    private readonly ILogger<AdminSubscriptionController> _logger;

    public AdminSubscriptionController(ILogger<AdminSubscriptionController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetSubscriptions(
        [FromQuery] string? status, [FromQuery] string? tenantId,
        [FromQuery] string? planType, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        // 매니저 가도 영역 — subscriptions·payments 테이블 조회 (마이그 0409124653 적용 후)
        return Ok(new
        {
            success = true,
            data = new AdminSubscriptionListResponse { Items = new(), TotalCount = 0, Page = page, Size = size, TotalPages = 0 },
            note = "W2 매니저 가도 영역 — 백엔드 매니저 6/4 발진 (토스 webhook 연동 포함)"
        });
    }

    [HttpGet("{subscriptionId}")]
    public IActionResult GetSubscription(string subscriptionId, CancellationToken ct)
    {
        return NotFound(new { success = false, message = "W2 매니저 가도 영역 (subscriptions 조회 매니저 가도)" });
    }

    [HttpPatch("{subscriptionId}/cancel")]
    public IActionResult CancelSubscription(string subscriptionId, [FromBody] AdminCancelSubscriptionRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[AdminSubscription] 해지 요청 박제 — Sub={Sid} Reason={R} Refund={Rf}",
            subscriptionId, request.Reason, request.RefundLastPayment);
        return Accepted(new { success = true, message = "해지 요청 박제 완료 — W2 매니저 가도" });
    }
}
