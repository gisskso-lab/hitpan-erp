using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ISettingsService settingsService, ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _settingsService.GetAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Save([FromBody] UpdateTenantSettingsDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _settingsService.SaveAsync(dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    /// 사업장 기본정보(tenants)를 조회한다.
    /// </summary>
    [HttpGet("company")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetCompany(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _settingsService.GetCompanyAsync(tenantId, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }

    /// <summary>
    /// 사업장 기본정보(tenants)를 갱신한다.
    /// </summary>
    [HttpPut("company")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> SaveCompany([FromBody] UpdateTenantCompanyDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 🔴 2026-08-10 ([3-V] 병렬검증 I-6) — 잠긴 항목은 조용히 무시하지 않는다.
        //   종전엔 잠금 위반이 와도 그대로 200 "적용되었습니다" 였다. 고객은 고쳤다고 믿는데
        //   값은 그대로라 원인을 알 수 없었다(2026-08-09 권한 화면과 같은 구조).
        //   ⇒ 저장은 하되(나머지 항목은 정상 반영) 무엇이 반영되지 않았는지 함께 돌려준다.
        var rejected = await _settingsService.SaveCompanyAsync(dto, tenantId, ct).ConfigureAwait(false);

        if (rejected.Count > 0)
        {
            _logger.LogWarning(
                "[SaveCompany] 잠긴 항목 변경 시도 거부 tenant={TenantId} fields={Fields}",
                tenantId, string.Join(",", rejected));

            return Ok(new
            {
                saved = true,
                rejectedFields = rejected,
                message = $"{string.Join(" · ", rejected)} 항목은 사업자등록증에서 자동으로 채워진 정보라 "
                          + "변경되지 않았습니다. 변경하시려면 [기본 사업장 정보 변경 요청] 을 이용해주세요."
            });
        }

        return Ok(new { saved = true, rejectedFields = Array.Empty<string>() });
    }

    [HttpPost("validate-unit-price")]
    public async Task<IActionResult> ValidateUnitPrice(
        [FromBody] ValidateUnitPriceRequestDto dto,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _settingsService.ValidateUnitPriceAsync(
            tenantId,
            dto.UnitPrice,
            dto.ReferencePrice,
            ct).ConfigureAwait(false);

        return Ok(result);
    }

    [HttpPost("verify-force-edit-password")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> VerifyForceEditPassword(
        [FromBody] VerifyForceEditPasswordRequestDto dto,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(dto.Password))
        {
            return Ok(new { valid = false });
        }

        var ok = await _settingsService.VerifyForceEditPasswordAsync(
            tenantId,
            dto.Password,
            ct).ConfigureAwait(false);

        return Ok(new { valid = ok });
    }

    // ═══════════════════════════════════════════════════════════════════
    // 회사 그림 보관소 — 로고 · 직인 · 출력 머리 (20261026작1 §B-2 · 결-H)
    //
    // 🔴 사장님 결재 2026-10-26: *"응 이 차수에서 만들어"*
    //
    // 🔴 **없던 것은 「칸」이 아니라 「통로」였다.** DB 칸은 10/25 실측에서
    //   이미 있었고(`local_company.logo_url`·`seal_url`·`header_url`) 저장 배선도 있었다.
    //   화면 단추가 **파일을 서버로 안 보냈다** — 파일 이름만 화면에 적고
    //   *"(서버 업로드는 Phase 2)"* 를 띄웠다. 이 문이 그 Phase 2 다.
    //
    // 🔴 그리고 **워터마크의 재료가 로고**다(사장님 오더 10/25 ④). 이 문이 없으면
    //   워터마크는 켜도 아무 일이 안 일어나는 칸이 된다.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>회사 그림 올리기</b> — 로고 · 직인 · 출력 머리.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>저장하지 않는다</b> — 그림만 보관하고 경로를 돌려준다. 회사 정보에 적는 것은
    ///   고객이 <b>저장 단추를 누를 때</b>다. 올리기만 하고 창을 닫으면 회사 정보는 그대로다.
    /// 🔴 <c>tenant_id</c> 는 <b>JWT 클레임에서만</b>(#2) — 파라미터로 받지 않는다.
    /// 🔴 종류(<c>kind</c>)는 <b>보관소가 다시 본다</b> — 바깥에서 온 글자를 파일 이름에
    ///   그냥 쓰지 않는다. 화면 검사는 편의일 뿐이다.
    /// </remarks>
    [HttpPost("company-image")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequestSizeLimit(CompanyImagePaths.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadCompanyImage(
        [FromForm] string kind, IFormFile? file,
        [FromServices] ICompanyImageStore store, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (file is null || file.Length <= 0)
            return BadRequest(new { message = "올릴 그림을 골라 주십시오." });

        await using var stream = file.OpenReadStream();
        var (storedPath, rejection) = await store
            .SaveAsync(tenantId, kind, file.FileName, stream, ct).ConfigureAwait(false);

        if (rejection is not null)
        {
            // 🔴 거절은 사고가 아니다 — 까닭을 **사람 말로** 돌려준다.
            _logger.LogWarning("회사 그림 거절: tenant={Tenant} 종류={Kind} 까닭={Why}",
                tenantId, kind, rejection);
            return BadRequest(new { message = rejection });
        }

        return Ok(new { storedPath });
    }

    /// <summary>
    /// 올린 회사 그림을 지운다. 🔴 <b>이 테넌트 폴더 안</b>이 아니면 지우지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 회사 정보의 칸을 비우는 것은 <b>고객이 저장할 때</b>다 — 여기서는 파일만 지운다.
    /// </remarks>
    [HttpDelete("company-image")]
    [Authorize(Policy = "TenantAdminOnly")]
    public IActionResult DeleteCompanyImage(
        [FromBody] DeleteCompanyImageRequest req,
        [FromServices] ICompanyImageStore store)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (string.IsNullOrWhiteSpace(req?.StoredPath))
            return BadRequest(new { message = "지울 그림을 찾을 수 없습니다." });

        var rejection = store.Delete(tenantId, req.StoredPath);
        if (rejection is not null)
        {
            _logger.LogWarning("회사 그림 지우기 거절: tenant={Tenant} 까닭={Why}", tenantId, rejection);
            return BadRequest(new { message = rejection });
        }

        return NoContent();
    }
}

/// <summary>회사 그림 지우기 요청 — 🔴 경로는 <b>보관소가 다시 본다</b>.</summary>
public sealed class DeleteCompanyImageRequest
{
    /// <summary>지울 그림의 보관 경로.</summary>
    public string? StoredPath { get; set; }
}
