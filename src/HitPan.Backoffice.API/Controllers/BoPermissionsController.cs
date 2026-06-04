using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.API.Controllers;

// 백오피스 권한 관리 API — 사장님 본인만 (OwnerOnly)
//
// 사장님 결재 2026-06-04 — 헌법 #11 정합 (권한은 어드민이 직접 설정)
//
// GET  /api/backoffice/permissions          (전체 목록)
// PUT  /api/backoffice/permissions/{key}    (허용 역할 갱신)
[ApiController]
[Route("api/backoffice/permissions")]
[Authorize]
public class BoPermissionsController : ControllerBase
{
    private readonly IBoPermissionService _svc;
    private readonly ILogger<BoPermissionsController> _logger;

    public BoPermissionsController(IBoPermissionService svc, ILogger<BoPermissionsController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!IsOwner())
            return StatusCode(403, new { success = false, message = "사장님(Owner)만 권한 관리에 접근할 수 있습니다." });

        var items = await _svc.ListAsync(ct);
        return Ok(new { success = true, items });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!IsOwner())
            return StatusCode(403, new { success = false, message = "사장님(Owner)만 권한 관리에 접근할 수 있습니다." });

        if (req is null || req.AllowedRoles is null || req.AllowedRoles.Count == 0)
            return BadRequest(new { success = false, message = "허용 역할을 1개 이상 선택해주세요." });

        // 유효 역할만 통과 (잘못된 값 차단)
        var valid = new[] { "owner", "platform_owner", "platform_admin", "platform_manager", "platform_staff" };
        var filtered = req.AllowedRoles
            .Where(r => valid.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Select(r => r.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (filtered.Count == 0)
            return BadRequest(new { success = false, message = "유효한 역할이 없습니다." });

        // owner는 항상 포함 (잠금 사고 방지)
        if (!filtered.Contains("owner"))
            filtered.Insert(0, "owner");

        var csv = string.Join(",", filtered);
        var updatedBy = User.FindFirst("sub")?.Value ?? "owner";

        try
        {
            var affected = await _svc.UpdateAllowedRolesAsync(key, csv, updatedBy, ct);
            if (affected == 0)
                return NotFound(new { success = false, message = "권한을 찾을 수 없습니다." });
            return Ok(new { success = true, message = "권한이 갱신되었습니다.", allowedRoles = filtered });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BoPermissions] 갱신 실패 key={Key}", key);
            return StatusCode(500, new { success = false, message = "권한 갱신 중 오류가 발생했습니다." });
        }
    }

    private bool IsOwner() =>
        string.Equals(User.FindFirst("role")?.Value, "owner", StringComparison.OrdinalIgnoreCase);

    public class UpdateRequest
    {
        public List<string>? AllowedRoles { get; set; }
    }
}
