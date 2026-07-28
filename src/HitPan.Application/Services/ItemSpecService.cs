using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Item;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// 상품 규격 1:N 관리 (사장님 작업지시 2026-05-31)
// SOFT DELETE (is_active=0) — 절대원칙 #3 호환
public class ItemSpecService : IItemSpecService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ItemSpecService> _logger;

    public ItemSpecService(IUnitOfWork unitOfWork, ILogger<ItemSpecService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ItemSpecDto>> GetByItemAsync(string tenantId, string itemId, bool activeOnly = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenant_id required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("item_id required", nameof(itemId));

        var db = _unitOfWork.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);
        // 진범 #99 봉합 (2026-05-31): DB-76 시드의 UUID() 함수가 MySqlConnector 메타에 Guid 힌트 저장
        // → SELECT 시 Guid 반환 → string DTO 충돌 → CAST AS CHAR 명시로 string 강제 정합
        var sql = activeOnly
            ? @"SELECT CAST(spec_id AS CHAR) AS SpecId, CAST(item_id AS CHAR) AS ItemId, spec_value AS SpecValue,
                       display_order AS DisplayOrder, is_default AS IsDefault, is_active AS IsActive
                FROM item_specs
                WHERE tenant_id = @TenantId AND item_id = @ItemId AND is_active = 1
                ORDER BY is_default DESC, display_order ASC, spec_value ASC"
            : @"SELECT CAST(spec_id AS CHAR) AS SpecId, CAST(item_id AS CHAR) AS ItemId, spec_value AS SpecValue,
                       display_order AS DisplayOrder, is_default AS IsDefault, is_active AS IsActive
                FROM item_specs
                WHERE tenant_id = @TenantId AND item_id = @ItemId
                ORDER BY is_active DESC, is_default DESC, display_order ASC, spec_value ASC";

        var rows = await db.QueryAsync<ItemSpecDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, ItemId = itemId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ItemSpecDto> CreateAsync(string tenantId, string itemId, CreateItemSpecRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SpecValue))
            throw new InvalidOperationException("규격값은 비어있을 수 없습니다.");

        var specId = Guid.NewGuid().ToString();
        var db = _unitOfWork.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // is_default=1 신규 시 기존 default 해제 (1:N 중 default 1개만)
        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE item_specs SET is_default = 0
                  WHERE tenant_id = @TenantId AND item_id = @ItemId AND is_default = 1",
                new { TenantId = tenantId, ItemId = itemId }, cancellationToken: ct));
        }

        await db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO item_specs
                (spec_id, tenant_id, item_id, spec_value, display_order, is_default, is_active)
              VALUES
                (@SpecId, @TenantId, @ItemId, @SpecValue, @DisplayOrder, @IsDefault, 1)",
            new
            {
                SpecId = specId,
                TenantId = tenantId,
                ItemId = itemId,
                request.SpecValue,
                request.DisplayOrder,
                IsDefault = request.IsDefault ? 1 : 0
            },
            cancellationToken: ct));

        // 작지 #4 동기화 정책 (사장님 작업지시 2026-05-31)
        // is_default=1로 신규 저장 시 items.spec도 동일값으로 sync
        if (request.IsDefault)
        {
            await SyncItemsSpecColumnAsync(db, tenantId, itemId, request.SpecValue, ct);
        }

        _logger.LogInformation("ItemSpec created: tenant={Tenant} item={Item} spec={Spec}",
            tenantId, itemId, request.SpecValue);

        return new ItemSpecDto
        {
            SpecId = specId,
            ItemId = itemId,
            SpecValue = request.SpecValue,
            DisplayOrder = request.DisplayOrder,
            IsDefault = request.IsDefault,
            IsActive = true
        };
    }

    public async Task<ItemSpecDto> UpdateAsync(string tenantId, string itemId, string specId, UpdateItemSpecRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SpecValue))
            throw new InvalidOperationException("규격값은 비어있을 수 없습니다.");

        var db = _unitOfWork.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE item_specs SET is_default = 0
                  WHERE tenant_id = @TenantId AND item_id = @ItemId AND spec_id <> @SpecId AND is_default = 1",
                new { TenantId = tenantId, ItemId = itemId, SpecId = specId }, cancellationToken: ct));
        }

        var rows = await db.ExecuteAsync(new CommandDefinition(
            @"UPDATE item_specs
              SET spec_value = @SpecValue,
                  display_order = @DisplayOrder,
                  is_default = @IsDefault,
                  is_active = @IsActive
              WHERE tenant_id = @TenantId AND item_id = @ItemId AND spec_id = @SpecId",
            new
            {
                SpecId = specId,
                TenantId = tenantId,
                ItemId = itemId,
                request.SpecValue,
                request.DisplayOrder,
                IsDefault = request.IsDefault ? 1 : 0,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct));

        if (rows == 0)
        {
            throw new InvalidOperationException($"해당 규격을 찾을 수 없습니다. spec_id={specId}");
        }

        // 작지 #4 동기화 (사장님 작업지시 2026-05-31)
        if (request.IsDefault && request.IsActive)
        {
            await SyncItemsSpecColumnAsync(db, tenantId, itemId, request.SpecValue, ct);
        }

        return new ItemSpecDto
        {
            SpecId = specId,
            ItemId = itemId,
            SpecValue = request.SpecValue,
            DisplayOrder = request.DisplayOrder,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive
        };
    }

    public async Task DeactivateAsync(string tenantId, string itemId, string specId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);
        await db.ExecuteAsync(new CommandDefinition(
            @"UPDATE item_specs SET is_active = 0
              WHERE tenant_id = @TenantId AND item_id = @ItemId AND spec_id = @SpecId",
            new { TenantId = tenantId, ItemId = itemId, SpecId = specId }, cancellationToken: ct));

        _logger.LogInformation("ItemSpec deactivated: tenant={Tenant} item={Item} spec={Spec}",
            tenantId, itemId, specId);
    }

    // EF Core Lazy connection 봉합 (진범 #99, 사장님 결재 2026-05-31)
    // UnitOfWork.GetDbConnection()은 Closed 상태로 반환될 수 있음 → 명시 OPEN 보장
    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
        {
            await c.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        db.Open();
    }

    // 작지 #4 동기화 정책 (사장님 작업지시 2026-05-31)
    // item_specs(is_default=1) 변경 시 → items.spec 컬럼에 자동 sync
    // 기존 코드(items.spec 단일 컬럼 사용)와 신규 코드(item_specs 1:N) 호환 보장
    private async Task SyncItemsSpecColumnAsync(IDbConnection db, string tenantId, string itemId, string specValue, CancellationToken ct)
    {
        try
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE items SET spec = @Spec
                  WHERE tenant_id = @TenantId AND item_id = @ItemId AND COALESCE(spec, '') <> @Spec",
                new { TenantId = tenantId, ItemId = itemId, Spec = specValue },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // items 테이블 spec 컬럼 미존재 등 = silent OK (헌법 #15 정합 log만)
            _logger.LogWarning(ex, "items.spec sync failed: tenant={Tenant} item={Item}", tenantId, itemId);
        }
    }
}
