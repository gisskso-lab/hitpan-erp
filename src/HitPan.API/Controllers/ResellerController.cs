// ============================================================================
// WS-20260601-17 ResellerController — 대리점 RLS API (8명제 #6·#7 정합)
// ----------------------------------------------------------------------------
// 작성일       : 2026-06-01
// 작성팀       : 백엔드 매니저(Harvard·Oracle 30년) + 프론트 매니저(Harvard·Meta 25년)
// 정합 산출물  : src/HitPan.API/Services/ResellerRlsService.cs
//                src/HitPan.API/Repositories/BackofficeRepositoryBase.cs (WS-16)
//                src/HitPan.API/Services/Auth/JwtClaimsBuilder.cs (WS-16)
//
// 라우트       : api/reseller/v2/*
//   기존 ResellerPortalController(api/reseller/*) 와 라우트 충돌 회피를 위해
//   /v2 prefix 채택 (헌법 #1 덮어쓰기 금지 + 작업지시서 "라우트 충돌 grep 의무").
//   기존 portal 은 account_type=reseller_admin 레거시 클레임 기반,
//   본 컨트롤러는 role=reseller + reseller_serial JWT 클레임 기반(WS-16).
//
// 절대 원칙
//   - [Authorize(Policy = "ResellerSelfOnly")] — JWT role=reseller + reseller_serial 보유
//   - 컨트롤러는 reseller_serial 을 절대 파라미터로 받지 않는다 (헌법 #2 확장).
//     서비스가 JWT 클레임에서만 추출 (BackofficeRepositoryBase.ApplyRls).
//   - 다른 대리점 접근 시도(UnauthorizedAccessException) = 403 + 감사로그 박제.
//   - 응답에 평문 사업자번호·상호·대표자 절대 금지 (8명제 #3·#6).
//   - 빈 catch 금지 (헌법 #15).
// ============================================================================

using System.Text.Json;
using System.Data.Common;
using Dapper;
using HitPan.API.Services;
using HitPan.API.Services.Auth;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/reseller/v2")]
[Authorize(Policy = "ResellerSelfOnly")]
public sealed class ResellerController : ControllerBase
{
    private readonly IResellerRlsService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResellerController> _logger;

    public ResellerController(
        IResellerRlsService service,
        IUnitOfWork unitOfWork,
        ILogger<ResellerController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── 자기 영업 고객사 시리얼 목록 ───────────────────────────────────────────
    [HttpGet("tenants")]
    public async Task<IActionResult> GetMyTenants(
        [FromQuery] string? search,
        [FromQuery(Name = "payment_status")] string? paymentStatus,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        // 헌법 #2 확장: 가드 — reseller_id/reseller_serial/tenant_id 가 쿼리스트링으로
        // 잠입하면 즉시 400. 서비스가 JWT 클레임만 신뢰하도록 진입점에서 차단.
        [FromQuery(Name = "reseller_id")] string? resellerIdGuard = null,
        [FromQuery(Name = "reseller_serial")] string? resellerSerialGuard = null,
        [FromQuery(Name = "tenant_id")] string? tenantIdGuard = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(resellerIdGuard)
            || !string.IsNullOrEmpty(resellerSerialGuard)
            || !string.IsNullOrEmpty(tenantIdGuard))
        {
            throw new ArgumentException(
                "헌법 #2 위반: reseller_id/reseller_serial/tenant_id 를 파라미터로 받을 수 없습니다.");
        }

        try
        {
            var rows = await _service.GetTenantsByResellerAsync(search, paymentStatus, page, size, ct);
            return Ok(new { success = true, data = rows });
        }
        catch (UnauthorizedAccessException ex)
        {
            await TryAuditAsync("TENANTS_QUERY_DENIED", null, ex.Message, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "권한 없음" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reseller/v2/tenants] 조회 실패");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "서버 오류" });
        }
    }

    // ── 자기 영업 고객사 CS 티켓 ──────────────────────────────────────────────
    [HttpGet("cs/tickets")]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetTicketsByResellerAsync(status, page, size, ct);
            return Ok(new { success = true, data = rows });
        }
        catch (UnauthorizedAccessException ex)
        {
            await TryAuditAsync("CS_QUERY_DENIED", null, ex.Message, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "권한 없음" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reseller/v2/cs/tickets] 조회 실패");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "서버 오류" });
        }
    }

    [HttpPost("cs/tickets")]
    public async Task<IActionResult> CreateTicket(
        [FromBody] ResellerCsTicketCreateRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "요청 본문 필수" });
        }
        try
        {
            var ticketId = await _service.CreateTicketByResellerAsync(request, ct);
            return Ok(new { success = true, data = new { ticket_id = ticketId } });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            await TryAuditAsync("CS_CREATE_DENIED", request.TargetSerial, ex.Message, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "권한 없음 — 다른 대리점 고객사" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reseller/v2/cs/tickets POST] 생성 실패");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "서버 오류" });
        }
    }

    // ── 자기 수수료 정산 ───────────────────────────────────────────────────────
    [HttpGet("commissions")]
    public async Task<IActionResult> GetMyCommissions(
        [FromQuery] string? period,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetCommissionsByResellerAsync(period, page, size, ct);
            return Ok(new { success = true, data = rows });
        }
        catch (UnauthorizedAccessException ex)
        {
            await TryAuditAsync("COMMISSIONS_QUERY_DENIED", null, ex.Message, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "권한 없음" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reseller/v2/commissions] 조회 실패");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "서버 오류" });
        }
    }

    // ── 자기 영업 실적 메타 대시보드 ───────────────────────────────────────────
    [HttpGet("sales-stats")]
    public async Task<IActionResult> GetSalesStats(CancellationToken ct = default)
    {
        try
        {
            var stats = await _service.GetSalesStatsAsync(ct);
            return Ok(new { success = true, data = stats });
        }
        catch (UnauthorizedAccessException ex)
        {
            await TryAuditAsync("STATS_QUERY_DENIED", null, ex.Message, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "권한 없음" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reseller/v2/sales-stats] 조회 실패");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "서버 오류" });
        }
    }

    // ── reseller_audit_log 박제 (감사) ─────────────────────────────────────────
    // RLS 위반 시도 = 403 + 감사로그 INSERT. 평문 식별자 박제 금지.
    // 헌법 #3 INSERT ONLY 원장 정합. DDL 미반영 환경에서는 경고 로그만.
    private async Task TryAuditAsync(string action, string? targetSerial, string detail, CancellationToken ct)
    {
        var serial = User.FindFirst(JwtClaimsBuilder.ClaimResellerSerial)?.Value ?? string.Empty;
        var details = JsonSerializer.Serialize(new
        {
            detail,
            ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ua = Request.Headers.UserAgent.ToString()
        });

        const string sql = @"
INSERT INTO reseller_audit_log
    (reseller_serial, action, target_serial, details, requested_at)
VALUES
    (@Serial, @Action, @Target, @Details, @Now);";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            await db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Serial = serial,
                Action = action,
                Target = targetSerial,
                Details = details,
                Now = DateTime.UtcNow
            }, cancellationToken: ct));
        }
        catch (DbException ex)
        {
            // DDL 미반영 환경 안전망. 빈 catch 금지 — 경고 박제.
            _logger.LogWarning(ex,
                "[ResellerAudit] reseller_audit_log INSERT 실패 action={Action} serial={Serial}",
                action, serial);
        }
    }
}
