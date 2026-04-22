using HitPan.API.Services;
using HitPan.Application.DTOs.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/delivery")]
[Authorize(Policy = "SalesOnly")]
public class DeliveryBatchController : ControllerBase
{
    private readonly IDeliveryBatchService _batchService;

    public DeliveryBatchController(IDeliveryBatchService batchService)
    {
        _batchService = batchService;
    }

    [HttpPost("batch")]
    public async Task<ActionResult<List<BatchSaveResult>>> BatchSave(
        [FromBody] List<CreateDeliveryRequest> requests,
        CancellationToken ct)
    {
        var result = await _batchService.SaveBatchAsync(requests, ct);
        return Ok(result);
    }
}
