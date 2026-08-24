using HitPan.Application.DTOs.Bom;
using HitPan.Application.Interfaces;
using HitPan.Contracts.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/bom")]
[Authorize]
public class BomController : ControllerBase
{
    private readonly IBomService _svc;

    public BomController(IBomService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetListAsync(tid, ct).ConfigureAwait(false));
    }

    [HttpGet("by-item/{itemId}")]
    public async Task<IActionResult> GetByItem(string itemId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var bomId = await _svc.GetBomIdByItemAsync(itemId, tid, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(bomId)) return NotFound();
        return Ok(new { bomId });
    }

    /// <summary>
    /// BOM 조립 직후 자재 부족·안전재고 위반 자동발주 후보 조회.
    /// 사장님 지시 (2026-04-26): 부족 다이얼로그 → OK 시 발주서 생성.
    /// </summary>
    [HttpGet("{id:guid}/auto-order-candidates")]
    public async Task<IActionResult> GetAssembleAutoOrderCandidates(
        string id,
        [FromQuery] decimal produceQty,
        CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        // 사장님 헌법 (2026-04-26): produceQty 0 이하면 1로 안전 처리 (기본 호출 호환).
        var qty = produceQty > 0 ? produceQty : 1m;
        var list = await _svc.GetAssembleAutoOrderCandidatesAsync(id, tid, qty, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.GetAsync(id, tid, ct).ConfigureAwait(false);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBomDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            var id = await _svc.CreateAsync(dto, tid, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateBomDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            await _svc.UpdateAsync(id, dto, tid, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DeleteAsync(id, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("{id:guid}/register-item")]
    public async Task<IActionResult> RegisterBomAsItem(string id, [FromBody] RegisterBomItemRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            var itemId = await _svc.RegisterBomAsItemAsync(id, req.ItemType, tid, ct).ConfigureAwait(false);
            return Ok(new { itemId, message = "상품마스터에 등록되었습니다." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public class RegisterBomItemRequest
    {
        public string ItemType { get; set; } = "semi_finished";
    }

    [HttpPost("{id:guid}/check-assemble")]
    public async Task<IActionResult> CheckAssemble(string id, [FromBody] decimal produceQty, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.CheckAssembleAsync(id, produceQty, tid, ct).ConfigureAwait(false);
        return Ok(result);
    }

    // 멱등 보호 (작6, 2026-06-26): 더블클릭·재시도로 같은 생산 1회가 2번 전송돼도 재고 2배 차단.
    //   같은 Idempotency-Key+같은 본문 = 캐시 재응답(재고 무변경). 다른 키 = 정상 반복생산 통과(헌법 #20 보존).
    [HttpPost("assemble")]
    [IdempotencyKey]
    public async Task<IActionResult> Assemble([FromBody] BomAssembleDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            await _svc.AssembleAsync(dto, tid, uid, ct).ConfigureAwait(false);
            return Ok(new { message = "조립이 완료됐습니다. 완성품 재고가 반영됐습니다." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 멱등 보호 (작6, 2026-06-26): 생산과 동일 — 중복 해체 차단, 정상 반복해체 보존.
    [HttpPost("disassemble")]
    [IdempotencyKey]
    public async Task<IActionResult> Disassemble([FromBody] BomAssembleDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            await _svc.DisassembleAsync(dto, tid, uid, ct).ConfigureAwait(false);
            return Ok(new { message = "조립 해체가 완료됐습니다. 완성품 재고는 차감되고 자재 재고가 복귀됐습니다." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAlertsAsync(tid, ct).ConfigureAwait(false));
    }

    [HttpPost("alerts/{alertId}/dismiss")]
    public async Task<IActionResult> Dismiss(string alertId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DismissAlertAsync(alertId, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    /// 안전재고 알림 발주. <paramref name="autoReceive"/> 가 true 면 매입확정까지 시도한다 (20260825작1 W2).
    /// </summary>
    /// <remarks>
    /// 🔴 결과를 그대로 돌려준다 — 화면이 <b>발주만 됐는지 매입확정까지 갔는지</b>에 따라
    /// 다른 안내를 띄워야 하기 때문이다(사장님 8/25 지시).
    /// </remarks>
    [HttpPost("alerts/{alertId}/order")]
    public async Task<IActionResult> Order(string alertId, [FromQuery] bool autoReceive, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.OrderAlertAsync(alertId, tid, autoReceive, ct).ConfigureAwait(false);
        return Ok(result);
    }
}
