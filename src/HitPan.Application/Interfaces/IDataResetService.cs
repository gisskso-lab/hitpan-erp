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

    /// <summary>
    /// 20261125작 — 요청자가 「살아 있는 대표 계정(is_parent = 1 · is_deleted = 0 · is_active = 1 · tenant_admin)」인지 로컬 DB 로 가른다.
    /// 쿼리 1번 · tenant 범위. <see cref="ResetAllAsync"/> 가 비번 확인 · 강제 백업 앞에서 이것으로 막는다.
    /// #12 구현체: DataResetService(진짜) · SpyDataResetService · SucceedingDataResetService(시험 가짜).
    /// </summary>
    Task<DataResetGate> GetResetGateAsync(string tenantId, string userId, CancellationToken ct = default);
}
