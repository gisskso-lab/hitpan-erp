using System.Security.Cryptography.X509Certificates;
using HitPan.Application.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 전자세금계산서 인증서 등록·관리 — 작B v3.0 방식 A 다이렉트
///
/// 헌법 정합:
/// - #11: 권한 어드민 직접 (테넌트 어드민만 인증서 관리)
/// - #22: 본사 데이터 0 (인증서는 고객 PC만)
/// - #29: 인프라 사전 승인 (사장님 결재 5/25 22:00 + 23:00 일괄)
/// </summary>
[ApiController]
[Route("api/settings/tax-invoice-cert")]
// 🔴 작(2026-08-14) — 샌드박스 실측 적발: 정책 이름이 틀려 있었다.
//   종전 "TenantAdmin" 은 **등록된 적 없는 이름**이다(Program.cs 의 실제 이름은
//   "TenantAdminOnly"). ASP.NET Core 는 없는 정책을 만나면 요청을 **400 으로 튕긴다** —
//   그래서 전자세금계산서 인증서 화면 3개가 **부모계정에게도** 통째로 안 열렸다.
//   ⚠️ 오타라 빌드도 시험도 안 잡는다. 실제로 화면을 열어봐야만 드러나는 자리다.
[Authorize(Policy = "TenantAdminOnly")]
public sealed class TaxInvoiceCertController : ControllerBase
{
    private readonly IDoubleEncryptionService _crypto;
    private readonly ICertStorageService _storage;
    private readonly ITpmKeyService _tpm;
    private readonly IRemoteControlDetector _remoteDetector;
    private readonly ICertIntegrityWatchdog _watchdog;
    private readonly ILogger<TaxInvoiceCertController> _logger;

    public TaxInvoiceCertController(
        IDoubleEncryptionService crypto,
        ICertStorageService storage,
        ITpmKeyService tpm,
        IRemoteControlDetector remoteDetector,
        ICertIntegrityWatchdog watchdog,
        ILogger<TaxInvoiceCertController> logger)
    {
        _crypto = crypto;
        _storage = storage;
        _tpm = tpm;
        _remoteDetector = remoteDetector;
        _watchdog = watchdog;
        _logger = logger;
    }

    /// <summary>워치독 무결성 검증 (WS-23 + WS-24 + WS-25 종합).</summary>
    [HttpGet("watchdog")]
    public async Task<IActionResult> GetWatchdogReport()
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var report = await _watchdog.CheckIntegrityAsync(tenantId);
        return Ok(report);
    }

    /// <summary>등록된 인증서 정보 조회 (메타데이터만, PFX 0).</summary>
    [HttpGet]
    public async Task<IActionResult> GetInfo()
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var data = await _storage.LoadAsync(tenantId);
        if (data is null) return Ok((object?)null);

        var (_, metadata) = data.Value;
        var daysToExpiry = (int)(metadata.NotAfter - DateTime.UtcNow).TotalDays;

        return Ok(new
        {
            metadata.Subject,
            metadata.Issuer,
            metadata.SerialNumber,
            metadata.NotBefore,
            metadata.NotAfter,
            DaysToExpiry = daysToExpiry,
            metadata.RegisteredAt
        });
    }

    /// <summary>보안 상태 조회 (TPM 봉인 + 원격제어 감지 + 가상 키패드).</summary>
    [HttpGet("security-status")]
    public IActionResult GetSecurityStatus()
    {
        var remoteActive = _remoteDetector.IsRemoteControlActive(out var detectedTool);
        var activeTools = _remoteDetector.GetActiveRemoteTools();
        return Ok(new
        {
            TpmSealed = _tpm.IsTpmAvailable(),
            RemoteControlDetected = remoteActive,
            DetectedRemoteTool = detectedTool,
            ActiveRemoteTools = activeTools,
            VirtualKeypadEnabled = true,
            StorageRoot = _storage.GetSecureStorageRoot()
        });
    }

    /// <summary>인증서 등록 (3중 암호화 + TPM 봉인).</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCertRequest request)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (request.PfxBytes is null || request.PfxBytes.Length == 0)
            return BadRequest(new { Success = false, ErrorMessage = "인증서 파일이 비어있습니다" });

        if (string.IsNullOrEmpty(request.UserPin))
            return BadRequest(new { Success = false, ErrorMessage = "인증서 비밀번호 필수" });

        if (request.PfxBytes.Length > 100 * 1024)
            return BadRequest(new { Success = false, ErrorMessage = "인증서 파일 크기 초과 (100KB 제한)" });

        try
        {
            // 1차: 메타데이터 추출 (검증용 임시 로딩, EphemeralKeySet으로 메모리 즉시 폐기)
            CertMetadata metadata;
            using (var tempCert = new X509Certificate2(
                request.PfxBytes,
                request.UserPin,
                X509KeyStorageFlags.EphemeralKeySet))
            {
                metadata = new CertMetadata(
                    Subject: tempCert.Subject,
                    Issuer: tempCert.Issuer,
                    SerialNumber: tempCert.SerialNumber,
                    NotBefore: tempCert.NotBefore.ToUniversalTime(),
                    NotAfter: tempCert.NotAfter.ToUniversalTime(),
                    RegisteredAt: DateTime.UtcNow);
            }

            // 2차: 3중 암호화 (사용자 PIN + ERP 마스터 키 + TPM 봉인)
            var encrypted = await _crypto.EncryptAsync(request.PfxBytes, request.UserPin);

            // 3차: 격리 저장 (%LOCALAPPDATA%\HitPan\Secure\)
            await _storage.SaveAsync(tenantId, encrypted, metadata);

            _logger.LogInformation("인증서 등록 완료 (Tenant: {Tenant}, Subject: {Subject})",
                tenantId, metadata.Subject);

            return Ok(new
            {
                Success = true,
                Message = "인증서 등록 완료 (3중 보안 적용)",
                metadata.Subject,
                metadata.NotAfter
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "인증서 등록 실패 (Tenant: {Tenant})", tenantId);
            return BadRequest(new { Success = false, ErrorMessage = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "인증서 등록 중 예외 발생 (Tenant: {Tenant})", tenantId);
            return StatusCode(500, new { Success = false, ErrorMessage = "내부 오류" });
        }
    }

    /// <summary>인증서 삭제.</summary>
    [HttpDelete]
    public async Task<IActionResult> Remove()
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var deleted = await _storage.DeleteAsync(tenantId);
        return Ok(new { Success = deleted, Message = deleted ? "삭제 완료" : "등록된 인증서 없음" });
    }
}

public sealed record RegisterCertRequest(byte[] PfxBytes, string UserPin);
