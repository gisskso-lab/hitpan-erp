using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 설치 박제 컨트롤러 (사장님 결재 2026-06-02 모두결재)
//
// 박제 흐름:
//   고객 EXE → POST /api/install/provision-tunnel (라이선스 키 + 계정 ID)
//             ↓ 본사 서버가 Cloudflare API 호출 (헌법 #29 정합)
//             ← www.{id}.hitpan.kr + tunnel credentials 박제
//
// 헌법 정합:
//   #18·#22 — 토큰 본사 서버만 박제. 고객 EXE 절대 박제 0건
//   #29 — 인프라 조작 사전 승인. 본사 자동 박제는 사장님 정책 결재 완료 박제
//   #34 — 정식 완성도 박제 (베타부터)
[ApiController]
[Route("api/install")]
public class InstallController : ControllerBase
{
    private readonly ICloudflareProvisioningService _cf;
    private readonly ILogger<InstallController> _logger;

    public InstallController(ICloudflareProvisioningService cf, ILogger<InstallController> logger)
    {
        _cf = cf;
        _logger = logger;
    }

    [HttpPost("provision-tunnel")]
    [AllowAnonymous] // 라이선스 키 박제로 인증 박제 (HMAC 검증)
    public async Task<IActionResult> ProvisionTunnel([FromBody] ProvisionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.AccountId))
            return BadRequest(new { success = false, message = "license_key·account_id 필수" });

        // TODO: 라이선스 키 박제 HMAC 검증 박제 (LandingSignupService.ComputeHmacSha256 박제 정합)
        // 베타 박제: 키 형식만 박제 검증

        var result = await _cf.ProvisionAsync(request.AccountId, request.AccountId, ct);
        if (!result.Success)
        {
            _logger.LogWarning("[Install] 프로비저닝 실패 account={Acc} msg={Msg}", request.AccountId, result.ErrorMessage);
            return StatusCode(502, new { success = false, message = result.ErrorMessage });
        }

        return Ok(new
        {
            success = true,
            message = result.ErrorMessage,
            domain = result.FullDomain,
            tunnelId = result.TunnelId
        });
    }

    public record ProvisionRequest(string LicenseKey, string AccountId);
}
