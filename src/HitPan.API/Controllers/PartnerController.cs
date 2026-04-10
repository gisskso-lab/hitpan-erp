using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/partners")]
public class PartnerController : ControllerBase
{
    private readonly IPartnerService _partnerService;

    public PartnerController(IPartnerService partnerService)
    {
        _partnerService = partnerService;
    }

    [HttpGet("{id}/balance")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetBalance(string id, CancellationToken ct)
    {
        var balance = await _partnerService.GetBalanceAsync(id, ct);
        if (balance is null)
        {
            return NotFound();
        }

        return Ok(balance);
    }
}
