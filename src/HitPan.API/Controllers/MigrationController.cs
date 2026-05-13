using System.Runtime.Versioning;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HitPan.API.Controllers;

/// <summary>
/// 레거시 히트판 MDB 데이터 마이그레이션 컨트롤러
/// - 기존 VB + Access(.mdb) 데이터를 신규 ERP DB로 이관
/// - tenant_admin 권한 필수
/// - Windows 전용 (Microsoft.Jet.OLEDB ACE 드라이버 의존) — Linux 컨테이너 배포 시 미지원
///   사장님 헌법 #19 warnings 0 준수: SupportedOSPlatform 어트리뷰트로 명시 → CA1416 해소
/// </summary>
[ApiController]
[Route("api/migration")]
[Authorize(Policy = "TenantAdminOnly")]
[SupportedOSPlatform("windows")]
public sealed class MigrationController : ControllerBase
{
    private readonly MdbMigrationService _migrationService;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(MdbMigrationService migrationService, ILogger<MigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    /// <summary>
    /// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음)
    /// - 마이그레이션 전 데이터 규모를 확인할 때 사용
    /// - 핫픽스 2026-05-13: mdbPassword 파라미터 추가 (비번 걸린 레거시 MDB 지원)
    /// </summary>
    [HttpGet("legacy-mdb/preview")]
    public async Task<IActionResult> PreviewLegacyMdb(
        [FromQuery] string folderPath,
        [FromQuery] string? mdbPassword,
        CancellationToken ct)
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
            var result = await _migrationService.PreviewAsync(folderPath, tenantId, mdbPassword, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Preview] MDB 파일 미발견 folder={Folder}", folderPath);
            return NotFound(new { message = $"MDB 파일을 찾을 수 없습니다. 폴더 경로를 확인해주세요. ({ex.Message})" });
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Preview] 폴더 미존재 folder={Folder}", folderPath);
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {folderPath}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Preview] 폴더 접근 권한 없음 folder={Folder}", folderPath);
            return StatusCode(403, new { message = $"폴더 접근 권한이 없습니다: {folderPath}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Preview] 데이터 무결성·형식 오류 folder={Folder}", folderPath);
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Data.OleDb.OleDbException ex)
        {
            _logger.LogWarning(ex, "[Preview] OLEDB 오류 folder={Folder} hresult={HResult}", folderPath, ex.HResult);
            var hint = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("암호")
                ? "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다. 비밀번호 칸을 확인해주세요."
                : "MDB 파일을 열 수 없습니다. ACE OLEDB Provider 설치 여부 + 파일 손상 여부를 확인해주세요.";
            return BadRequest(new { message = $"{hint} (상세: {ex.Message})" });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "[Preview] ACE Provider 미설치 가능성 folder={Folder}", folderPath);
            return StatusCode(500, new { message = "MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다. 서버 설정을 확인해주세요." });
        }
        catch (Exception ex)
        {
            // 봉합: 미처리 예외도 사용자에게 의미있는 메시지로 (silent swallow 금지 - 헌법 #15)
            _logger.LogError(ex, "[Preview] 미처리 예외 folder={Folder}", folderPath);
            return StatusCode(500, new { message = $"미리보기 실행 중 오류가 발생했습니다: {ex.GetType().Name} - {ex.Message}" });
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
            var result = await _migrationService.MigrateAsync(request.FolderPath, tenantId, request.MdbPassword, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Migrate] MDB 파일 미발견 folder={Folder}", request.FolderPath);
            return NotFound(new { message = $"MDB 파일을 찾을 수 없습니다: {ex.Message}" });
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 폴더 미존재 folder={Folder}", request.FolderPath);
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {request.FolderPath}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 폴더 접근 권한 없음 folder={Folder}", request.FolderPath);
            return StatusCode(403, new { message = $"폴더 접근 권한이 없습니다: {request.FolderPath}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 데이터 무결성·형식 오류 folder={Folder}", request.FolderPath);
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Data.OleDb.OleDbException ex)
        {
            _logger.LogWarning(ex, "[Migrate] OLEDB 오류 folder={Folder} hresult={HResult}", request.FolderPath, ex.HResult);
            var hint = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("암호")
                ? "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다. 비밀번호 칸을 확인해주세요."
                : "MDB 파일을 열 수 없습니다. ACE OLEDB Provider 설치 여부 + 파일 손상 여부를 확인해주세요.";
            return BadRequest(new { message = $"{hint} (상세: {ex.Message})" });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "[Migrate] ACE Provider 미설치 가능성 folder={Folder}", request.FolderPath);
            return StatusCode(500, new { message = "MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Migrate] 미처리 예외 folder={Folder}", request.FolderPath);
            return StatusCode(500, new { message = $"마이그레이션 실행 중 오류: {ex.GetType().Name} - {ex.Message}" });
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

    /// <summary>
    /// MDB 파일 비밀번호 (선택사항, 핫픽스 2026-05-13).
    /// 레거시 히트판 MDB는 비번이 걸려있는 경우가 있다 (예: 7618968).
    /// 비번이 없으면 null 또는 빈 문자열.
    /// </summary>
    public string? MdbPassword { get; init; }
}
