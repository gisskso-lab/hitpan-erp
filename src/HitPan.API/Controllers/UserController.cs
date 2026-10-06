using ClosedXML.Excel;
using HitPan.API.Authorization;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    // ⬛ [RequirePermission("USERS", "view")]  ← 20261005작5 V5-04 주소마다 판정 하나(§5-2 표 · 개발명세서 단계표)
    [RequireUsersLevel(1)]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _userService.GetListAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{id}")]
    // ⬛ [RequirePermission("USERS", "view")]  ← 20261005작5 V5-04
    [RequireUsersLevel(1)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _userService.GetAsync(id, tenantId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "create")]  ← 20261005작5 V5-04 · 「새 사원과 함께」는 대표·관리자만(V5-06 ①)
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 🔴 20261005작5 V5-06 ② — 같은 이름의 재직·미등록 사원이 있으면 쌍둥이 사원 의심 ⇒ 409 + 후보.
        //   「다른 사람이다」(ConfirmDifferentPerson=true)로 다시 보낼 때만 새 사원과 함께 만든다(동명이인 허용).
        if (!dto.ConfirmDifferentPerson)
        {
            var name = string.IsNullOrWhiteSpace(dto.EmpName) ? dto.UserName : dto.EmpName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var candidates = await _userService.ListLinkableEmployeesAsync(tenantId, name, false, ct).ConfigureAwait(false);
                if (candidates.Count > 0)
                {
                    return Conflict(new
                    {
                        code = "same_name_employee",
                        message = $"같은 이름의 사원 {name.Trim()}(이)가 이미 있습니다. 그 사원에 계정을 연결할까요?",
                        candidates
                    });
                }
            }
        }

        try
        {
            var id = await _userService.CreateAsync(dto, tenantId, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (AccountSeatFullException ex)
        {
            // 20261005작3 — 한도 초과는 InvalidOperationException 보다 먼저 잡는다(설계 §4).
            return SeatFull(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "update")]  ← 20261005작5 V5-04 · 수정은 대표·관리자만(P-3)
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // ⬛ await _userService.UpdateAsync(id, dto, tenantId, ct).ConfigureAwait(false);
        // ⬛ return Ok();
        // 20261005작3 E-4 — 0→1 이 차면 409 · 대표를 끄는 요청은 400(반증 F3)
        try
        {
            await _userService.UpdateAsync(id, dto, tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (AccountSeatFullException ex)
        {
            return SeatFull(ex);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ── 20261005작3 계정 과금 (설계 §4) — tenant 는 전부 JWT 클레임(TenantMiddleware 가 넣은 Items)만(#2) ──

    [HttpGet("seats")]
    // ⬛ [RequirePermission("USERS", "view")]  ← 20261005작5 V5-04
    [RequireUsersLevel(1)]
    public async Task<IActionResult> GetSeats(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var s = await _userService.GetSeatsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(new { active = s.Active, baseLimit = s.BaseLimit, extra = s.Extra, limit = s.Limit, tier = s.Tier });
    }

    [HttpPost("{id}/suspend")]
    // ⬛ [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "update")]  ← 20261005작5 V5-04 · 2단계 이상(§5-2)
    [RequireUsersLevel(2)]
    public async Task<IActionResult> Suspend(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            // ⬛ await _userService.SuspendAsync(id, tenantId, ct).ConfigureAwait(false);
            // 20261005작5 §5-3 · P3-08 — 대표·관리자가 아니면 울타리(대표·관리자·본인·권한 가진 직원 거절)
            if (IsTenantAdminCaller())
                await _userService.SuspendAsync(id, tenantId, ct).ConfigureAwait(false);
            else
                await _userService.SuspendAsStaffAsync(id, CallerUserId(), tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (AccountActionForbiddenException ex)
        {
            return StaffForbidden(ex);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/resume")]
    // ⬛ [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "update")]  ← 20261005작5 V5-04 · 2단계 이상(§5-2)
    [RequireUsersLevel(2)]
    public async Task<IActionResult> Resume(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            // ⬛ await _userService.ResumeAsync(id, tenantId, ct).ConfigureAwait(false);
            // 20261005작5 §5-3 · P3-08 — 대표·관리자가 아니면 울타리
            if (IsTenantAdminCaller())
                await _userService.ResumeAsync(id, tenantId, ct).ConfigureAwait(false);
            else
                await _userService.ResumeAsStaffAsync(id, CallerUserId(), tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (AccountSeatFullException ex)
        {
            return SeatFull(ex);
        }
        catch (AccountActionForbiddenException ex)
        {
            return StaffForbidden(ex);
        }
        catch (AccountLinkConflictException ex)
        {
            // 🔴 작5 §8-7 P2-13 — 연결 사원 퇴사(employee_leaver) · 화면(AccountSeatApi)은 message 를 그대로 보인다
            return Conflict(new { code = ex.Code, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private ObjectResult SeatFull(AccountSeatFullException ex) =>
        Conflict(new { code = "account_seat_full", active = ex.Active, limit = ex.Limit, message = ex.Message });

    [HttpDelete("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "delete")]  ← 20261005작5 V5-04 · 계정폐기는 대표·관리자만(P-3)
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            // ⬛ await _userService.DeactivateAsync(id, tenantId, ct).ConfigureAwait(false);
            // 20261005작3 — DELETE 의 뜻이 「계정폐기」로 바뀐다(아이디 비움 · 사원 연결 끊기 · 설계 §1·§4).
            await _userService.RetireAsync(id, tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            // 헌법 #35 부모계정 삭제 차단 등
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "update")]  ← 20261005작5 V5-04 · 비번 초기화는 대표·관리자만(P-3)
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var tempPassword = await _userService.ResetPasswordAsync(id, tenantId, ct).ConfigureAwait(false);
            return Ok(new { tempPassword, message = "임시 비밀번호가 발급됐습니다." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>엑셀 일괄 업로드 템플릿 다운로드</summary>
    [HttpGet("bulk/template")]
    // ⬛ [RequirePermission("USERS", "create")]  ← 20261005작5 V5-04 · 엑셀 일괄은 대표·관리자만(P-3)
    [Authorize(Policy = "TenantAdminOnly")]
    public IActionResult GetBulkTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("사용자");
        // ⬛ ws.Cell(1, 1).Value = "이메일";
        // 20261005작5 §6 — 계정 칸 이름은 「아이디」. 읽기는 열 위치(1열)라 옛 양식도 그대로 읽힌다.
        ws.Cell(1, 1).Value = "아이디";
        ws.Cell(1, 2).Value = "이름";
        ws.Cell(1, 3).Value = "임시비밀번호";
        ws.Cell(1, 4).Value = "부서";
        ws.Cell(1, 5).Value = "직책";
        ws.Cell(1, 6).Value = "전화번호";
        ws.Cell(1, 7).Value = "역할(User/Manager)";
        // 예시 1행
        // ⬛ ws.Cell(2, 1).Value = "hong@example.com";
        ws.Cell(2, 1).Value = "hong01";
        ws.Cell(2, 2).Value = "홍길동";
        ws.Cell(2, 3).Value = "Temp1234!";
        ws.Cell(2, 4).Value = "영업";
        ws.Cell(2, 5).Value = "대리";
        ws.Cell(2, 6).Value = "010-0000-0000";
        ws.Cell(2, 7).Value = "User";

        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F6E56");
        ws.Row(1).Style.Font.FontColor = XLColor.White;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "히트판_사용자_일괄등록_템플릿.xlsx");
    }

    /// <summary>엑셀 일괄 업로드 실행 — 각 행 독립 처리</summary>
    [HttpPost("bulk")]
    [Authorize(Policy = "TenantAdminOnly")]
    // ⬛ [RequirePermission("USERS", "create")]  ← 20261005작5 V5-04 · 엑셀 일괄은 대표·관리자만(P-3)
    public async Task<IActionResult> BulkCreate(IFormFile file, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "파일이 첨부되지 않았습니다." });

        // 파일 검증 — 매직바이트까지 확인
        using var stream = file.OpenReadStream();
        var fileError = FileSecurityHelper.Validate(file.FileName, file.Length, stream);
        if (fileError != null) return BadRequest(new { message = fileError });

        // 엑셀 파싱
        var rows = new List<CreateUserDto>();
        try
        {
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (var r = 2; r <= lastRow; r++)
            {
                var email = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(email)) continue;
                rows.Add(new CreateUserDto
                {
                    Email = email,
                    UserName = ws.Cell(r, 2).GetString().Trim(),
                    Password = ws.Cell(r, 3).GetString().Trim(),
                    Department = ws.Cell(r, 4).GetString().Trim(),
                    Position = ws.Cell(r, 5).GetString().Trim(),
                    Phone = ws.Cell(r, 6).GetString().Trim(),
                    Role = string.IsNullOrWhiteSpace(ws.Cell(r, 7).GetString()) ? "User" : ws.Cell(r, 7).GetString().Trim()
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"엑셀 파싱 실패: {ex.Message}" });
        }

        if (rows.Count == 0)
            return BadRequest(new { message = "엑셀에 등록할 행이 없습니다." });

        var result = await _userService.BulkCreateAsync(rows, tenantId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    // ── 20261005작5 사원 ↔ 계정 연결 (설계 §3·§4·§5) — tenant 는 JWT 클레임(Items)만(#2) · employeeId 는 같은 tenant 조건으로만 조회 ──

    /// <summary>내 「직원 계정 관리」 단계(0~3). 화면이 버튼·사이드바 한 줄을 가른다(§5-4).</summary>
    [HttpGet("my-level")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetMyLevel([FromServices] IPermissionService permission, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = CallerUserId();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        var level = await permission.GetUsersLevelAsync(userId, tenantId, ct).ConfigureAwait(false);
        return Ok(new { level, isAdmin = IsTenantAdminCaller() });
    }

    /// <summary>계정을 만들 수 있는 사원(재직 · 계정 없음 · 2단계 직원이면 일반 직무만). 「기존 사원 고르기」(§4).</summary>
    [HttpGet("linkable-employees")]
    [RequireUsersLevel(2)]
    public async Task<IActionResult> GetLinkableEmployees([FromQuery] string? name, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _userService.ListLinkableEmployeesAsync(tenantId, name, !IsTenantAdminCaller(), ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>기존 사원에게 계정 만들기(§3 · 사원 행 새로 안 만듦). 사원관리 [계정 만들기]와 직원계정 「기존 사원 고르기」가 같은 주소.</summary>
    [HttpPost("for-employee")]
    [RequireUsersLevel(2)]
    public async Task<IActionResult> CreateForEmployee([FromBody] CreateForEmployeeDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var id = await _userService.CreateForEmployeeAsync(dto, tenantId, IsTenantAdminCaller(), ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (AccountSeatFullException ex)
        {
            // 작3 과 같은 응답 모양(code = account_seat_full) — 화면은 AccountSeatNoticeDialog 를 그대로 연다(A)
            return SeatFull(ex);
        }
        catch (EmployeeNotFoundForAccountException ex)
        {
            return NotFound(new { code = "employee_not_found", message = ex.Message });
        }
        catch (AccountLinkConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // 비밀번호 규칙 · 아이디 규칙
            return BadRequest(new { code = "invalid_input", message = ex.Message });
        }
    }

    // Web AccountPricing.ExtraAccountMonthlyWon 과 같은 값(#4 decimal). 한 곳으로 모으는 일은 개발명세서 「남은 일」.
    private const decimal ExtraAccountMonthlyWon = 10000m;

    /// <summary>
    /// 20261005작5 §8-3 ② — [구독계정추가] 창이 부르는 조회(지금 한도·추가 단가). 「구독계정추가」(USERS_SEAT · 3단계)의 서버 강제 주소.
    /// 구매 API 가 생기면 그것도 3단계로(P-2).
    /// </summary>
    [HttpGet("seat-subscription")]
    [RequireUsersLevel(3)]
    public async Task<IActionResult> GetSeatSubscription(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var s = await _userService.GetSeatsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(new
        {
            active = s.Active,
            baseLimit = s.BaseLimit,
            extra = s.Extra,
            limit = s.Limit,
            tier = s.Tier,
            extraAccountMonthlyWon = ExtraAccountMonthlyWon
        });
    }

    private bool IsTenantAdminCaller() => User.HasClaim("account_type", "tenant_admin");

    private string CallerUserId() => HttpContext.Items["UserId"]?.ToString() ?? string.Empty;

    private ObjectResult StaffForbidden(AccountActionForbiddenException ex) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Code, code = ex.Code, message = ex.Message });

    // ── 작10 자식 계정 발급 스켈레톤 (W2 매니저 가도) ─────────────────────────
    // 부모(대표) 계정만 호출 가능 — TenantAdminOnly Policy
    // 권한 매핑 영역은 UserPermissionService 확장 (매니저 가도)

    [HttpPost("children")]
    [Authorize(Policy = "TenantAdminOnly")]
    public IActionResult CreateChild([FromBody] CreateChildUserRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var parentUserId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(parentUserId)) return Forbid();

        return Accepted(new
        {
            success = true,
            message = "자식 계정 발급 요청 저장 완료 — W2 매니저 가도 (UserPermissionService 확장 영역)",
            tenantId,
            parentUserId,
            email = request.Email,
            role = request.Role
        });
    }

    [HttpPut("children/{id}/status")]
    [Authorize(Policy = "TenantAdminOnly")]
    public IActionResult UpdateChildStatus(string id, [FromBody] UpdateChildUserStatusRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        return Accepted(new
        {
            success = true,
            message = "자식 계정 상태 변경 요청 저장 완료 — W2 매니저 가도",
            userId = id,
            newStatus = request.Status
        });
    }
}
