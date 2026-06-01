using HitPan.Application.DTOs.Landing;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 대리점 신청 서비스 (사장님 결재 2026-06-01)
/// 랜딩 /reseller/signup 에서 신청 → 본사 백오피스 심사
/// </summary>
public interface IResellerApplicationService
{
    Task<ResellerApplicationResult> SubmitAsync(ResellerApplicationRequest request, CancellationToken ct = default);
}
