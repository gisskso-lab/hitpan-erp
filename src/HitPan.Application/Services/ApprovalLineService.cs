using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.ApprovalLine;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// approval_lines + approval_line_steps 기반 결재라인 서비스 구현체.
/// 사장님 결재 2026-04-29: "회사마다 결재라인 시스템이 다르니" 어드민이 자유롭게 추가/삭제.
/// </summary>
public sealed class ApprovalLineService : IApprovalLineService
{
    private readonly IDbConnection _db;

    public ApprovalLineService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<ApprovalLineListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              al.approval_line_id AS ApprovalLineId,
              al.name             AS Name,
              al.description      AS Description,
              al.sort_order       AS SortOrder,
              al.is_active        AS IsActive,
              (SELECT COUNT(*) FROM approval_line_steps s
                WHERE s.tenant_id = al.tenant_id
                  AND s.approval_line_id = al.approval_line_id) AS StepCount
            FROM approval_lines al
            WHERE al.tenant_id = @TenantId
            ORDER BY al.sort_order, al.name
            """;

        var rows = await _db.QueryAsync<ApprovalLineListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<ApprovalLineDetailDto?> GetAsync(string tenantId, string approvalLineId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string headSql = """
            SELECT approval_line_id AS ApprovalLineId,
                   name             AS Name,
                   description      AS Description,
                   sort_order       AS SortOrder,
                   is_active        AS IsActive
            FROM approval_lines
            WHERE tenant_id = @TenantId AND approval_line_id = @ApprovalLineId
            """;

        var head = await _db.QueryFirstOrDefaultAsync<ApprovalLineDetailDto>(new CommandDefinition(
            headSql,
            new { TenantId = tenantId, ApprovalLineId = approvalLineId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (head is null) return null;

        const string stepsSql = """
            SELECT s.step_id        AS StepId,
                   s.step_order     AS StepOrder,
                   s.position_id    AS PositionId,
                   p.name           AS PositionName,
                   s.employee_id    AS EmployeeId,
                   e.emp_name       AS EmployeeName
            FROM approval_line_steps s
            LEFT JOIN positions p
              ON p.position_id = s.position_id AND p.tenant_id = s.tenant_id
            LEFT JOIN employees e
              ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND s.approval_line_id = @ApprovalLineId
            ORDER BY s.step_order
            """;

        var steps = await _db.QueryAsync<ApprovalLineStepDto>(new CommandDefinition(
            stepsSql,
            new { TenantId = tenantId, ApprovalLineId = approvalLineId },
            cancellationToken: ct)).ConfigureAwait(false);

        head.Steps = steps.ToList();
        return head;
    }

    public async Task<string> CreateAsync(string tenantId, SaveApprovalLineRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var lineId = Guid.NewGuid().ToString();

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");

        try
        {
            const string headSql = """
                INSERT INTO approval_lines
                  (approval_line_id, tenant_id, name, description, sort_order, is_active)
                VALUES
                  (@ApprovalLineId, @TenantId, @Name, @Description, @SortOrder, @IsActive)
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                headSql,
                new
                {
                    ApprovalLineId = lineId,
                    TenantId = tenantId,
                    request.Name,
                    request.Description,
                    request.SortOrder,
                    IsActive = request.IsActive ? 1 : 0
                },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            await InsertStepsAsync(tenantId, lineId, request.Steps, tx, ct).ConfigureAwait(false);

            tx.Commit();
            return lineId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(string tenantId, string approvalLineId, SaveApprovalLineRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");

        try
        {
            const string headSql = """
                UPDATE approval_lines
                SET name        = @Name,
                    description = @Description,
                    sort_order  = @SortOrder,
                    is_active   = @IsActive,
                    updated_at  = NOW(6)
                WHERE tenant_id = @TenantId AND approval_line_id = @ApprovalLineId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                headSql,
                new
                {
                    TenantId = tenantId,
                    ApprovalLineId = approvalLineId,
                    request.Name,
                    request.Description,
                    request.SortOrder,
                    IsActive = request.IsActive ? 1 : 0
                },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            const string deleteSteps = """
                DELETE FROM approval_line_steps
                WHERE tenant_id = @TenantId AND approval_line_id = @ApprovalLineId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                deleteSteps,
                new { TenantId = tenantId, ApprovalLineId = approvalLineId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            await InsertStepsAsync(tenantId, approvalLineId, request.Steps, tx, ct).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(string tenantId, string approvalLineId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");

        try
        {
            const string deleteSteps = """
                DELETE FROM approval_line_steps
                WHERE tenant_id = @TenantId AND approval_line_id = @ApprovalLineId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                deleteSteps,
                new { TenantId = tenantId, ApprovalLineId = approvalLineId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            const string deleteHead = """
                DELETE FROM approval_lines
                WHERE tenant_id = @TenantId AND approval_line_id = @ApprovalLineId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                deleteHead,
                new { TenantId = tenantId, ApprovalLineId = approvalLineId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private async Task InsertStepsAsync(
        string tenantId,
        string approvalLineId,
        List<SaveApprovalLineStepRequest> steps,
        IDbTransaction tx,
        CancellationToken ct)
    {
        if (steps is null || steps.Count == 0) return;

        const string sql = """
            INSERT INTO approval_line_steps
              (step_id, tenant_id, approval_line_id, step_order, position_id, employee_id)
            VALUES
              (@StepId, @TenantId, @ApprovalLineId, @StepOrder, @PositionId, @EmployeeId)
            """;

        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            await _db.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    StepId = Guid.NewGuid().ToString(),
                    TenantId = tenantId,
                    ApprovalLineId = approvalLineId,
                    StepOrder = i + 1,
                    s.PositionId,
                    s.EmployeeId
                },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
