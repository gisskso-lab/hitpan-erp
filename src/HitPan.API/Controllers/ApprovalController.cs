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

    // 작(2026-08-13) 단계9: 결재 결과를 신청자에게 메시지로 알린다.
    // 🔴 결재 로직은 손대지 않는다 — 발송 호출만 더한다(헌법 #1).
    private readonly IChatService _chatService;
    private readonly ILogger<ApprovalController> _logger;

    public ApprovalController(IApprovalService approvalService,
        IChatService chatService, ILogger<ApprovalController> logger)
    {
        _approvalService = approvalService;
        _chatService = chatService;
        _logger = logger;
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

        // 작(2026-08-13) 단계9 — 다음 결재자에게 "결재 요청이 도착했습니다" 메시지.
        // 김삼성 상무: "'결재가 올라왔습니다' 알림부터 붙이세요. 그러면 직원이 메신저를 켤 이유가 생깁니다."
        await NotifyNextApproverAsync(id, tenantId, employeeId, ct);

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

        // 작(2026-08-13) 단계9 — 결재 결과를 신청자에게 메시지로 보낸다.
        await NotifyRequesterAsync(approvalId, tenantId, employeeId, request.Action, ct);

        return Ok(new { message = request.Action == "approved" ? "승인되었습니다." : "반려되었습니다." });
    }

    /// <summary>
    /// 결재 결과를 <b>최초 발신인(신청자)</b> 에게 메신저 메시지로 보낸다. 작(2026-08-13) 단계9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 사장님(2026-08-13): <i>"그룹웨어 문서 승인 혹은 반려시,
    /// 최초 발신인(신청자)에게 메시지 보내야됨"</i> / <i>"반려되었습니다. 혹은 승인되었습니다."</i>
    /// </para>
    /// <para>
    /// 🔴 <b>알림(종)이 아니라 메시지다</b> — 대화방에 남고 읽음이 찍힌다.
    /// 알림과 메시지를 <b>둘 다 보내지 않는다</b>. 같은 소식이 두 번 오면 직원이 헷갈린다.
    /// </para>
    /// <para>
    /// 🔴 <b>보내는 이는 결재한 사람</b>이다(봇 이름으로 가리지 않는다) —
    /// 반려되면 <b>그 방에서 바로 되물을 수 있어야</b> 한다.
    /// </para>
    /// <para>
    /// 🔴 <b>메시지 발송 실패가 결재를 막으면 안 된다</b>(헌법 #15 — 빈 catch 금지).
    /// 결재는 이미 저장됐는데 메시지가 안 갔다고 결재까지 되돌리면 <b>그게 더 큰 사고다.</b>
    /// </para>
    /// </remarks>
    private async Task NotifyRequesterAsync(string approvalId, string tenantId,
        string actorEmployeeId, string? action, CancellationToken ct)
    {
        try
        {
            var detail = await _approvalService.GetDetailAsync(approvalId, tenantId, ct);
            var requesterId = detail?.Document.RequesterId;
            if (string.IsNullOrEmpty(requesterId)) return;

            var docName = DocName(detail!.Document);

            var verb = string.Equals(action, "approved", StringComparison.Ordinal)
                ? "승인되었습니다." : "반려되었습니다.";

            // 사장님이 직접 쓰신 문구 그대로.
            var body = $"\"{docName}\" 가 {verb}\n클릭하시면 {docName} 결재와 연결됩니다.";

            await _chatService.SendApprovalMessageAsync(tenantId, actorEmployeeId,
                requesterId, body, "approval", approvalId, docName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "결재 결과 메시지 발송 실패 — 결재 {ApprovalId} 는 정상 처리됨", approvalId);
        }
    }

    /// <summary>
    /// 상신 직후 <b>다음 결재자</b> 에게 "결재 요청이 도착했습니다" 메시지. 작(2026-08-13) 단계9.
    /// </summary>
    /// <remarks>
    /// 김삼성 상무: <i>"'결재가 올라왔습니다' 알림부터 붙이세요.
    /// 그러면 직원이 메신저를 켤 이유가 생깁니다."</i>
    /// <para>
    /// 🔴 여기서도 <b>결재 로직을 건드리지 않는다.</b> 이미 저장된 결재선을 읽어 보내기만 한다.
    /// 실패해도 상신은 그대로다.
    /// </para>
    /// </remarks>
    private async Task NotifyNextApproverAsync(string approvalId, string tenantId,
        string requesterEmployeeId, CancellationToken ct)
    {
        try
        {
            var detail = await _approvalService.GetDetailAsync(approvalId, tenantId, ct);
            if (detail is null) return;

            // 지금 차례인 결재자. 없으면 첫 줄.
            var currentSeq = detail.Document.CurrentSeq;
            var next = detail.Lines.FirstOrDefault(l => l.SeqNo == currentSeq)
                       ?? detail.Lines.OrderBy(l => l.SeqNo).FirstOrDefault();

            // 대리 결재자가 정해져 있으면 그 사람이 받는다.
            var approverId = string.IsNullOrWhiteSpace(next?.DelegateId)
                ? next?.ApproverId : next!.DelegateId;

            if (string.IsNullOrEmpty(approverId)) return;

            var docName = DocName(detail.Document);
            var body = $"\"{docName}\" 결재 요청이 도착했습니다.\n클릭하시면 {docName} 결재와 연결됩니다.";

            await _chatService.SendApprovalMessageAsync(tenantId, requesterEmployeeId,
                approverId, body, "approval", approvalId, docName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "결재 상신 메시지 발송 실패 — 결재 {ApprovalId} 는 정상 상신됨", approvalId);
        }
    }

    /// <summary>메시지에 쓸 문서 이름. 유형 이름이 없으면 제목을 쓴다.</summary>
    private static string DocName(ApprovalDocumentDto doc)
        => !string.IsNullOrWhiteSpace(doc.DocTypeLabel) ? doc.DocTypeLabel
         : !string.IsNullOrWhiteSpace(doc.Title) ? doc.Title
         : "결재 문서";
}
