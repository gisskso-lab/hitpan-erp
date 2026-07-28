using HitPan.Application.DTOs.Email;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 이메일 발송 API (사장님 결재 2026-04-29).
/// SMTP 설정은 어드민만, 발송/이력/PDF 다운로드는 로그인 사용자.
/// </summary>
[ApiController]
[Route("api/email")]
[Authorize(Policy = "TenantOnly")]
public sealed class EmailController : HitPanControllerBase
{
    private readonly IEmailService _email;
    private readonly IPdfRenderService _pdf;

    public EmailController(IEmailService email, IPdfRenderService pdf)
    {
        _email = email; _pdf = pdf;
    }

    [HttpGet("settings")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var dto = await _email.GetSettingsAsync(TenantId!, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut("settings")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        await _email.UpdateSettingsAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("settings/test")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> TestSmtp(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var resp = await _email.TestSmtpAsync(TenantId!, ct).ConfigureAwait(false);
        return Ok(resp);
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendDocumentEmailRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var resp = await _email.SendDocumentAsync(TenantId!, UserId, req, ct).ConfigureAwait(false);
        return Ok(resp);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] string? documentType, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _email.GetHistoryAsync(TenantId!, documentType, limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("preview-pdf")]
    public async Task<IActionResult> PreviewPdf([FromQuery] string documentType, [FromQuery] string documentId, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var (bytes, fname) = await _pdf.RenderDocumentAsync(TenantId!, documentType, documentId, ct).ConfigureAwait(false);
        return File(bytes, "application/pdf", fname);
    }

    /// <summary>거래처 이메일/대표명 lookup (Dialog 자동채움용).</summary>
    [HttpGet("partner-email")]
    public async Task<IActionResult> GetPartnerEmail([FromQuery] string partnerId, [FromServices] System.Data.IDbConnection db, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        if (string.IsNullOrWhiteSpace(partnerId)) return Ok(new { email = "", partnerName = "" });
        const string sql = "SELECT email, partner_name FROM partners WHERE tenant_id = @T AND partner_id = @P";
        var row = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<(string email, string partner_name)>(
            db, sql, new { T = TenantId, P = partnerId }).ConfigureAwait(false);
        return Ok(new { email = row.email ?? "", partnerName = row.partner_name ?? "" });
    }
}
