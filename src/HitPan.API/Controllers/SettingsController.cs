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

    /// <summary>
    /// 인쇄 공통 설정을 읽는다 (DB-148 · 결-I).
    /// </summary>
    /// <remarks>🔴 행이 없으면 <b>제안값</b>이 온다 — 화면이 빈 칸을 보여 주지 않게 한다.</remarks>
    [HttpGet("print-common")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetPrintCommon(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _settingsService.GetPrintCommonAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    /// <summary>
    /// 인쇄 공통 설정을 저장한다 (DB-148 · 결-I).
    /// </summary>
    /// <remarks>
    /// 🔴 이 값은 <b>양식마다의 기본</b>이다. 이미 만들어 둔 양식의 종이는 바꾸지 않는다
    ///   — 한 칸을 켜서 고객의 모든 종이가 한꺼번에 바뀌는 길은 만들지 않는다(#1).
    /// </remarks>
    [HttpPut("print-common")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> SavePrintCommon(
        [FromBody] PrintCommonSettingsDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _settingsService.SavePrintCommonAsync(tenantId, dto, ct).ConfigureAwait(false);
        _logger.LogInformation("인쇄 공통 설정 저장: tenant={Tenant}", tenantId);
        return NoContent();
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
    /// 🔴🔴 <b>올리는 걸음에 회사 정보 칸까지 적는다</b> — 이 화면에는 그림만 위한 「저장」
    ///   단추가 없다. 올리기에서 끝나야 「등록되었습니다」가 <b>참</b>이 된다.
    ///   ⇒ 🔴 <b>올린 뒤 창을 닫아도 로고는 바뀐 채로 남는다.</b> 되돌리려면 「지우기」를 누르거나
    ///     다른 그림을 다시 올려야 한다. 화면 글도 그렇게 적혀 있어야 한다.
    ///   🔴 [2026-10-26 [3-V] P2-8 ① 봉합] 종전 이 자리에는 *"저장하지 않는다 — 회사 정보에
    ///     적는 것은 고객이 저장 단추를 누를 때다"* 라고 적혀 있었다. <b>같은 메서드 안에서
    ///     두 주석이 반대였다.</b> 코드가 하는 일로 고쳤다.
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

        // 🔴🔴 [§B-4] **보관만 하고 끝내지 않는다 — 회사 정보 칸에 경로까지 적는다.**
        //   이 화면에는 「저장」 단추가 없다. 올리는 걸음에 칸까지 적어야
        //   「등록되었습니다」가 참이 된다. 10/25 사고가 바로 이 자리였다:
        //   화면은 「등록되었습니다」라 말하고 서버는 null 을 저장했다.
        var written = await _settingsService
            .SaveCompanyImagePathAsync(tenantId, kind, storedPath, ct).ConfigureAwait(false);
        if (!written)
        {
            _logger.LogWarning("회사 그림 종류를 알 수 없어 칸에 적지 않았다: {Kind}", kind);
            return BadRequest(new { message = "그림 종류를 확인할 수 없습니다." });
        }

        return Ok(new { storedPath });
    }

    /// <summary>
    /// 🔴 회사 그림이 <b>인쇄에 쓰일 수 있는 상태인가</b> — 종류마다 한 낱말로 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴🔴 [2026-10-26 [4] 중 R-6 봉합] *"켰는데 안 보인다"* 의 까닭 일곱 갈래가
    ///   <b>전부 서버 로그 한 줄로 끝나고 화면은 평소처럼 나왔다.</b> 가장 흔한 두 갈래가
    ///   ① 로고를 안 올렸다 ② 올린 로고가 <b>보관소 밖 옛 경로</b>다(기존 고객의 <c>logo_url</c>).
    ///   ②는 화면에 「등록됨」으로 보이는데 작성기는 울타리에서 막아 영영 안 쓴다.
    /// 🔴 <b>판정은 서버가 한다</b> — 울타리가 어디인지는 화면이 모른다(알아서도 안 된다).
    ///   <b>경로는 내보내지 않는다.</b>
    ///
    /// 🔴🔴 돌려주는 낱말은 <b>네 개</b>다 — <c>present</c>(쓸 수 있다) · <c>missing</c>(칸이 비었다) ·
    ///   <c>unusable</c>(칸은 있는데 못 쓴다) · <c>unknown</c>(<b>재지 못했다</b>).
    ///   [2026-10-26 재검증 V-4 봉합] 종전 주석은 *"세 낱말뿐"* 이라 적었는데
    ///   아래 <c>catch</c> 가 <c>unknown</c> 을 돌려준다 ⇒ <b>주석이 거짓</b>이었고,
    ///   두 화면도 그 낱말을 <b>말없이 삼켜</b> 고객은 아무 말도 못 들었다.
    ///   🔴 <c>unknown</c> 은 「괜찮다」가 아니다 — <b>모른다</b>는 뜻이고 화면이 그렇게 말해야 한다.
    /// </remarks>
    [HttpGet("company-image-state")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetCompanyImageState(CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in CompanyImagePaths.Kinds)
        {
            var path = await _settingsService
                .GetCompanyImagePathAsync(tenantId, kind, ct).ConfigureAwait(false);
            state[kind] = DescribeCompanyImage(tenantId, path);
        }

        return Ok(state);
    }

    /// <summary>그 그림이 인쇄에 쓰일 수 있나 — 🔴 작성기의 울타리와 <b>같은 기준</b>으로 본다.</summary>
    private string DescribeCompanyImage(string tenantId, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "missing";

        try
        {
            var root = Path.GetFullPath(CompanyImagePaths.TenantRoot(tenantId))
                           .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);

            // 🔴 울타리 밖이거나 파일이 없으면 **등록돼 보여도 안 쓰인다** — 같은 안내를 준다.
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "unusable";
            // 🔴 `File` 은 컨트롤러에서 `ControllerBase.File(...)` 과 겹친다 — 전체 이름으로 적는다.
            return System.IO.File.Exists(full) ? "present" : "unusable";
        }
        catch (Exception ex)
        {
            // 🔴 빈 catch 금지(#15). 모르면 **모른다고** 돌려준다 — 화면이 단정하지 않게.
            _logger.LogWarning(ex, "회사 그림 상태를 재지 못했다: tenant={Tenant}", tenantId);
            return "unknown";
        }
    }

    /// <summary>
    /// 올린 회사 그림을 지운다. 🔴 <b>이 테넌트 폴더 안</b>이 아니면 지우지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 파일을 지우고 <b>회사 정보 칸도 같이 비운다</b> — 올리기와 대칭이다.
    ///
    /// 🔴🔴 [2026-10-26 [3-V] P2-5 봉합] 종전에는 <b>요청에 적힌 경로</b>를 지우고
    ///   <see cref="ISettingsService.SaveCompanyImagePathAsync"/> 의 <b>반환값을 버렸다.</b>
    ///   그래서 종류와 경로를 엇갈리게 보내면
    ///   ① 모르는 종류(<c>logoo</c>)인데 <b>파일은 지워지고</b> 칸은 남거나,
    ///   ② <c>seal</c> 로 보내 <b>로고 파일이 지워지고 직인 칸이 비는</b> 길이 있었다.
    ///   둘 다 API 를 직접 부르는 길이고, 화면은 「지웠습니다」라 말했다.
    ///   ⇒ 🔴 <b>지울 대상을 요청에서 받지 않고 DB 에서 읽는다.</b> 그러면 두 사고가
    ///     <b>구조적으로 불가능</b>해진다 — 울타리를 「그럴 리 없다」로 세우지 않는다.
    /// </remarks>
    [HttpDelete("company-image")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> DeleteCompanyImage(
        [FromBody] DeleteCompanyImageRequest req,
        [FromServices] ICompanyImageStore store, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        // 🔴 종류를 **보관소와 같은 자리**로 먼저 거른다 — 「빈 값 아님」만 보면 모르는 종류가 통과한다.
        if (!CompanyImagePaths.IsKnownKind(req?.Kind))
            return BadRequest(new { message = "그림 종류를 확인할 수 없습니다." });

        // 🔴 지울 경로는 **DB 가 가리키는 그 칸**에서 읽는다(요청의 StoredPath 는 믿지 않는다).
        var stored = await _settingsService
            .GetCompanyImagePathAsync(tenantId, req!.Kind!, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(stored))
        {
            // 🔴 이미 비어 있다 — 사고가 아니다. 화면이 「지웠습니다」를 말해도 참이 된다.
            _logger.LogInformation("회사 그림 지우기 — 이미 비어 있다: tenant={Tenant} 종류={Kind}",
                tenantId, req.Kind);
            return NoContent();
        }

        var rejection = store.Delete(tenantId, stored);
        if (rejection is not null)
        {
            _logger.LogWarning("회사 그림 지우기 거절: tenant={Tenant} 까닭={Why}", tenantId, rejection);
            return BadRequest(new { message = rejection });
        }

        // 🔴 파일을 지웠으면 **칸도 비운다** — 안 비우면 작성기가 없는 파일을 가리켜
        //   로그에 「파일이 없다」가 쌓인다. 화면도 「등록됨」으로 남아 거짓이 된다.
        //   🔴 반환값을 **버리지 않는다** — 못 적었으면 화면에 참을 말해야 한다.
        var cleared = await _settingsService
            .SaveCompanyImagePathAsync(tenantId, req.Kind!, null, ct).ConfigureAwait(false);
        if (!cleared)
        {
            _logger.LogWarning("회사 그림을 지웠으나 칸을 비우지 못했다: tenant={Tenant} 종류={Kind}",
                tenantId, req.Kind);
            return BadRequest(new { message = "그림은 지웠지만 회사 정보를 갱신하지 못했습니다. 화면을 새로 고쳐 주십시오." });
        }

        return NoContent();
    }
}

/// <summary>회사 그림 지우기 요청 — 🔴 지울 <b>대상은 종류로만</b> 가린다.</summary>
public sealed class DeleteCompanyImageRequest
{
    /// <summary>
    /// 화면이 보던 경로 — 🔴 <b>참고용이다. 서버는 이 값으로 지우지 않는다</b>
    /// ([3-V] P2-5 봉합 · 지울 대상은 DB 칸에서 읽는다).
    /// </summary>
    public string? StoredPath { get; set; }

    /// <summary>그림 종류 — <c>logo</c>·<c>seal</c>·<c>header</c>. 🔴 칸을 비울 때 어느 칸인지 알아야 한다.</summary>
    public string? Kind { get; set; }
}
