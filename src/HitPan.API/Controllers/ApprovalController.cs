using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>결재 API — 설정·라인·문서·이력 관리</summary>
[ApiController]
[Route("api/approval")]
[Authorize(Policy = "TenantOnly")]
public class ApprovalController : ControllerBase
{
    private readonly IApprovalService _approvalService;

    public ApprovalController(IApprovalService approvalService)
    {
        _approvalService = approvalService;
    }

    // ── 결재 설정 ──

    /// <summary>전체 결재 설정 조회</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        return Ok(await _approvalService.GetSettingsAsync(tenantId, ct));
    }

    /// <summary>결재 설정 저장 (문서유형별 — 관리자 전용)</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("settings")]
    public async Task<IActionResult> SaveSetting([FromBody] SaveApprovalSettingRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        await _approvalService.SaveSettingAsync(request, tenantId, userId, ct);
        return Ok(new { message = "저장되었습니다." });
    }

    // ── 결재 라인 ──

    /// <summary>문서유형별 결재 라인 조회</summary>
    [HttpGet("lines/{docType}")]
    public async Task<IActionResult> GetLines(string docType, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        return Ok(await _approvalService.GetLinesAsync(tenantId, docType, ct));
    }

    /// <summary>결재 라인 일괄 저장 (관리자 전용)</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("lines")]
    public async Task<IActionResult> SaveLines([FromBody] SaveApprovalLinesRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _approvalService.SaveLinesAsync(request, tenantId, ct);
        return Ok(new { message = "저장되었습니다." });
    }

    // ── 결재 문서 ──

    // ── 결재 문서 (일반 사용자 접근 가능) ──

    /// <summary>결재 요청 생성</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpPost("documents")]
    public async Task<IActionResult> CreateApproval([FromBody] CreateApprovalRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        var userName = HttpContext.Items["UserName"]?.ToString() ?? "Unknown";
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        var id = await _approvalService.CreateApprovalAsync(request, tenantId, userId, userName, ct);
        return Created($"/api/approval/documents/{id}", new { id });
    }

    /// <summary>결재 대기 목록 (내가 처리해야 할 결재)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetPendingAsync(tenantId, employeeId, ct));
    }

    /// <summary>내가 보낸 결재 목록</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("sent")]
    public async Task<IActionResult> GetSent(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetSentAsync(tenantId, employeeId, ct));
    }

    /// <summary>완료된 결재 목록 (내가 결재한 건)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("completed")]
    public async Task<IActionResult> GetCompleted(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetCompletedAsync(tenantId, employeeId, ct));
    }

    /// <summary>결재 상세 (문서 + 이력 + 라인)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("documents/{approvalId}")]
    public async Task<IActionResult> GetDetail(string approvalId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var detail = await _approvalService.GetDetailAsync(approvalId, tenantId, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    /// <summary>결재 처리 (승인/반려)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpPost("documents/{approvalId}/process")]
    public async Task<IActionResult> Process(string approvalId, [FromBody] ProcessApprovalRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["UserId"]?.ToString();
        var employeeName = HttpContext.Items["UserName"]?.ToString() ?? "Unknown";
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        await _approvalService.ProcessAsync(approvalId, request, tenantId, employeeId, employeeName, ct);
        return Ok(new { message = request.Action == "approved" ? "승인되었습니다." : "반려되었습니다." });
    }
}
