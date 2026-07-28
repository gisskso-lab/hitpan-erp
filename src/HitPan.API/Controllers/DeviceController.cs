using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 등록 기기 관리 API.
/// - 히트판 과금 모델: 기기 수 제한(계정 무제한).
/// - TenantAdmin: 테넌트 내 전체 기기 목록/폐기 가능
/// - 일반 사용자(tenant_user): 자기 기기만 조회
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize(Policy = "TenantOnly")]
public sealed class DeviceController : ControllerBase
{
    private readonly ITenantDeviceService _svc;

    public DeviceController(ITenantDeviceService svc) => _svc = svc;

    /// <summary>기기 목록 — TenantAdmin은 전체, 일반 사용자는 자기 것만.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();

        var all = await _svc.GetAllAsync(tid, ct);

        // 부모계정(tenant_admin) 이외는 자기 기기만 필터
        //   platform_admin 절 제거 (보안 격벽 2026-06-18): 본사 계층은 백오피스 전용 — ERP가 발급 안 함.
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
        {
            all = all.Where(d => d.UserId == uid).ToList();
        }
        return Ok(all);
    }

    /// <summary>현재 테넌트의 기기 쿼터 (한도·사용량).</summary>
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetQuotaAsync(tid, ct));
    }

    /// <summary>기기 폐기 — TenantAdmin만.</summary>
    [HttpPost("revoke/{id}")]
    public async Task<IActionResult> Revoke(string id, [FromBody] RevokeDeviceRequest? body, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        // platform_admin 절 제거 (보안 격벽 2026-06-18): 본사 계층은 백오피스 전용. 부모계정(tenant_admin)만 폐기 가능.
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        await _svc.RevokeAsync(id, tid, uid, body?.Reason, ct);
        return Ok(new { message = "기기가 폐기되었습니다." });
    }

    public sealed class RevokeDeviceRequest
    {
        public string? Reason { get; set; }
    }
}
