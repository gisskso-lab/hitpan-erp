using HitPan.API.Authorization;
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
    [RequirePermission("APPROVAL", "view")]
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
    [RequirePermission("APPROVAL", "view")]
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
    [RequirePermission("APPROVAL", "create")]
    public async Task<IActionResult> CreateApproval([FromBody] CreateApprovalRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 봉합 (2026-06-21, A-P0-1): requester_id 는 employee_id 체계여야 결재선(approver_id=employee_id)·
        //   대기/완료 매칭과 정합한다. 종전엔 user_id 를 저장해 결재선 결재자와 영영 불일치했다(헌법 #20).
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        var userName = HttpContext.Items["UserName"]?.ToString() ?? "Unknown";
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        var id = await _approvalService.CreateApprovalAsync(request, tenantId, employeeId, userName, ct);
        return Created($"/api/approval/documents/{id}", new { id });
    }

    /// <summary>결재 대기 목록 (내가 처리해야 할 결재)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("pending")]
    [RequirePermission("APPROVAL", "view")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 봉합 (2026-06-21, A-P0-1): 결재 매칭은 employee_id 체계. user_id 를 넘기면 대기함이 빈 목록이 된다.
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetPendingAsync(tenantId, employeeId, ct));
    }

    /// <summary>내가 보낸 결재 목록</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("sent")]
    [RequirePermission("APPROVAL", "view")]
    public async Task<IActionResult> GetSent(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 봉합 (2026-06-21, A-P0-1): 기안 목록은 requester_id(=employee_id) 매칭.
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetSentAsync(tenantId, employeeId, ct));
    }

    /// <summary>완료된 결재 목록 (내가 결재한 건)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("completed")]
    [RequirePermission("APPROVAL", "view")]
    public async Task<IActionResult> GetCompleted(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 봉합 (2026-06-21, A-P0-1): 완료 목록은 approval_history.approver_id(=employee_id) 매칭.
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        return Ok(await _approvalService.GetCompletedAsync(tenantId, employeeId, ct));
    }

    /// <summary>결재 상세 (문서 + 이력 + 라인)</summary>
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("documents/{approvalId}")]
    [RequirePermission("APPROVAL", "view")]
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
    [RequirePermission("APPROVAL", "update")]
    public async Task<IActionResult> Process(string approvalId, [FromBody] ProcessApprovalRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 봉합 (2026-06-21, A-P0-1): 결재 권한 체크(approver_id=employee_id)·이력 기록 모두 employee_id 체계.
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        var employeeName = HttpContext.Items["UserName"]?.ToString() ?? "Unknown";
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();
        await _approvalService.ProcessAsync(approvalId, request, tenantId, employeeId, employeeName, ct);
        return Ok(new { message = request.Action == "approved" ? "승인되었습니다." : "반려되었습니다." });
    }
}
