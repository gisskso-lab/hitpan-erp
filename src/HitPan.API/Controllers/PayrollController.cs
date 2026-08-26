using HitPan.API.Authorization;
using HitPan.Application.DTOs.Payroll;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HitPan.API.Controllers;

/// <summary>
/// 급여·퇴직금 API. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 <b>보호는 권한 계층이 한다</b> — 사장님(2026-08-13):
/// <i>"권한 계층분리로 급여를 관리해도 충분히 됨."</i> /
/// <i>"히트판의 계층분리는 굉장히 촘촘하게 설계되어있음"</i>
///
/// 실측하니 촘촘하다 — <c>user_permissions</c> 가 <b>메뉴별 × 5동작</b>
/// (<c>can_view · can_create · can_update · can_delete · can_export</c>) 으로 갈리고,
/// 부모계정(<c>tenant_admin</c>)은 <c>PermissionService</c> 에서 바이패스한다.
///
/// ⇒ 급여는 <c>menu_code = "PAYROLL"</c> 로 막는다. 권한 없는 직원은 <b>화면조차 못 연다.</b>
///
/// ⚠️ 처음엔 금액 칸을 암호화하려 했다가 걷어냈다. 컬럼 암호화는
/// <b>DB 파일을 훔쳐갔을 때만</b> 의미가 있고, 로그인만 하면 화면에 그대로 보이므로
/// 실제 위협(내부자 열람)을 못 막는다. 권한으로 막으면 <b>볼 사람만 본다.</b>
/// </remarks>
[ApiController]
[Route("api/payroll")]
[Authorize]
public sealed class PayrollController : ControllerBase
{
    private const string Menu = "PAYROLL";

    private readonly IPayrollService _service;
    private readonly IPermissionService _permission;
    private readonly ICurrentTenant _currentTenant;

    /// <summary>급여명세서 일괄 메일발송 — 20260826작6 W4.</summary>
    private readonly IPayslipMailService _payslipMail;

    public PayrollController(IPayrollService service, IPermissionService permission,
        ICurrentTenant currentTenant, IPayslipMailService payslipMail)
    {
        _service = service;
        _permission = permission;
        _currentTenant = currentTenant;
        _payslipMail = payslipMail;
    }

    /// <summary>
    /// 🔴 <b>남의 급여를 볼 수 있는 사람인가.</b> 사장님 지시(2026-08-13):
    /// <i>"a직원 급여를 b직원이 볼수 없어야해. 자기 급여만 볼수 있게."</i> /
    /// <i>"자기 외 직원들 급여는 부모계정인 사장, 담당자만 볼수있음."</i>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 <c>[RequirePermission]</c> 만으로는 안 되나</b> — 그 속성은 <b>메뉴 단위</b>다.
    /// 통과하면 <b>전 직원 급여</b>가 나온다. 막으면 직원이 <b>자기 명세도 못 본다.</b>
    /// 둘 다 사장님 지시와 다르다.
    ///
    /// ⇒ 세 갈래로 가른다:
    /// <list type="bullet">
    ///   <item>부모계정(사장) — 전 직원</item>
    ///   <item>급여 담당자(<c>PAYROLL</c> 권한) — 전 직원</item>
    ///   <item><b>일반 직원 — 본인 것만</b></item>
    /// </list>
    ///
    /// ⚠️ 그래서 조회 API 에는 <c>[RequirePermission]</c> 을 걸지 <b>않는다</b>.
    /// 걸면 일반 직원이 자기 명세도 못 본다. 대신 <b>여기서 판정해 범위를 좁힌다.</b>
    /// 쓰기(등록·확정·지급·취소)는 그대로 속성으로 막는다 — 남의 급여를 만들 이유가 없다.
    /// </remarks>
    private async Task<bool> CanSeeOthersAsync()
    {
        // 부모계정은 항상 본다(PermissionService 도 같은 판정을 한다 — 기준이 갈리면 안 된다).
        if (string.Equals(_currentTenant.AccountType, "tenant_admin", StringComparison.Ordinal))
            return true;

        if (string.IsNullOrEmpty(_currentTenant.UserId) || string.IsNullOrEmpty(_currentTenant.TenantId))
            return false;

        return await _permission
            .HasPermissionAsync(_currentTenant.UserId, _currentTenant.TenantId, Menu, "view")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 조회 범위를 정한다. 남의 것을 못 보는 사람은 <b>본인 id 로 덮는다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 화면이 보내온 <c>employeeId</c> 를 믿지 않는다 — 주소창에 남의 id 를 넣으면 그대로 새 나간다.
    /// </remarks>
    private async Task<(bool Allowed, string? ScopedEmployeeId)> ResolveScopeAsync(string? requested)
    {
        if (await CanSeeOthersAsync().ConfigureAwait(false))
        {
            return (true, requested);   // 전 직원 또는 요청한 한 사람
        }

        var me = CurrentEmployeeId();
        if (string.IsNullOrEmpty(me)) return (false, null);

        // 🔴 요청을 무시하고 본인으로 덮는다.
        return (true, me);
    }

    // ───────────────────────────────────────────────────────────────
    // 급여 명세
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 달 명세 목록. 🔴 <b>일반 직원은 본인 것만 나온다.</b>
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-13): <i>"a직원 급여를 b직원이 볼수 없어야해. 자기 급여만 볼수 있게."</i>
    ///
    /// ⚠️ <c>[RequirePermission]</c> 을 <b>일부러 안 걸었다.</b> 걸면 일반 직원이
    /// 자기 명세도 못 본다. 범위는 <see cref="ResolveScopeAsync"/> 가 좁힌다.
    /// </remarks>
    [HttpGet("slips")]
    public async Task<IActionResult> GetSlips([FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? employeeId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (allowed, scoped) = await ResolveScopeAsync(employeeId).ConfigureAwait(false);
        if (!allowed) return Forbid();

        var (y, m) = Normalize(year, month);
        var list = await _service.GetSlipsAsync(tenantId, y, m, scoped, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>
    /// 명세 한 건(항목 포함). 🔴 <b>남의 명세는 못 연다.</b>
    /// </summary>
    /// <remarks>
    /// 목록을 좁혀도 <b>id 를 직접 넣으면</b> 열릴 수 있다. 여기서 한 번 더 본다 —
    /// 목록만 막는 것은 막은 것이 아니다.
    /// </remarks>
    [HttpGet("slips/{slipId}")]
    public async Task<IActionResult> GetSlip(string slipId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetSlipAsync(tenantId, slipId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();

        // 🔴 남의 급여면 막는다. 본인 것이거나, 남의 것을 볼 수 있는 사람이어야 한다.
        if (!await CanSeeOthersAsync().ConfigureAwait(false)
            && dto.EmployeeId != CurrentEmployeeId())
        {
            return Forbid();
        }

        return Ok(dto);
    }

    /// <summary>
    /// 그 달 급여를 만들 때 <b>참고할 것들</b>. 🔴 자동으로 채우지 않고 보여만 준다.
    /// </summary>
    /// <remarks>
    /// 휴직자의 경우 단계6 에서 <b>사람이 정해 둔</b> 지급액을 함께 준다.
    /// 사장님: <i>"그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i> — 그 연결이 여기다.
    /// </remarks>
    /// <remarks>
    /// ⚠️ 이건 <b>전 직원 명단</b>이라 급여를 <b>만드는 사람만</b> 본다.
    /// 일반 직원에게는 남의 이름·부서·휴직 사실이 통째로 나가므로 <c>[RequirePermission]</c> 을 건다.
    /// </remarks>
    [HttpGet("context")]
    [RequirePermission(Menu, "view")]
    public async Task<IActionResult> GetContext([FromQuery] int year, [FromQuery] int month,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (y, m) = Normalize(year, month);
        var list = await _service.GetContextAsync(tenantId, y, m, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>명세 저장(신규·수정). 합계는 서버가 줄을 더해 낸다.</summary>
    [HttpPost("slips")]
    [RequirePermission(Menu, "create")]
    public async Task<IActionResult> SaveSlip([FromBody] SavePayrollSlipRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var id = await _service.SaveSlipAsync(tenantId, Actor(), request, ct).ConfigureAwait(false);
            return Ok(new { slipId = id });
        }
        catch (InvalidOperationException ex)
        {
            // "이미 있습니다" 같은 이유를 그대로 전한다.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>확정. 확정하면 못 고친다.</summary>
    [HttpPost("slips/{slipId}/confirm")]
    [RequirePermission(Menu, "update")]
    public async Task<IActionResult> ConfirmSlip(string slipId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.ConfirmSlipAsync(tenantId, Actor(), slipId, ct).ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>지급 완료. 지급일을 사람이 넣는다.</summary>
    [HttpPost("slips/{slipId}/pay")]
    [RequirePermission(Menu, "update")]
    public async Task<IActionResult> MarkPaid(string slipId, [FromBody] PayBody body,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.MarkPaidAsync(tenantId, Actor(), slipId,
                body?.PayDate ?? DateTime.Today, ct).ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>취소.</summary>
    [HttpPost("slips/{slipId}/cancel")]
    [RequirePermission(Menu, "delete")]
    public async Task<IActionResult> CancelSlip(string slipId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.CancelSlipAsync(tenantId, Actor(), slipId, ct).ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 퇴직금
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 퇴직금 목록. 🔴 <b>일반 직원은 본인 것만 나온다.</b>
    /// </summary>
    /// <remarks>
    /// 급여와 같은 규칙이다 — 남의 퇴직금이 보이면 안 된다.
    /// 퇴사자 본인이 자기 퇴직금을 확인하는 경로이기도 하다.
    /// </remarks>
    [HttpGet("severance")]
    public async Task<IActionResult> GetSeverance([FromQuery] string? employeeId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (allowed, scoped) = await ResolveScopeAsync(employeeId).ConfigureAwait(false);
        if (!allowed) return Forbid();

        var list = await _service.GetSeveranceListAsync(tenantId, scoped, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>퇴직금 저장. 🔴 금액을 사람이 넣는다.</summary>
    [HttpPost("severance")]
    [RequirePermission(Menu, "create")]
    public async Task<IActionResult> SaveSeverance([FromBody] SaveSeveranceRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var id = await _service.SaveSeveranceAsync(tenantId, Actor(), request, ct)
                .ConfigureAwait(false);
            return Ok(new { severanceId = id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>퇴직금 확정.</summary>
    [HttpPost("severance/{severanceId}/confirm")]
    [RequirePermission(Menu, "update")]
    public async Task<IActionResult> ConfirmSeverance(string severanceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.ConfirmSeveranceAsync(tenantId, Actor(), severanceId, ct)
                .ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>지급 방식·진행 단계 목록 — 화면이 고르게 한다.</summary>
    [HttpGet("codes")]
    [RequirePermission(Menu, "view")]
    public IActionResult Codes()
        => Ok(new
        {
            statuses = PayrollStatusLabels.All.Select(x => new { code = x.Key, name = x.Value }),
            lineTypes = PayrollLineTypeLabels.All.Select(x => new { code = x.Key, name = x.Value }),
            severancePayTypes = SeverancePayTypeLabels.All.Select(x => new { code = x.Key, name = x.Value })
        });

    // ───────────────────────────────────────────────────────────────

    public sealed class PayBody
    {
        public DateTime? PayDate { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  급여명세서 일괄 메일발송 — 20260826작6 W4
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 발송 전 확인 명단 — <b>누가 받고 누가 못 받는지</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>되돌릴 수 없는 발송</b>이라 경리가 <b>보고 나서</b> 누른다(사장님 반자동 원칙).
    /// 이름·수신주소·불가사유를 <b>전부</b> 준다 — 숫자만 주면 무엇을 고칠지 알 수 없다.
    /// </para>
    /// <para>
    /// ⚠️ 권한은 <c>update</c> 다. <b>조회 권한만 가진 사람에게 전 직원 급여 명단과
    /// 이메일 주소가 나가면 안 된다</b> — 이 화면은 발송 준비 화면이다.
    /// </para>
    /// </remarks>
    [HttpGet("slips/send-mail/preview")]
    [RequirePermission(Menu, "update")]
    public async Task<IActionResult> GetSendMailPreview([FromQuery] int year, [FromQuery] int month,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (y, m) = Normalize(year, month);
        return Ok(await _payslipMail.GetSendPreviewAsync(tenantId, y, m, ct));
    }

    /// <summary>
    /// 급여명세서 <b>일괄 발송</b>. 건별로 보내고 건별로 결과를 돌려준다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>화면이 보낸 목록을 그대로 믿지 않는다</b> — 서비스가 각 건을 다시 판정해서
    /// 못 보낼 것이면 안 보낸다. 화면을 우회한 요청으로 <b>미결재 명세서가 나가면 안 된다</b>(⑤결재).
    /// </para>
    /// <para>
    /// 🔴 <b>뭉뚱그린 "발송 완료" 를 주지 않는다</b> — 성공·실패를 사람별로 돌려준다.
    /// 실패분만 다시 넘기면 그대로 재발송이 된다(별도 API 가 필요 없다).
    /// </para>
    /// </remarks>
    [HttpPost("slips/send-mail")]
    [RequirePermission(Menu, "update")]
    public async Task<IActionResult> SendMail([FromBody] SendPayslipMailRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (request?.SlipIds is null || request.SlipIds.Count == 0)
            return BadRequest(new { message = "발송할 명세서를 하나 이상 골라주세요." });

        var (y, m) = Normalize(request.Year, request.Month);
        request.Year = y;
        request.Month = m;

        return Ok(await _payslipMail.SendAsync(tenantId, Actor(), request, ct));
    }

    private string Actor() => CurrentEmployeeId() ?? "unknown";

    /// <summary>
    /// 로그인한 사람의 <b>사원 id</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>user_id</c> 가 아니라 <c>employee_id</c> 다. <b>둘은 별개 GUID</b>이고
    /// AuthService 가 두 클레임을 따로 발급한다(<c>TenantMiddleware.cs:72</c> 봉합 기록).
    /// 여기서 <c>user_id</c> 를 쓰면 <b>아무 명세와도 안 맞아</b> 직원이 자기 급여를 못 보거나,
    /// 더 나쁘게는 <b>엉뚱한 사람 것이 보인다.</b>
    /// </remarks>
    private string? CurrentEmployeeId() => User.FindFirstValue("employee_id");

    /// <summary>연·월이 안 왔거나 이상하면 이번 달로 본다.</summary>
    private static (int Year, int Month) Normalize(int year, int month)
        => (year > 0 ? year : DateTime.Today.Year,
            month is >= 1 and <= 12 ? month : DateTime.Today.Month);
}
