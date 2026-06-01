using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/admin/tenants")]
[Authorize(Policy = "PlatformOnly")]
public class AdminTenantController : ControllerBase
{
    private readonly IResellerService _resellerService;
    private readonly ITenantSnapshotService _snapshotService;
    private readonly ILogger<AdminTenantController> _logger;

    public AdminTenantController(
        IResellerService resellerService,
        ITenantSnapshotService snapshotService,
        ILogger<AdminTenantController> logger)
    {
        _resellerService = resellerService;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    /// <summary>
    /// Pull 복사본 — 직원 조회 전용 (헌법 #18: 5컬럼만, 수정 불가)
    /// </summary>
    [HttpGet("{tenantId}/employees")]
    public async Task<IActionResult> GetTenantEmployees(string tenantId, CancellationToken ct)
    {
        try
        {
            var rows = await _snapshotService.GetEmployeesAsync(tenantId, ct);
            var lastSync = await _snapshotService.GetLastSyncAtAsync(tenantId, ct);
            return Ok(new { success = true, data = new { items = rows, lastSyncAt = lastSync } });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 직원 snapshot 조회 실패 — {TenantId}", tenantId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    /// <summary>
    /// Pull 복사본 — 기기 조회 전용 (헌법 #18: 3컬럼만, 수정 불가)
    /// </summary>
    [HttpGet("{tenantId}/devices")]
    public async Task<IActionResult> GetTenantDevices(string tenantId, CancellationToken ct)
    {
        try
        {
            var rows = await _snapshotService.GetDevicesAsync(tenantId, ct);
            var lastSync = await _snapshotService.GetLastSyncAtAsync(tenantId, ct);
            return Ok(new { success = true, data = new { items = rows, lastSyncAt = lastSync } });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 기기 snapshot 조회 실패 — {TenantId}", tenantId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTenants(
        [FromQuery] string? status, [FromQuery] string? resellerId,
        [FromQuery] string? planType, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _resellerService.GetAdminTenantsAsync(status, resellerId, planType, search, page, size, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 고객사 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetTenant(string tenantId, CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetAdminTenantAsync(tenantId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 고객사 상세 조회 실패 — {TenantId}", tenantId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] AdminCreateTenantRequest request, CancellationToken ct)
    {
        try
        {
            var adminId = User.FindFirst("admin_id")?.Value ?? "";
            var result = await _resellerService.AdminCreateTenantAsync(request, adminId, ct);
            return Created($"/api/admin/tenants/{result.TenantId}", new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 고객사 등록 실패 — {CompanyName}", request.CompanyName);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPatch("{tenantId}/status")]
    public async Task<IActionResult> UpdateTenantStatus(
        string tenantId, [FromBody] UpdateTenantStatusRequest request, CancellationToken ct)
    {
        try
        {
            await _resellerService.UpdateTenantStatusAsync(tenantId, request, ct);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminTenant] 상태 변경 실패 — {TenantId}", tenantId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
