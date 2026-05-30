using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts.PurchaseOptions;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// UoM purchase-options effort — CRUD for a part's purchasable sizes/forms (4×8 sheet, 1 kg bar,
/// bag of 100). Part-level master data; vendors price these via the price-tier surface. Gated by
/// the parts-master capability.
/// </summary>
[ApiController]
[Route("api/v1/parts/{partId:int}/purchase-options")]
[Authorize(Roles = "Admin,Manager,Engineer,OfficeManager")]
[RequiresCapability("CAP-MD-PARTS")]
public class PartPurchaseOptionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PartPurchaseOptionResponseModel>>> List(int partId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPartPurchaseOptionsQuery(partId), ct));

    [HttpPost]
    public async Task<ActionResult<PartPurchaseOptionResponseModel>> Create(
        int partId, [FromBody] CreatePartPurchaseOptionRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreatePartPurchaseOptionCommand(partId, request), ct);
        return Created($"/api/v1/parts/{partId}/purchase-options/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PartPurchaseOptionResponseModel>> Update(
        int partId, int id, [FromBody] UpdatePartPurchaseOptionRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdatePartPurchaseOptionCommand(partId, id, request), ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int partId, int id, CancellationToken ct)
    {
        await mediator.Send(new DeletePartPurchaseOptionCommand(partId, id), ct);
        return NoContent();
    }
}
