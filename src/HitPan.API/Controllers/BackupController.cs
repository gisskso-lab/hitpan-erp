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
// 🔴 2026-08-11 (사장님 지시): 자료관리는 **부모계정 + 메인PC 환경에서만**.
//   백업·복원은 자료가 든 그 컴퓨터에서 하는 일이다. 원격에서 남의 회사 자료를
//   되돌리는 경로를 만들지 않는다. 화면만 감추면 API 를 직접 불러 뚫린다.
//
//   ⚠️ 업데이트 직전 자동백업은 이 API 를 쓰지 않는다 — 워치독이 mysqldump 를 직접 돌린다
//     (WatchdogBackupRunner). 그래서 여기를 막아도 업데이트 흐름은 끊기지 않는다. 확인함.
[HitPan.API.Security.MainPcOnly]
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
