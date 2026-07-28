namespace HitPan.Application.Interfaces;

using HitPan.Application.DTOs.DataReset;

/// <summary>
/// 모든데이터 초기화(히트판 자료 포맷) 서비스 (사장님 결재 2026-06-23).
/// 부모계정·회사정보·로그·구조설정만 보존하고, 원장·장부·마스터 포함 모든 업무 데이터를 비운다.
/// 실행 전 ① 부모계정 패스워드 재검증 ② 강제 백업 ③ 초기화 ④ audit_logs 기록 순서를 강제한다.
/// </summary>
public interface IDataResetService
{
    /// <summary>
    /// 모든데이터 초기화를 실행한다.
    /// 패스워드 재검증 → 강제 백업 → 보존 화이트리스트 외 테이블 삭제 → 초기화 동작 로그 기록.
    /// </summary>
    Task<DataResetResponse> ResetAllAsync(DataResetRequest request, string tenantId, string userId, CancellationToken ct = default);
}
