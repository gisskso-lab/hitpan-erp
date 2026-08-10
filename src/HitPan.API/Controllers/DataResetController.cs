using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 모든데이터 초기화(히트판 자료 포맷) API (사장님 결재 2026-06-23).
/// 부모계정·회사정보·로그 제외 모든 업무 데이터(원장·장부 포함)를 비운다.
/// 실행 전 부모계정 패스워드 재입력 + 강제 백업을 강제하며, 초기화 동작도 audit 로그에 남긴다.
/// 권한: TenantAdminOnly — 부모계정 전용. §#22 본사 미수신(로컬 처리).
/// </summary>
[ApiController]
[Route("api/data-reset")]
[Authorize(Policy = "TenantAdminOnly")]
// 🔴 2026-08-11 (사장님 지시): 자료관리는 **부모계정 + 메인PC 환경에서만**.
//   화면을 감추는 것만으로는 API 를 직접 부르면 그대로 지워진다. 서버에서도 막는다.
[HitPan.API.Security.MainPcOnly]
public sealed class DataResetController : HitPanControllerBase
{
    private readonly IDataResetService _service;

    public DataResetController(IDataResetService service)
    {
        _service = service;
    }

    /// <summary>모든데이터 초기화 실행. 패스워드 재검증 → 강제 백업 → 초기화 → 로그 기록.</summary>
    [HttpPost]
    public async Task<IActionResult> ResetAll([FromBody] DataResetRequest request, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        if (string.IsNullOrEmpty(UserId)) return Forbid();

        var result = await _service.ResetAllAsync(request, TenantId!, UserId!, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
