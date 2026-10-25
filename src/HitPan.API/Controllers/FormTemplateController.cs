using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 양식정보설정 API (사장님 작업지시 2026-05-31 작지②·③)
[ApiController]
[Route("api/settings/form-templates")]
[Authorize]
public class FormTemplateController : ControllerBase
{
    private readonly IFormTemplateService _service;
    private readonly ILogger<FormTemplateController> _logger;

    public FormTemplateController(IFormTemplateService service, ILogger<FormTemplateController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? formType, [FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _service.ListAsync(tenantId, formType, activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("default/{formType}")]
    public async Task<IActionResult> GetDefault(string formType, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetDefaultAsync(tenantId, formType, ct);
        if (dto is null) return NotFound(new { error = $"기본 양식이 없습니다. ({formType})" });
        return Ok(dto);
    }

    [HttpPost]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Create([FromBody] CreateFormTemplateRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var dto = await _service.CreateAsync(tenantId, request, ct);
            return Created($"/api/settings/form-templates/{dto.TemplateId}", dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "FormTemplate create rejected: tenant={Tenant}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{templateId}")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Update(string templateId, [FromBody] UpdateFormTemplateRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var dto = await _service.UpdateAsync(tenantId, templateId, request, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "FormTemplate update failed: tenant={Tenant} template={Template}", tenantId, templateId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{templateId}")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Deactivate(string templateId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.DeactivateAsync(tenantId, templateId, ct);
        return NoContent();
    }

    [HttpPost("seed-defaults")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> SeedDefaults(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.SeedDefaultsAsync(tenantId, ct);
        return Ok(new { message = "기본 양식이 만들어졌습니다." });
    }

    /// <summary>
    /// 🔴 양식 미리보기 — <b>저장 전 값</b>으로 <b>실제 인쇄 통로</b>를 타서 PDF 를 낸다.
    /// 사장님 오더 2026-10-21 · 2026-10-24.
    /// </summary>
    /// <remarks>
    /// 사장님: <i>"미리보기가 되야, 양식조정이 의미가 있지"</i> ·
    ///   <i>"실제 출력물 양식을 편집하는 대로 보여주는 게 중요함"</i> ·
    ///   <i>"미리보기-&gt; 인쇄까지 모두 양식지 적용이 되도록 해야지"</i>
    /// <para>
    /// 🔴 <b>전표 id 를 받지 않는다 — 일부러다.</b> <c>/api/email/preview-pdf</c> 는
    ///   건별 소유 확인이 없다([1-V] §2-4). 설정 화면에 단추를 달면서 그 문을 쓰면
    ///   <b>그 구멍에 실효가 생긴다</b>(§11-3). 이 문은 <b>예시 자료만</b> 그리므로
    ///   남의 전표가 나갈 길이 **원리적으로 없다.**
    /// </para>
    /// <para>
    /// 🔴 <c>tenant_id</c> 는 <b>JWT 클레임에서만</b> 온다 — 본문으로 받지 않는다(#2).
    /// 🔴 양식을 고치는 사람만 미리볼 수 있다 ⇒ 수정 권한과 <b>같은 역할</b>로 묶는다.
    /// 🔴 미리보기 전용 작성기는 <b>만들지 않았다</b> — 정본 통로를 그대로 탄다.
    ///   두 벌이 되면 미리보기와 인쇄물이 조용히 어긋난다(20261021작 §11-3).
    /// </para>
    /// </remarks>
    [HttpPost("preview-pdf")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> PreviewPdf([FromBody] FormTemplatePreviewRequest req,
        [FromServices] IPdfRenderService pdf, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        if (req?.Template is null) return BadRequest(new { message = "미리볼 양식값이 없습니다." });

        // form_type → documentType. 🔴 변환표가 작성기 안에 숨어 있어 여기서 뒤집는다.
        //   이름 체계가 3가지인 것은 등재만 하고 넓히지 않는다([1-V] E-6).
        var documentType = FormTypeToDocumentType(req.Template.FormType);
        if (documentType is null)
        {
            return BadRequest(new { message = $"미리보기를 지원하지 않는 양식입니다: {req.Template.FormType}" });
        }

        var overrides = new HitPan.Application.DTOs.Documents.PdfPreviewOverrides
        {
            Template = req.Template,
            // 🔴 양식정보설정의 미리보기다 — 스캔한 양식지 위에 필드를 mm 로 맞추는 화면이므로
            //   배경 그림을 깐다(§13-8). 전표 인쇄 미리보기는 이 값이 false 라 안 깔린다.
            IsFormDesignPreview = true,
            UseSampleData = true,
            SampleLineCount = req.LineCount <= 0 ? 3 : req.LineCount,
            MarkAsSample = true
        };

        var (bytes, _) = await pdf.RenderDocumentAsync(tenantId, documentType, string.Empty, overrides, ct)
            .ConfigureAwait(false);

        // 🔴 파일 이름에 「예시」를 넣는다 — 받아 두고 진짜로 쓰면 안 된다.
        return File(bytes, "application/pdf", "양식미리보기_예시.pdf");
    }

    /// <summary>
    /// 🔴 <b>양식지 그림 올리기</b> — 고객이 쓰는 양식용지를 그림 파일로 받는다.
    /// </summary>
    /// <remarks>
    /// 사장님 2026-10-24: <i>"이미지파일로 양식지 추가시, PDF편집기로 즉시 반영될 수 있도록 설계, 구현해"</i>
    ///
    /// 🔴 <b>왜 이 문이 없었나</b> — 10/24 에 작성기가 양식지 그림을 종이에 깔 수 있게 됐는데
    ///   <b>그림을 넣을 입구가 없었다.</b> 편집기에는 주소를 손으로 적는 글자 칸뿐이고
    ///   바깥 주소는 작성기가 거절한다(인쇄가 남의 서버에 묶이면 고객 장사가 멈춘다).
    ///   ⇒ 고객이 쓸 수 있는 길이 사실상 0 이었다. 이 문이 그 입구다.
    ///
    /// 🔴 <b>저장하지 않는다</b> — 그림만 보관하고 돌려준다. 양식 행에 적는 것은
    ///   고객이 저장 단추를 누를 때다. 올리기만 하고 창을 닫으면 양식은 그대로다.
    /// 🔴 <c>tenant_id</c> 는 <b>JWT 클레임에서만</b>(#2) · 양식을 고치는 역할과 같은 문.
    /// </remarks>
    [HttpPost("background")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    [RequestSizeLimit(FormBackgroundPaths.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadBackground(IFormFile? file,
        [FromServices] IFormBackgroundStore store, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (file is null || file.Length <= 0)
            return BadRequest(new { message = "올릴 그림을 골라 주십시오." });

        await using var stream = file.OpenReadStream();
        var (storedPath, rejection) = await store
            .SaveAsync(tenantId, file.FileName, stream, ct).ConfigureAwait(false);

        if (rejection is not null)
        {
            // 🔴 거절은 사고가 아니다 — 까닭을 **사람 말로** 돌려준다.
            _logger.LogWarning("양식지 그림 거절: tenant={Tenant} 까닭={Why}", tenantId, rejection);
            return BadRequest(new { message = rejection });
        }

        return Ok(new { storedPath });
    }

    /// <summary>
    /// 올린 양식지 그림을 지운다. 🔴 <b>이 테넌트 폴더 안</b>이 아니면 지우지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 양식 행의 칸을 비우는 것은 <b>고객이 저장할 때</b>다 — 여기서는 파일만 지운다.
    /// </remarks>
    [HttpDelete("background")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public IActionResult DeleteBackground([FromBody] DeleteFormBackgroundRequest req,
        [FromServices] IFormBackgroundStore store)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (string.IsNullOrWhiteSpace(req?.StoredPath))
            return BadRequest(new { message = "지울 그림을 찾을 수 없습니다." });

        var rejection = store.Delete(tenantId, req.StoredPath);
        if (rejection is not null)
        {
            _logger.LogWarning("양식지 그림 지우기 거절: tenant={Tenant} 까닭={Why}", tenantId, rejection);
            return BadRequest(new { message = rejection });
        }

        return NoContent();
    }

    /// <summary>
    /// <c>form_type</c> → <c>documentType</c>. 🔴 급여명세서는 <b>일부러 빠져 있다</b> —
    /// 그 길은 본인 확인이 있는 전용 문으로만 간다(W1 사고 자리).
    /// </summary>
    private static string? FormTypeToDocumentType(string formType) => formType switch
    {
        "estimate" => "quotation",
        "sales_order" => "sales_order",
        "delivery" => "delivery",
        "purchase_order" => "purchase_order",
        "receipt" => "purchase_receipt",
        "purchase_return" => "purchase_return",
        "sales_return" => "sales_return",
        "tax_invoice" => "tax_invoice",
        "invoice_exempt" => "invoice_exempt",
        "payment_receipt" => "payment_receipt",
        _ => null
    };
}

/// <summary>양식 미리보기 요청 — 저장 전 양식값 + 품목 줄 수.</summary>
public class FormTemplatePreviewRequest
{
    /// <summary>화면에 지금 적혀 있는 값(저장 전). 🔴 이것이 「편집하는 대로」의 실체다.</summary>
    public FormTemplateDto? Template { get; set; }

    /// <summary>
    /// 예시 품목 줄 수. 🔴 「몇 줄부터 두 쪽이 되나」를 <b>눈으로</b> 보는 것이
    /// 이 화면의 목적이다(§11-3).
    /// </summary>
    public int LineCount { get; set; } = 3;
}

/// <summary>올린 양식지 그림을 지우는 요청.</summary>
public class DeleteFormBackgroundRequest
{
    /// <summary>올릴 때 돌려받은 보관 경로. 🔴 테넌트 폴더 밖이면 서버가 거절한다.</summary>
    public string? StoredPath { get; set; }
}
