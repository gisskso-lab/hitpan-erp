using HitPan.Application.DTOs.WorkReport;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 업무보고서(일일·주간·월간·경위서) API. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// 사장님 지시(2026-08-12): <i>"일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"</i>
/// <para>
/// 🔴 <b>권한</b> — 일반 사용자는 <b>본인 보고서만</b> 보고 쓴다. 부모계정(<c>tenant_admin</c>)만
/// 전체를 본다. 사원 ID 는 <b>JWT 클레임에서만</b> 받는다 — 요청에 담긴 사원 ID 를 믿으면
/// 남의 이름으로 보고서를 쓸 수 있다(헌법 #2, 연차에서 실제로 있었던 사고).
/// </para>
/// <para>
/// ⚠️ <c>ReportController</c>(현황 리포트)와 다른 것이다. 주소도 <c>api/work-reports</c> 로 가른다.
/// </para>
/// </remarks>
[ApiController]
[Route("api/work-reports")]
[Authorize]
public sealed class WorkReportController : ControllerBase
{
    private readonly IWorkReportService _service;

    public WorkReportController(IWorkReportService service)
    {
        _service = service;
    }

    /// <summary>보고서 목록. 일반 사용자는 본인 것만, 관리자는 전체.</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? reportType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? employeeId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var (myEmployeeId, isAdmin) = Identity();

        // 🔴 조회 범위를 서버가 정한다. 일반 사용자가 employeeId 를 넘겨도 무시하고 본인 것만 준다.
        string? scope;
        if (isAdmin)
        {
            scope = string.IsNullOrWhiteSpace(employeeId) ? null : employeeId;
        }
        else
        {
            if (string.IsNullOrEmpty(myEmployeeId))
            {
                // 사원 연결이 없는 계정은 볼 보고서가 없다. 남의 것을 보여주면 안 된다.
                return Ok(new List<WorkReportListDto>());
            }
            scope = myEmployeeId;
        }

        var list = await _service.GetListAsync(tenantId, scope, reportType, from, to, ct)
            .ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>보고서 상세. 본인 것이 아니면 관리자만 볼 수 있다.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var detail = await _service.GetAsync(tenantId, id, ct).ConfigureAwait(false);
        if (detail is null)
        {
            return NotFound();
        }

        var (myEmployeeId, isAdmin) = Identity();

        // 🔴 봉합 (2026-08-13, 검증 P1-4): 본인·관리자에 더해 <b>결재자</b>도 볼 수 있다.
        //    종전엔 결재선 2단계 결재자(비관리자)가 제목만 보고 승인해야 했다 — 안 읽고 찍는 결재다.
        //    결재선 밖 사람은 여전히 못 본다(판정은 서비스가 위임까지 포함해 한다).
        if (!isAdmin && detail.EmployeeId != myEmployeeId)
        {
            var isApprover = !string.IsNullOrEmpty(myEmployeeId)
                && await _service.IsApproverAsync(tenantId, id, myEmployeeId, ct).ConfigureAwait(false);

            if (!isApprover)
            {
                return Forbid();
            }
        }

        return Ok(detail);
    }

    /// <summary>보고서를 새로 쓴다.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveWorkReportRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var (myEmployeeId, _) = Identity();
        if (string.IsNullOrEmpty(myEmployeeId))
        {
            // 사원 연결이 없으면 보고서를 쓸 수 없다. 누가 쓴 것인지 남지 않기 때문이다.
            return BadRequest(new { message = "사원 정보가 연결되지 않아 보고서를 작성할 수 없습니다. 관리자에게 문의해 주세요." });
        }

        var userName = HttpContext.Items["UserName"]?.ToString() ?? "직원";

        try
        {
            var result = await _service.CreateAsync(tenantId, myEmployeeId, userName, request, ct)
                .ConfigureAwait(false);

            // 🔴 결재가 실제로 올라갔는지 그대로 전한다(P0-1 봉합 — 되는 척 제거).
            return Ok(new
            {
                reportId = result.ReportId,
                approvalCreated = result.ApprovalCreated,
                approvalSkipReason = result.ApprovalSkipReason
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>보고서를 고친다. 작성중·반려 상태에서만 된다.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] SaveWorkReportRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var (myEmployeeId, _) = Identity();
        if (string.IsNullOrEmpty(myEmployeeId))
        {
            return Forbid();
        }

        var result = await _service.UpdateAsync(tenantId, id, myEmployeeId, request, ct)
            .ConfigureAwait(false);

        // 🔴 "왜 안 되는지" 를 알려준다. 그냥 실패라고만 하면 고객이 다시 눌러 본다.
        if (!result.Saved)
        {
            return BadRequest(new { message = "결재가 진행 중이거나 완료된 보고서는 수정할 수 없습니다." });
        }

        return Ok(new
        {
            approvalCreated = result.ApprovalCreated,
            approvalSkipReason = result.ApprovalSkipReason
        });
    }

    /// <summary>작성중 보고서를 결재에 올린다.</summary>
    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var (myEmployeeId, _) = Identity();
        if (string.IsNullOrEmpty(myEmployeeId))
        {
            return Forbid();
        }

        var userName = HttpContext.Items["UserName"]?.ToString() ?? "직원";

        var result = await _service.SubmitAsync(tenantId, id, myEmployeeId, userName, ct)
            .ConfigureAwait(false);

        if (!result.Saved)
        {
            return BadRequest(new { message = "이미 결재가 진행 중이거나 완료된 보고서입니다." });
        }

        return Ok(new
        {
            approvalCreated = result.ApprovalCreated,
            approvalSkipReason = result.ApprovalSkipReason
        });
    }

    /// <summary>작성중 보고서를 지운다.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var (myEmployeeId, _) = Identity();
        if (string.IsNullOrEmpty(myEmployeeId))
        {
            return Forbid();
        }

        var ok = await _service.DeleteAsync(tenantId, id, myEmployeeId, ct).ConfigureAwait(false);

        return ok
            ? Ok()
            : BadRequest(new { message = "결재에 올라간 보고서는 삭제할 수 없습니다." });
    }

    /// <summary>요청자의 사원 ID 와 관리자 여부.</summary>
    /// <remarks>
    /// 🔴 <c>employee_id</c> 는 <c>user_id</c> 와 별개 GUID 다(6/21 A-P0-1 봉합 참조).
    /// 결재 체계가 전부 employee_id 이므로 여기서도 그것을 쓴다.
    /// ERP 관리자는 부모계정(<c>tenant_admin</c>) 뿐이다(헌법 #38).
    /// </remarks>
    private (string? EmployeeId, bool IsAdmin) Identity()
    {
        var employeeId = User.FindFirst("employee_id")?.Value;
        var accountType = HttpContext.Items["AccountType"]?.ToString();
        return (employeeId, accountType is "tenant_admin");
    }
}
