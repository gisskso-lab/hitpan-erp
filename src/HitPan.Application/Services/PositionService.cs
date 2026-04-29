using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Position;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// positions 테이블 기반 직급 마스터 서비스 구현체.
/// 사장님 결재 2026-04-29: 회사별 어드민이 직급을 직접 추가/삭제. (§절대원칙 #11)
/// </summary>
public sealed class PositionService : IPositionService
{
    private readonly IDbConnection _db;

    public PositionService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<PositionListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              position_id AS PositionId,
              code        AS Code,
              name        AS Name,
              sort_order  AS SortOrder,
              is_active   AS IsActive
            FROM positions
            WHERE tenant_id = @TenantId
            ORDER BY sort_order DESC, name
            """;

        var rows = await _db.QueryAsync<PositionListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<string> CreateAsync(string tenantId, CreatePositionRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var positionId = Guid.NewGuid().ToString();

        const string sql = """
            INSERT INTO positions
              (position_id, tenant_id, code, name, sort_order, is_active)
            VALUES
              (@PositionId, @TenantId, @Code, @Name, @SortOrder, @IsActive)
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                PositionId = positionId,
                TenantId = tenantId,
                request.Code,
                request.Name,
                request.SortOrder,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return positionId;
    }

    public async Task UpdateAsync(string tenantId, string positionId, UpdatePositionRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE positions
            SET code       = @Code,
                name       = @Name,
                sort_order = @SortOrder,
                is_active  = @IsActive,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND position_id = @PositionId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                PositionId = positionId,
                request.Code,
                request.Name,
                request.SortOrder,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string tenantId, string positionId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 결재라인 단계에서 사용 중이면 삭제 차단(논리삭제로 변경하라는 신호).
        const string usageSql = """
            SELECT COUNT(*) FROM approval_line_steps
            WHERE tenant_id = @TenantId AND position_id = @PositionId
            """;

        var inUse = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            usageSql,
            new { TenantId = tenantId, PositionId = positionId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (inUse > 0)
        {
            // 사용 중이면 비활성화로 대체 (참조 무결성 보호).
            const string deactivate = """
                UPDATE positions
                SET is_active = 0, updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND position_id = @PositionId
                """;
            await _db.ExecuteAsync(new CommandDefinition(
                deactivate,
                new { TenantId = tenantId, PositionId = positionId },
                cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        const string sql = """
            DELETE FROM positions
            WHERE tenant_id = @TenantId AND position_id = @PositionId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, PositionId = positionId },
            cancellationToken: ct)).ConfigureAwait(false);
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
