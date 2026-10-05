using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 🔴 2026-10-05 작4 B-1 — 「접속기기 확인」 조회 API (설계 §3 · 9/28 §4). <b>읽기만</b> — 쓰는 길 0.
/// </summary>
/// <remarks>
/// <para>· 누가: 대표계정(<c>account_type = tenant_admin</c>)만 — <c>DeviceController.GetAll</c> 과 같은 클레임 검사. 직원은 403.</para>
/// <para>· 테넌트: <c>HttpContext.Items["TenantId"]</c> 만(#2) — 쿼리·본문에 tenant 를 받지 않는다.</para>
/// <para>· 서비스: 이미 등록된 <c>ITenantDeviceService</c> 에 더한 메서드 2개(P-B) ⇒ <c>Program.cs</c> 0줄(작3 소유 파일).</para>
/// <para>· 기간: 막힌 로그인 기록 30일(사장님 E-8) — 서버가 고정한다(화면이 바꾸지 않는다).</para>
/// </remarks>
[ApiController]
[Route("api/access-status")]
[Authorize(Policy = "TenantOnly")]
public sealed class AccessStatusController : ControllerBase
{
    /// <summary>막힌 로그인 기록 보여주는 기간 — 사장님 E-8(10/5).</summary>
    public const int AlertDays = 30;

    private readonly ITenantDeviceService _svc;
    private readonly ILogger<AccessStatusController> _logger;

    // 🔴 10/5 봉합1 P2-06 — 「대표만」은 account_type 이 아니라 users.is_parent 로 가른다(JWT 에 is_parent 클레임 없음 ⇒ DB).
    private readonly IUserService? _users;

    public AccessStatusController(ITenantDeviceService svc, ILogger<AccessStatusController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    // 10/5 봉합1 P2-06 — DI 는 이 생성자. 위 생성자는 지우지 않는다(#1) — 그쪽으로 만들면 대표 판정을 못 해 늘 403(닫힌 쪽).
    [ActivatorUtilitiesConstructor]
    public AccessStatusController(ITenantDeviceService svc, IUserService users, ILogger<AccessStatusController> logger)
        : this(svc, logger)
    {
        _users = users;
    }

    /// <summary>지금 접속 중 — 사람마다 컴퓨터/휴대폰 · 마지막 사용.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        // ⬛ var deny = Gate(out var tid);   ← 10/5 봉합1 P2-06: 대표(is_parent) 판정을 더한 GateAsync
        var (deny, tid) = await GateAsync(ct);
        if (deny is not null) return deny;
        return Ok(await _svc.GetAccessStatusAsync(tid!, ct));
    }

    /// <summary>막힌 로그인 기록 — 최근 30일 · 「누가 · 언제」.</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(CancellationToken ct)
    {
        // ⬛ var deny = Gate(out var tid);   ← 10/5 봉합1 P2-06
        var (deny, tid) = await GateAsync(ct);
        if (deny is not null) return deny;
        return Ok(await _svc.GetLoginConflictAlertsAsync(tid!, AlertDays, ct));
    }

    /// <summary>
    /// 🔴 10/5 봉합1 P2-06 — 종전 <see cref="Gate"/>(tenant · 관리자 클레임) 통과 뒤 <b>DB 의 is_parent</b> 를 본다.
    /// role=TenantAdmin 직원 관리자도 account_type 이 tenant_admin 이라 종전엔 통과했다. 못 가리면 막는다(닫힌 쪽).
    /// </summary>
    private async Task<(IActionResult? Deny, string? TenantId)> GateAsync(CancellationToken ct)
    {
        var deny = Gate(out var tenantId);
        if (deny is not null) return (deny, tenantId);

        var userId = User.FindFirst("user_id")?.Value;
        if (_users is null || string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[AccessStatus] 대표 여부를 가릴 수 없어 막았다. tenant={TenantId} hasUser={HasUser}",
                tenantId, !string.IsNullOrEmpty(userId));
            return (OwnerOnly(), tenantId);
        }

        var me = await _users.GetAsync(userId, tenantId!, ct);
        if (me is null || !me.IsParent)
        {
            _logger.LogInformation("[AccessStatus] 대표가 아닌 관리자 계정의 조회를 막았다. tenant={TenantId}", tenantId);
            return (OwnerOnly(), tenantId);
        }
        return (null, tenantId);
    }

    private ObjectResult OwnerOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "이 화면은 대표 계정에서만 볼 수 있습니다." });

    /// <summary>tenant 는 Items 에서만 · 대표만. 통과하면 null.</summary>
    private IActionResult? Gate(out string? tenantId)
    {
        tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
        {
            _logger.LogInformation(
                "[AccessStatus] 대표계정이 아닌 계정의 조회를 막았다. tenant={TenantId} type={AccountType}",
                tenantId, accountType);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "이 화면은 대표 계정에서만 볼 수 있습니다." });
        }
        return null;
    }
}
