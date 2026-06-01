using System.Security.Claims;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 백오피스 Pull 동기화 API (사장님 결재 2026-06-01)
///
/// 정책:
///  - /api/sync/token : tenant_admin JWT 필요. Sync 토큰 발급 (24h, 회전)
///  - /api/sync/employees, /api/sync/devices : Sync 토큰만 허용. 일반 JWT 거부.
///
/// 헌법:
///  - #5 (해시만 저장) / #7 (역할 분리) / #18·#22 (5컬럼·3컬럼) / #23 (5중 검증)
/// </summary>
[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly ISyncTokenService _tokenService;
    private readonly ISyncService _syncService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ISyncTokenService tokenService,
        ISyncService syncService,
        ILogger<SyncController> logger)
    {
        _tokenService = tokenService;
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Sync 토큰 발급 — tenant_admin / system_admin JWT 필요
    /// 응답: { token, expiresAt }
    /// </summary>
    [HttpPost("token")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> IssueToken(CancellationToken ct)
    {
        var tenantId = User.FindFirstValue("tenant_id");
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "tenant_id claim missing" });

        try
        {
            var result = await _tokenService.IssueAsync(tenantId, ct);
            return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync token issue failed for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "token issue failed" });
        }
    }

    /// <summary>
    /// 직원 Pull (헌법 #18: 5컬럼만)
    /// Header: Authorization: Bearer {syncToken}
    /// </summary>
    [HttpGet("employees")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
    {
        var tenantId = await ResolveSyncTokenAsync(ct);
        if (tenantId is null) return Unauthorized(new { error = "invalid sync token" });

        var rows = await _syncService.GetEmployeesAsync(tenantId, ct);
        return Ok(rows);
    }

    /// <summary>
    /// 기기 Pull (헌법 #18: 3컬럼만)
    /// </summary>
    [HttpGet("devices")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDevices(CancellationToken ct)
    {
        var tenantId = await ResolveSyncTokenAsync(ct);
        if (tenantId is null) return Unauthorized(new { error = "invalid sync token" });

        var rows = await _syncService.GetDevicesAsync(tenantId, ct);
        return Ok(rows);
    }

    private async Task<string?> ResolveSyncTokenAsync(CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth)) return null;
        var header = auth.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header[prefix.Length..].Trim();
        return await _tokenService.ValidateAsync(token, ct);
    }
}
