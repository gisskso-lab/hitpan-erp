using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.API.Controllers;

// 도메인 별칭 검증 API — 랜딩 가입 폼 실시간 호출용
//
// 사장님 결재 2026-06-09:
//   "랜딩페이지로 고객사한테 히트판ERP주소 받고, 중복검사"
//
// 흐름:
//   1) 랜딩 입력란 타이핑 → debounce 500ms 후 GET /api/landing/domain-alias/check?alias=xxx
//   2) 형식·예약어·욕설·중복 4중 검증 응답
//   3) 가입 시점에 LandingSignupController가 동일 Service로 재검증 (race condition 차단)
[ApiController]
[Route("api/landing/domain-alias")]
[AllowAnonymous]
public class DomainAliasController : ControllerBase
{
    private readonly IDomainAliasService _service;
    private readonly ILogger<DomainAliasController> _logger;

    public DomainAliasController(IDomainAliasService service, ILogger<DomainAliasController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] string alias, CancellationToken ct)
    {
        try
        {
            var result = await _service.ValidateAsync(alias, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DomainAlias] check 실패 alias={Alias}", alias);
            return StatusCode(500, new
            {
                available = false,
                code = "server_error",
                message = "검증 처리 중 오류가 발생했습니다."
            });
        }
    }
}
