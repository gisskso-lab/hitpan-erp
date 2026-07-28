using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 연차 승인/반려 시 잔여연차 차감 + 대응 결재문서 동기화 공통 헬퍼.
///
/// 봉합 (2026-06-23, 6차 전수조사 NEW-A1, 사장님 결재 "연차도 결재선 + A·B 둘다"):
///   연차는 두 경로로 최종 처리될 수 있다 — ① HR 전용 승인화면(LeaveRequestService.ApproveAsync) ②
///   결재함 결재선 최종승인(ApprovalService.ProcessAsync). 종전엔 두 경로가 서로를 갱신 안 해, 결재함에서
///   승인해도 leave_requests 가 안 바뀌고(잔여 미차감·캘린더 미반영), HR 화면에서 승인해도 결재문서가
///   pending 유령으로 남았다(헌법 #20 끊김).
///
///   해법: 차감 로직과 결재문서 동기화를 정적 헬퍼로 단일화해, 두 경로가 ★호출자의 트랜잭션(tx)★에
///   합류해 호출하게 한다. ApprovalService → LeaveRequestService 직접 의존(순환참조)을 만들지 않고,
///   모두 동일 IDbConnection·동일 tx 안에서 raw SQL 로 처리(계층 위험 0, 설계팀장 확정).
/// </summary>
public static class LeaveBalanceHelper
{
    /// <summary>
    /// 승인된 연차 신청의 잔여연차를 차감한다('annual' 유형만). 호출자 트랜잭션에 합류.
    /// 한도 초과는 차단하지 않고 경고만 남긴다(현장 선사용·이월 관행 수용, 헌법 #20·#15).
    /// leave_requests.status 를 'approved' 로 바꾸는 책임은 호출자가 진다(이 헬퍼는 차감만).
    /// </summary>
    public static async Task DeductAsync(
        IDbConnection db, IDbTransaction tx, string tenantId, string requestId, CancellationToken ct = default)
    {
        var info = await db.QueryFirstOrDefaultAsync<(string EmployeeId, decimal LeaveDays, string LeaveType)>(
            new CommandDefinition(
                "SELECT employee_id AS EmployeeId, leave_days AS LeaveDays, leave_type AS LeaveType FROM leave_requests WHERE tenant_id=@TenantId AND request_id=@RequestId",
                new { TenantId = tenantId, RequestId = requestId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (info.LeaveType != "annual" || info.LeaveDays <= 0m || string.IsNullOrEmpty(info.EmployeeId))
            return;

        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET annual_leave_used = annual_leave_used + @Days, updated_at = NOW(6) WHERE tenant_id=@TenantId AND employee_id=@EmpId",
            new { Days = info.LeaveDays, TenantId = tenantId, EmpId = info.EmployeeId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var over = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN annual_leave_used > annual_leave_total THEN 1 ELSE 0 END FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
            new { TenantId = tenantId, EmpId = info.EmployeeId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (over == 1)
            System.Diagnostics.Trace.TraceWarning($"[Leave] 사원 {info.EmployeeId} 연차 한도 초과 사용(선사용/이월 가능) — request {requestId}");
    }

    /// <summary>
    /// 한 경로에서 연차를 최종 처리(승인/반려)했을 때, 대응 결재문서(doc_type='leave', ref_id=requestId)를
    /// 같은 상태로 닫아 양방향 일관을 유지한다(유령 pending 제거). status='pending' 가드로 멱등 — 다른 경로가
    /// 이미 닫았거나 결재선 미설정(문서 없음)이면 0행(무해). 호출자 트랜잭션에 합류.
    /// </summary>
    public static async Task CloseLinkedApprovalAsync(
        IDbConnection db, IDbTransaction tx, string tenantId, string requestId, string status, CancellationToken ct = default)
    {
        await db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE approval_documents
            SET status = @Status, completed_at = NOW(6), updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND doc_type = 'leave' AND ref_id = @RequestId AND status = 'pending'
            """,
            new { Status = status, TenantId = tenantId, RequestId = requestId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }
}
