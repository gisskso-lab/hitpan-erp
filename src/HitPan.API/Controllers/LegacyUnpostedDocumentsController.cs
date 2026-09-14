using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HitPan.API.Controllers;

/// <summary>
/// 🔴 20260915작1 갈래 H — 「이전 프로그램 장부에 반영되지 않은 명세서」 조회 API (읽기 전용).
/// <list type="bullet">
///   <item>GET 만 둔다 — 쓰기(POST/PUT/PATCH/DELETE) 0개. 보관 표는 이관만 넣는다(설계 §15).</item>
///   <item>권한 = 자료 가져오기 화면과 같은 규칙: <c>[Authorize(Policy = "TenantAdminOnly")]</c> (<see cref="MigrationController"/>).</item>
///   <item>tenant_id 는 JWT 클레임 → TenantMiddleware 가 넣은 <c>HttpContext.Items["TenantId"]</c> 에서만 꺼낸다(헌법 #2). 쿼리 파라미터로 받지 않는다.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/migration/legacy-unposted")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class LegacyUnpostedDocumentsController : ControllerBase
{
    private readonly ILegacyUnpostedDocumentService _service;
    private readonly ILogger<LegacyUnpostedDocumentsController> _logger;

    public LegacyUnpostedDocumentsController(ILegacyUnpostedDocumentService service, ILogger<LegacyUnpostedDocumentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>목록 (기간·거래처·판매/매입·사유 · 쪽 나눔).</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? partner,
        [FromQuery] string? ioType,
        [FromQuery] string? reason,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (from is not null && to is not null && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "시작일이 종료일보다 늦습니다." });

        var result = await _service.GetPagedAsync(tenantId, new LegacyUnpostedQuery
        {
            From = from,
            To = to,
            Partner = partner,
            IoType = ioType,
            Reason = reason,
            Page = page,
            PageSize = pageSize,
        }, ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>명세서 한 건 + 줄.</summary>
    [HttpGet("{docId}")]
    public async Task<IActionResult> GetDetail(string docId, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var detail = await _service.GetDetailAsync(tenantId, docId, ct).ConfigureAwait(false);
        if (detail is null)
        {
            // 다른 회사 명세서도 여기로 온다 — 있다/없다를 구분해 알려주지 않는다.
            _logger.LogInformation("[보관명세서] 상세 없음 tenant={Tenant}", tenantId);
            return NotFound(new { message = "명세서를 찾을 수 없습니다." });
        }
        return Ok(detail);
    }
}
