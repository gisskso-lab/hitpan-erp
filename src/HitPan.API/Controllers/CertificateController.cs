using HitPan.API.Authorization;
using HitPan.Application.DTOs.Certificate;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>범용인증서 관리 API — 민감정보 AES-256 + DPAPI 보호</summary>
[ApiController]
[Route("api/certificates")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class CertificateController : ControllerBase
{
    private readonly ITenantCertificateService _svc;

    public CertificateController(ITenantCertificateService svc) => _svc = svc;

    [HttpGet]
    [RequirePermission("CERTIFICATE", "view")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAllAsync(tid, ct));
    }

    [HttpGet("{certId}")]
    [RequirePermission("CERTIFICATE", "view")]
    public async Task<IActionResult> Get(string certId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.GetAsync(certId, tid, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>PFX/P12 파일 업로드. multipart/form-data</summary>
    [HttpPost("upload")]
    [RequirePermission("CERTIFICATE", "create")]
    public async Task<IActionResult> Upload(
        [FromForm] UploadCertificateRequest request,
        IFormFile file,
        CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "인증서 파일이 첨부되지 않았습니다." });

        // PFX/P12 확장자만 허용 (FileSecurityHelper는 현재 기본 확장자만 있음 — 여기서 직접 체크)
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".pfx" && ext != ".p12")
            return BadRequest(new { message = "PFX 또는 P12 형식만 업로드 가능합니다." });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "인증서 파일은 5MB 이하만 가능합니다." });

        // 파일 내용 읽기
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        try
        {
            var certId = await _svc.UploadAsync(request, bytes, tid, uid, ct);
            return Created($"/api/certificates/{certId}", new { certId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{certId}/status")]
    [RequirePermission("CERTIFICATE", "update")]
    public async Task<IActionResult> UpdateStatus(string certId, [FromBody] UpdateCertificateStatusRequest request, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        await _svc.UpdateStatusAsync(certId, request, tid, uid, ct);
        return Ok(new { message = "변경되었습니다." });
    }

    [HttpPut("{certId}/primary")]
    [RequirePermission("CERTIFICATE", "update")]
    public async Task<IActionResult> SetPrimary(string certId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        try
        {
            await _svc.SetPrimaryAsync(certId, tid, uid, ct);
            return Ok(new { message = "기본 인증서로 설정되었습니다." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{certId}")]
    [RequirePermission("CERTIFICATE", "delete")]
    public async Task<IActionResult> Delete(string certId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        await _svc.DeleteAsync(certId, tid, uid, ct);
        return Ok(new { message = "폐기되었습니다." });
    }
}
