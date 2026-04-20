using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 레거시 히트판 MDB 데이터 마이그레이션 컨트롤러
/// - 기존 VB + Access(.mdb) 데이터를 신규 ERP DB로 이관
/// - tenant_admin 권한 필수
/// </summary>
[ApiController]
[Route("api/migration")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class MigrationController : ControllerBase
{
    private readonly MdbMigrationService _migrationService;

    public MigrationController(MdbMigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    /// <summary>
    /// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음)
    /// - 마이그레이션 전 데이터 규모를 확인할 때 사용
    /// </summary>
    [HttpGet("legacy-mdb/preview")]
    public async Task<IActionResult> PreviewLegacyMdb([FromQuery] string folderPath, CancellationToken ct)
    {
        // tenant_id는 JWT 클레임 기반 TenantMiddleware에서 설정
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 폴더 경로 유효성 검증
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
        }

        try
        {
            // preview = true: 건수만 조회, 실제 데이터 이관 없음
            var result = await _migrationService.PreviewAsync(folderPath, tenantId, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            // MDB 파일을 찾을 수 없는 경우
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // MDB 파일 형식 오류 등
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 레거시 MDB 데이터를 신규 ERP DB로 마이그레이션 실행
    /// - 업체, 상품, BOM, 사원, 발주, 수주, 재고원장, 세금계산서, 수금, 분개, 현금출납 등 일괄 이관
    /// </summary>
    [HttpPost("legacy-mdb")]
    public async Task<IActionResult> MigrateLegacyMdb([FromBody] MdbMigrationRequest request, CancellationToken ct)
    {
        // tenant_id는 JWT 클레임 기반 TenantMiddleware에서 설정
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 폴더 경로 유효성 검증
        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
        }

        try
        {
            // 실제 마이그레이션 실행 — 테이블별 이관 건수 반환
            var result = await _migrationService.MigrateAsync(request.FolderPath, tenantId, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            // MDB 파일을 찾을 수 없는 경우
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // 데이터 무결성 오류, MDB 파일 형식 오류 등
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>
/// MDB 마이그레이션 요청 DTO
/// </summary>
public record MdbMigrationRequest
{
    /// <summary>
    /// 레거시 MDB 파일이 위치한 폴더 경로
    /// </summary>
    public string FolderPath { get; init; } = string.Empty;
}
