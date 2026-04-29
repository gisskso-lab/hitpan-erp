using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 자료 백업·복원 API (사장님 결재 2026-04-29).
/// 로컬 미러 백업: 1차 폴더(메인 하드) + 2차 폴더(외장/USB) 동시 저장.
/// §#18 본사 미수신 — 모든 파일은 고객사 로컬에서만 처리.
/// 권한: TenantAdminOnly — 백업/복원은 어드민 전용.
/// </summary>
[ApiController]
[Route("api/backup")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class BackupController : HitPanControllerBase
{
    private readonly IBackupService _service;

    public BackupController(IBackupService service)
    {
        _service = service;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var dto = await _service.GetSettingsAsync(TenantId!, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateBackupSettingsRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        await _service.UpdateSettingsAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _service.GetHistoryAsync(TenantId!, limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunBackup(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var result = await _service.RunBackupAsync(TenantId!, "manual", ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] RestoreRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var result = await _service.RestoreAsync(TenantId!, UserId, req, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("restore-history")]
    public async Task<IActionResult> GetRestoreHistory([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _service.GetRestoreHistoryAsync(TenantId!, limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }
}
