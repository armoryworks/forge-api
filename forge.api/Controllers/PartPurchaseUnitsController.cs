using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts.PurchaseUnits;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// UoM purchase-units effort — CRUD for a part's purchasable sizes/forms (4×8 sheet, 1 kg bar,
/// bag of 100). Part-level master data; vendors price these via the price-tier surface. Gated by
/// the parts-master capability.
/// </summary>
[ApiController]
[Route("api/v1/parts/{partId:int}/purchase-units")]
[Authorize(Roles = "Admin,Manager,Engineer,OfficeManager")]
[RequiresCapability("CAP-MD-PARTS")]
public class PartPurchaseUnitsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PartPurchaseUnitResponseModel>>> List(int partId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPartPurchaseUnitsQuery(partId), ct));

    [HttpPost]
    public async Task<ActionResult<PartPurchaseUnitResponseModel>> Create(
        int partId, [FromBody] CreatePartPurchaseUnitRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreatePartPurchaseUnitCommand(partId, request), ct);
        return Created($"/api/v1/parts/{partId}/purchase-units/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PartPurchaseUnitResponseModel>> Update(
        int partId, int id, [FromBody] UpdatePartPurchaseUnitRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdatePartPurchaseUnitCommand(partId, id, request), ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int partId, int id, CancellationToken ct)
    {
        await mediator.Send(new DeletePartPurchaseUnitCommand(partId, id), ct);
        return NoContent();
    }
}
