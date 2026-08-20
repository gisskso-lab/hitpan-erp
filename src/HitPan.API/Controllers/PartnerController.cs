using System.Security.Claims;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/partners")]
[Authorize]
public class PartnerController : ControllerBase
{
    private readonly IPartnerService _partnerService;

    public PartnerController(IPartnerService partnerService)
    {
        _partnerService = partnerService;
    }

    [HttpGet]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetList([FromQuery] string? search, [FromQuery] string? type, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _partnerService.GetPartnerListAsync(tenantId, search, type, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>
    /// 서버 페이지네이션 버전 (2026-05-13 야간 신규, 헌법 #25 정공법).
    /// 기존 GetList(/) 유지 — Razor가 ServerData 패턴으로 전환 시 이 엔드포인트 사용.
    /// </summary>
    [HttpGet("paged")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetListPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var req = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var result = await _partnerService.GetPartnerListPagedAsync(tenantId, req, type, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("search")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> SearchPartners([FromQuery] string q, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _partnerService.SearchPartnersAsync(tenantId, q, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetDetail(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var row = await _partnerService.GetPartnerDetailAsync(id, tenantId, ct).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Ok(row);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreatePartnerDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var newId = await _partnerService.CreatePartnerAsync(dto, tenantId, ct).ConfigureAwait(false);
            return Ok(new { partnerId = newId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePartnerDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            await _partnerService.UpdatePartnerAsync(id, dto, tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.DeletePartnerAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("{id}/master-special-prices")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetMasterSpecialPrices(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _partnerService.GetPartnerSpecialPricesAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost("{id}/master-special-prices")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> UpsertMasterSpecialPrice(string id, [FromBody] PartnerSpecialPriceDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.UpsertPartnerSpecialPriceAsync(id, dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("master-special-prices/{priceId}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> DeleteMasterSpecialPrice(string priceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.DeletePartnerSpecialPriceByIdAsync(priceId, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>AR Aging 연체 버킷 — 전 거래처 or 특정 거래처 연체 현황.</summary>
    [HttpGet("aging")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetAging([FromServices] System.Data.IDbConnection db, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        const string sql = """
            SELECT partner_id AS PartnerId, partner_name AS PartnerName,
                   open_invoices AS OpenInvoices,
                   bucket_0_30 AS Bucket0_30, bucket_31_60 AS Bucket31_60,
                   bucket_61_90 AS Bucket61_90, bucket_90_plus AS Bucket90Plus,
                   total_unpaid AS TotalUnpaid
            FROM v_partner_aging_buckets
            WHERE tenant_id = @TenantId
            ORDER BY total_unpaid DESC
            """;

        var rows = await Dapper.SqlMapper.QueryAsync(db, new Dapper.CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
        return Ok(rows);
    }

    [HttpGet("{id}/balance")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetBalance(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var balance = await _partnerService.GetBalanceAsync(id, ct);
        if (balance is null)
        {
            return NotFound();
        }

        return Ok(balance);
    }

    [HttpGet("{id}/special-prices")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetSpecialPrices(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var employeeId = User.FindFirst("employee_id")?.Value;
        if (role == "sales_user")
        {
            var ok = await _partnerService.IsAssignedPartnerAsync(employeeId, id, tenantId, ct);
            if (!ok) return Forbid();
        }

        var result = await _partnerService.GetSpecialPricesAsync(id, tenantId, ct);
        return Ok(result);
    }

    [HttpPost("{id}/special-prices")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> UpsertSpecialPrice(string id, [FromBody] SpecialPriceUpsertDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var userId = User.FindFirst("employee_id")?.Value ?? string.Empty;
        await _partnerService.UpsertSpecialPriceAsync(id, dto, tenantId, userId, ct);
        return Ok();
    }

    /// <summary>
    /// 단가 참고값 4종을 읽는다 — 명세서 화면 말풍선용 (20260820작4 · 설계2).
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 설계: <i>"마우스 커서 갖다대면, 업체특별단가·최종단가·표준단가·혹은
    /// 상품특별단가를 고객이 볼 수 있도록"</i>
    ///
    /// <para>
    /// ⚠️ <paramref name="purchase"/> 는 <b>최종단가의 출처</b>를 가른다 —
    /// 발주·매입·반품은 <c>true</c>(산 값), 견적·수주·판매는 <c>false</c>(판 값).
    /// </para>
    ///
    /// <para>
    /// 🔴 권한은 <b>조회 계열</b>(<c>SalesOnly</c>)로 둔다. 단가 <b>등록</b>은
    /// <c>SalesManager</c> 지만, 이 값은 명세서를 쓰는 실무자가 <b>보기만</b> 하는 것이라
    /// 등록 권한을 요구하면 <b>정작 쓸 사람이 못 본다.</b>
    /// ⚠️ 담당업체 제한(<c>sales_user</c>)은 위 조회 API 와 <b>똑같이</b> 건다 — 남의 업체 단가는 못 본다.
    /// </para>
    /// </remarks>
    [HttpGet("{id}/price-hint/{itemId}")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetPriceHint(
        string id, string itemId, [FromQuery] bool purchase, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var employeeId = User.FindFirst("employee_id")?.Value;
        if (role == "sales_user")
        {
            var ok = await _partnerService.IsAssignedPartnerAsync(employeeId, id, tenantId, ct);
            if (!ok) return Forbid();
        }

        var hint = await _partnerService.GetPriceHintAsync(id, itemId, tenantId, purchase, ct)
            .ConfigureAwait(false);
        return Ok(hint);
    }

    [HttpDelete("{id}/special-prices/{itemId}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> DeleteSpecialPrice(string id, string itemId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _partnerService.DeleteSpecialPriceAsync(id, itemId, tenantId, ct);
        return Ok();
    }
}
