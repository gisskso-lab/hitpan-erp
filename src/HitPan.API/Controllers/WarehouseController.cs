using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 창고 관리 CRUD 컨트롤러 — Dapper 직접 사용
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize(Policy = "TenantAdminOnly")]
public class WarehouseController : ControllerBase
{
    private readonly IDbConnection _db;

    public WarehouseController(IDbConnection db)
    {
        _db = db;
    }

    /// <summary>
    /// 테넌트의 활성 창고 목록 조회
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        const string sql = """
            SELECT warehouse_id AS WarehouseId,
                   wh_code     AS WhCode,
                   wh_name     AS WhName,
                   wh_type     AS WhType,
                   location    AS Location,
                   is_active   AS IsActive,
                   created_at  AS CreatedAt,
                   updated_at  AS UpdatedAt
              FROM warehouses
             WHERE tenant_id = @tenantId
               AND is_active = 1
             ORDER BY wh_code
            """;

        var list = await _db.QueryAsync<WarehouseDto>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return Ok(list);
    }

    /// <summary>
    /// 창고 신규 등록
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WarehouseCreateDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = Guid.NewGuid().ToString("N");

        const string sql = """
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
            VALUES (@id, @tenantId, @WhCode, @WhName, @WhType, @Location, 1, NOW(), NOW())
            """;

        await _db.ExecuteAsync(
            new CommandDefinition(sql, new { id, tenantId, dto.WhCode, dto.WhName, dto.WhType, dto.Location }, cancellationToken: ct));

        return Ok(new { warehouseId = id });
    }

    /// <summary>
    /// 창고 정보 수정
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] WarehouseCreateDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        const string sql = """
            UPDATE warehouses
               SET wh_code   = @WhCode,
                   wh_name   = @WhName,
                   wh_type   = @WhType,
                   location  = @Location,
                   updated_at = NOW()
             WHERE warehouse_id = @id
               AND tenant_id   = @tenantId
            """;

        var affected = await _db.ExecuteAsync(
            new CommandDefinition(sql, new { id, tenantId, dto.WhCode, dto.WhName, dto.WhType, dto.Location }, cancellationToken: ct));

        return affected > 0 ? Ok() : NotFound();
    }

    /// <summary>
    /// 창고 소프트 삭제 (is_active = 0)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        const string sql = """
            UPDATE warehouses
               SET is_active  = 0,
                   updated_at = NOW()
             WHERE warehouse_id = @id
               AND tenant_id   = @tenantId
            """;

        var affected = await _db.ExecuteAsync(
            new CommandDefinition(sql, new { id, tenantId }, cancellationToken: ct));

        return affected > 0 ? Ok() : NotFound();
    }
}

// --- DTO 클래스 ---

/// <summary>창고 조회 DTO</summary>
public class WarehouseDto
{
    public string WarehouseId { get; set; } = "";
    public string WhCode { get; set; } = "";
    public string WhName { get; set; } = "";
    public string WhType { get; set; } = "";
    public string? Location { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>창고 생성/수정 DTO</summary>
public class WarehouseCreateDto
{
    public string WhCode { get; set; } = "";
    public string WhName { get; set; } = "";
    public string WhType { get; set; } = "자사창고";
    public string? Location { get; set; }
}
