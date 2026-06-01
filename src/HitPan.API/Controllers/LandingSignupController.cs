using HitPan.Application.DTOs.Landing;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 랜딩 가입 컨트롤러 (사장님 결재 2026-06-01)
//
// 8명제 #1·#2·#3 정합:
//  - 랜딩 SignupPage HandleSubmit → POST /api/landing/signup
//  - 본사 백오피스는 메타만 박제 (평문 사업자정보 0건)
//  - signup_token 부여 → OTP·결제 단계로 이어짐
[ApiController]
[Route("api/landing")]
public class LandingSignupController : ControllerBase
{
    private readonly ILandingSignupService _service;
    private readonly ILogger<LandingSignupController> _logger;

    public LandingSignupController(
        ILandingSignupService service,
        ILogger<LandingSignupController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request, CancellationToken ct)
    {
        var result = await _service.SubmitAsync(request, ct);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.Message });

        return Ok(new
        {
            success = true,
            message = result.Message,
            signupToken = result.SignupToken
        });
    }
}
