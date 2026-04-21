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
    [RequirePermission("USERS", "view")]
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
    [RequirePermission("USERS", "view")]
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
    [RequirePermission("USERS", "create")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var id = await _userService.CreateAsync(dto, tenantId, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "update")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _userService.UpdateAsync(id, dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _userService.DeactivateAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "update")]
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
    [RequirePermission("USERS", "create")]
    public IActionResult GetBulkTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("사용자");
        ws.Cell(1, 1).Value = "이메일";
        ws.Cell(1, 2).Value = "이름";
        ws.Cell(1, 3).Value = "임시비밀번호";
        ws.Cell(1, 4).Value = "부서";
        ws.Cell(1, 5).Value = "직책";
        ws.Cell(1, 6).Value = "전화번호";
        ws.Cell(1, 7).Value = "역할(User/Manager)";
        // 예시 1행
        ws.Cell(2, 1).Value = "hong@example.com";
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
    [RequirePermission("USERS", "create")]
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
}
