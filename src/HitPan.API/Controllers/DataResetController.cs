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
    private readonly HitPan.API.Services.CompanyBootstrapProvisioner _provisioner;
    private readonly ILogger<DataResetController> _logger;

    public DataResetController(
        IDataResetService service,
        HitPan.API.Services.CompanyBootstrapProvisioner provisioner,
        ILogger<DataResetController> logger)
    {
        _service = service;
        _provisioner = provisioner;
        _logger = logger;
    }

    /// <summary>모든데이터 초기화 실행. 패스워드 재검증 → 강제 백업 → 초기화 → <b>회사 뼈대 재시드</b> → 로그 기록.</summary>
    /// <remarks>
    /// 🔴 20260910작1 A1 (사장님 결재 2026-09-10 *"넣어"*): 초기화는 보존 목록 밖을 다 비우므로
    /// 신규 설치 때 깔리는 <b>표준 계정과목 27 · 대표 사원 1 · 직급 6 · 근로 기준값 16 · 기본창고</b>까지 지워졌고,
    /// 되살리는 코드가 <b>0건</b>이었다(선행검증 20260909검1 §2-1). 그대로 두면 초기화 뒤 첫 판매확정·수금이
    /// <c>journal_lines → accounts</c> FK 로 죽고 대표가 결재선에서 사라진다.
    /// <b>덮어쓰기 가져오기(<c>MigrationController</c>)와 같은 메서드</b>를 부른다 — 두 경로가 갈리면 그게 다음 사고다.
    /// </remarks>
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

        // 초기화가 지운 회사 뼈대를 다시 깐다. 실패해도 초기화 자체는 이미 끝났으므로 200 을 주되,
        // 무슨 일이 있었는지 반드시 남긴다(헌법 #15 — 삼키지 않는다).
        try
        {
            var seeded = await _provisioner.ReseedCompanySkeletonAsync(TenantId!, ct).ConfigureAwait(false);
            return Ok(new
            {
                result.Success,
                result.BackupId,
                result.ClearedTableCount,
                result.Error,
                skeleton = seeded,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataReset] 초기화는 끝났으나 회사 기본 자료 다시 깔기에 실패했다");
            return Ok(new
            {
                result.Success,
                result.BackupId,
                result.ClearedTableCount,
                error = "자료는 모두 지웠으나 회사 기본 자료(계정과목·대표 사원 등)를 다시 만들지 못했습니다. 히트판을 다시 시작한 뒤에도 같으면 알려주세요.",
            });
        }
    }
}
