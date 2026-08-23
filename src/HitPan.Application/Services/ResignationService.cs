using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 전자 퇴직서(사직서) 서비스 — 작20260824작2 [4].
/// </summary>
/// <remarks>
/// 사장님 지시(2026-08-24): <i>"전자근로계약서 = 입사/퇴사 로 메뉴변경
/// 전자근로계약서 작성, 전자 퇴직서 작성"</i>
/// </remarks>
public sealed class ResignationService : IResignationService
{
    private readonly IDbConnection _db;
    private readonly IEmployeeService _employees;

    public ResignationService(IDbConnection db, IEmployeeService employees)
    {
        _db = db;
        _employees = employees;
    }

    /// <summary>사직 유형 라벨. 🔴 고객 노출 — 영문 코드가 뜨면 안 된다.</summary>
    private static readonly Dictionary<string, string> TypeLabels = new()
    {
        ["voluntary"]   = "자발적 퇴사",
        ["recommended"] = "권고사직",
        ["expired"]     = "계약만료",
        ["retirement"]  = "정년퇴직"
    };

    /// <summary>
    /// 상태 라벨.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>rejected</c>(회사가 물림)와 <c>withdrawn</c>(본인이 물림)을 <b>다른 말로</b> 쓴다.
    /// 같은 말로 뭉개면 나중에 "왜 안 나갔나" 를 아무도 모른다.
    /// </remarks>
    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["draft"]     = "작성중",
        ["pending"]   = "결재중",
        ["approved"]  = "수리됨",
        ["rejected"]  = "반려됨",
        ["completed"] = "퇴사완료",
        ["withdrawn"] = "철회함"
    };

    private const string SelectColumns = """
        r.resignation_id AS ResignationId, r.employee_id AS EmployeeId,
        r.employee_name AS EmployeeName, r.dept_name AS DeptName, r.position_name AS PositionName,
        r.resign_type AS ResignType, r.desired_date AS DesiredDate, r.actual_date AS ActualDate,
        r.reason AS Reason, r.handover_to AS HandoverTo, r.handover_note AS HandoverNote,
        r.return_items AS ReturnItems, r.status AS Status, r.approval_id AS ApprovalId,
        r.submitted_at AS SubmittedAt, r.approved_at AS ApprovedAt, r.reject_reason AS RejectReason,
        r.signed_at AS SignedAt, r.created_at AS CreatedAt
        """;

    public async Task<List<ResignationLetterDto>> GetListAsync(
        string tenantId, string employeeId, bool onlyMine, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 🔴 onlyMine 은 화면이 정하지 않는다. 컨트롤러가 권한을 보고 정한다 —
        //    화면이 정하면 요청을 고쳐 남의 사직서를 볼 수 있다.
        var mineFilter = onlyMine ? " AND r.employee_id = @EmployeeId" : string.Empty;

        var rows = await _db.QueryAsync<ResignationLetterDto>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM resignation_letters r
            WHERE r.tenant_id = @TenantId{mineFilter}
            ORDER BY r.created_at DESC
            """,
            new { TenantId = tenantId, EmployeeId = employeeId }, cancellationToken: ct));

        return rows.Select(MapLabels).ToList();
    }

    public async Task<ResignationLetterDto?> GetAsync(
        string resignationId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var row = await _db.QueryFirstOrDefaultAsync<ResignationLetterDto>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM resignation_letters r
            WHERE r.resignation_id = @Id AND r.tenant_id = @TenantId
            """,
            new { Id = resignationId, TenantId = tenantId }, cancellationToken: ct));

        return row is null ? null : MapLabels(row);
    }

    public async Task<string> SaveAsync(
        SaveResignationRequest request, string tenantId, string actorId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 작성 시점의 이름·부서·직급을 문자열로 담는다 —
        // 🔴 사원 정보가 나중에 바뀌어도 **문서는 그대로여야 한다.**
        var emp = await _db.QueryFirstOrDefaultAsync<(string EmpName, string? DeptName, string? Position)>(
            new CommandDefinition(
                """
                SELECT e.emp_name AS EmpName, d.dept_name AS DeptName, e.position AS Position
                FROM employees e
                LEFT JOIN departments d ON d.dept_id = e.dept_id AND d.tenant_id = e.tenant_id
                WHERE e.tenant_id = @TenantId AND e.employee_id = @EmployeeId
                """,
                new { TenantId = tenantId, request.EmployeeId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(emp.EmpName))
        {
            throw new InvalidOperationException("사원 정보를 찾을 수 없습니다.");
        }

        if (!string.IsNullOrEmpty(request.ResignationId))
        {
            // 🔴 제출된 뒤에는 못 고친다. 결재가 도는 중에 내용이 바뀌면
            //    결재자가 본 것과 확정되는 것이 달라진다.
            var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM resignation_letters WHERE resignation_id = @Id AND tenant_id = @TenantId",
                new { Id = request.ResignationId, TenantId = tenantId }, cancellationToken: ct));

            if (status is null) throw new InvalidOperationException("사직서를 찾을 수 없습니다.");
            if (status is not ("draft" or "rejected"))
            {
                throw new InvalidOperationException("결재가 진행 중이거나 끝난 사직서는 수정할 수 없습니다.");
            }

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE resignation_letters
                SET resign_type = @ResignType, desired_date = @DesiredDate, reason = @Reason,
                    handover_to = @HandoverTo, handover_note = @HandoverNote, return_items = @ReturnItems,
                    status = 'draft', reject_reason = NULL,
                    updated_by = @ActorId
                WHERE resignation_id = @ResignationId AND tenant_id = @TenantId
                """,
                new
                {
                    request.ResignationId, TenantId = tenantId, request.ResignType,
                    request.DesiredDate, request.Reason, request.HandoverTo,
                    request.HandoverNote, request.ReturnItems, ActorId = actorId
                }, cancellationToken: ct));

            return request.ResignationId;
        }

        var newId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO resignation_letters
              (resignation_id, tenant_id, employee_id, employee_name, dept_name, position_name,
               resign_type, desired_date, reason, handover_to, handover_note, return_items,
               status, created_by, updated_by)
            VALUES
              (@Id, @TenantId, @EmployeeId, @EmployeeName, @DeptName, @PositionName,
               @ResignType, @DesiredDate, @Reason, @HandoverTo, @HandoverNote, @ReturnItems,
               'draft', @ActorId, @ActorId)
            """,
            new
            {
                Id = newId, TenantId = tenantId, request.EmployeeId,
                EmployeeName = emp.EmpName, DeptName = emp.DeptName, PositionName = emp.Position,
                request.ResignType, request.DesiredDate, request.Reason,
                request.HandoverTo, request.HandoverNote, request.ReturnItems, ActorId = actorId
            }, cancellationToken: ct));

        return newId;
    }

    public async Task<string?> SubmitAsync(
        string resignationId, string tenantId, string actorId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var doc = await GetAsync(resignationId, tenantId, ct);
        if (doc is null) throw new InvalidOperationException("사직서를 찾을 수 없습니다.");
        if (doc.Status is not ("draft" or "rejected"))
        {
            throw new InvalidOperationException("이미 제출된 사직서입니다.");
        }

        // 🔴 결재를 못 걸면 **먼저 이유를 돌려준다.** 조용히 pending 으로 바꾸면
        //    직원은 "냈다" 고 보는데 결재함엔 안 뜬다 — 8/21 휴직 P0 가 그 자리였다.
        var blocker = await ApprovalTriggerHelper.DescribeApprovalBlockerAsync(
            _db, tenantId, "resignation", ct);
        if (blocker is not null) return blocker;

        await ApprovalTriggerHelper.TryCreateApprovalAsync(
            _db, "resignation", resignationId, string.Empty,
            $"사직서: {doc.EmployeeName} ({doc.DesiredDate:yyyy-MM-dd} 퇴사 희망)",
            0m, tenantId, doc.EmployeeId, doc.EmployeeName, ct);

        // 방금 만들어진 결재 문서를 물어 연결한다.
        var approvalId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT approval_id FROM approval_documents
            WHERE tenant_id = @TenantId AND doc_type = 'resignation' AND ref_id = @RefId
            ORDER BY requested_at DESC LIMIT 1
            """,
            new { TenantId = tenantId, RefId = resignationId }, cancellationToken: ct));

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE resignation_letters
            SET status = 'pending', approval_id = @ApprovalId, submitted_at = NOW(6),
                reject_reason = NULL, updated_by = @ActorId
            WHERE resignation_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = resignationId, TenantId = tenantId, ApprovalId = approvalId, ActorId = actorId },
            cancellationToken: ct));

        return null;
    }

    public async Task WithdrawAsync(
        string resignationId, string tenantId, string actorId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM resignation_letters WHERE resignation_id = @Id AND tenant_id = @TenantId",
            new { Id = resignationId, TenantId = tenantId }, cancellationToken: ct));

        if (status is null) throw new InvalidOperationException("사직서를 찾을 수 없습니다.");

        // 🔴 이미 수리된 것은 못 거둔다. 퇴사가 확정된 뒤에 철회하려면
        //    그건 철회가 아니라 **재입사**다 — 다른 일이다.
        if (status is "completed" or "approved")
        {
            throw new InvalidOperationException("이미 수리된 사직서는 철회할 수 없습니다.");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE resignation_letters
            SET status = 'withdrawn', updated_by = @ActorId
            WHERE resignation_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = resignationId, TenantId = tenantId, ActorId = actorId }, cancellationToken: ct));
    }

    public async Task AcceptAsync(
        string resignationId, AcceptResignationRequest request,
        string tenantId, string actorId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var doc = await GetAsync(resignationId, tenantId, ct);
        if (doc is null) throw new InvalidOperationException("사직서를 찾을 수 없습니다.");
        if (doc.Status == "completed") throw new InvalidOperationException("이미 처리된 사직서입니다.");
        if (doc.Status is "withdrawn") throw new InvalidOperationException("철회된 사직서입니다.");

        // 🔴 실제 퇴사 반영은 **기존 로직을 부른다.** 여기서 employees 를 직접 UPDATE 하지 않는다.
        //    ResignAsync 는 결재선 점검·work_status 정리까지 하는데(8/12 단계0),
        //    여기서 따로 쓰면 그것들이 통째로 빠진다 — 같은 일을 두 곳이 다르게 하게 된다.
        var ok = await _employees.ResignAsync(
            tenantId, doc.EmployeeId, request.ActualDate, doc.Reason, ct);

        if (!ok) throw new InvalidOperationException("퇴사 처리에 실패했습니다.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE resignation_letters
            SET status = 'completed', actual_date = @ActualDate, approved_at = NOW(6),
                updated_by = @ActorId
            WHERE resignation_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = resignationId, TenantId = tenantId, request.ActualDate, ActorId = actorId },
            cancellationToken: ct));
    }

    private static ResignationLetterDto MapLabels(ResignationLetterDto d)
    {
        d.ResignTypeLabel = TypeLabels.GetValueOrDefault(d.ResignType, d.ResignType);
        d.StatusLabel = StatusLabels.GetValueOrDefault(d.Status, d.Status);
        return d;
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
