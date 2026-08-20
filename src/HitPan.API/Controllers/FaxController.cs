using HitPan.Application.DTOs.Fax;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 팩스 발송 API (사장님 오더 2026-08-21 — "업체팩스번호: 실제 팩스전송").
/// 설정은 어드민만, 발송·이력은 로그인 사용자. EmailController 와 동일 권한 구조.
/// tenant_id 는 JWT 클레임에서만 취한다 (§#2 — 파라미터 수신 금지).
/// </summary>
[ApiController]
[Route("api/fax")]
[Authorize(Policy = "TenantOnly")]
public sealed class FaxController : HitPanControllerBase
{
    private readonly IFaxService _fax;

    public FaxController(IFaxService fax) => _fax = fax;

    [HttpGet("settings")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var dto = await _fax.GetSettingsAsync(TenantId!, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut("settings")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateFaxSettingsRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        await _fax.UpdateSettingsAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("settings/test")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var resp = await _fax.TestConnectionAsync(TenantId!, ct).ConfigureAwait(false);
        return Ok(resp);
    }

    /// <summary>
    /// 문서 팩스 발송.
    /// 🔴 공급자 미설정이면 응답의 IsMock=true 로 내려간다. 화면은 이 경우
    ///    성공으로 표시하면 안 된다 — 실제로 전송되지 않았다 (§#23).
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendFaxRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var resp = await _fax.SendDocumentAsync(TenantId!, UserId, req, ct).ConfigureAwait(false);
        return Ok(resp);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? documentType, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _fax.GetHistoryAsync(TenantId!, documentType, limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }
}
