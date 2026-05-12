using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.PriceLists;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/price-lists")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-PRICELIST")]
public class PriceListsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PriceListListItemModel>>> GetPriceLists(
        [FromQuery] int? customerId)
    {
        var result = await mediator.Send(new GetPriceListsQuery(customerId));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PriceListResponseModel>> GetPriceList(int id)
    {
        var result = await mediator.Send(new GetPriceListByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PriceListListItemModel>> CreatePriceList(CreatePriceListRequestModel request)
    {
        var result = await mediator.Send(new CreatePriceListCommand(
            request.Name, request.Description, request.CustomerId,
            request.IsDefault, request.IsActive, request.EffectiveFrom, request.EffectiveTo,
            request.Entries));
        return CreatedAtAction(nameof(GetPriceList), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PriceListListItemModel>> UpdatePriceList(
        int id, UpdatePriceListRequestModel request)
    {
        var result = await mediator.Send(new UpdatePriceListCommand(
            id, request.Name, request.Description, request.IsDefault, request.IsActive,
            request.EffectiveFrom, request.EffectiveTo));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePriceList(int id)
    {
        await mediator.Send(new DeletePriceListCommand(id));
        return NoContent();
    }

    // --- Entry endpoints scoped under the parent list -----------------------

    [HttpGet("{id:int}/entries")]
    public async Task<ActionResult<PagedResponse<PriceListEntryResponseModel>>> GetEntries(
        int id,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await mediator.Send(new GetPriceListEntriesQuery(id, search, page, pageSize));
        return Ok(result);
    }

    [HttpPost("{id:int}/entries")]
    public async Task<ActionResult<PriceListEntryResponseModel>> CreateEntry(
        int id, CreatePriceListEntryRequestModel request)
    {
        var result = await mediator.Send(new CreatePriceListEntryCommand(
            id, request.PartId, request.UnitPrice, request.MinQuantity,
            request.Currency, request.Notes));
        return CreatedAtAction(nameof(PriceListEntriesController.GetEntry),
            controllerName: "PriceListEntries",
            routeValues: new { id = result.Id },
            value: result);
    }

    // --- CSV bulk import: dry-run preview + apply -------------------------
    // Two-step flow per the universal ERP convention surveyed in
    // phase-4-output/pricelist-entry-edit-ux.md. Preview is read-only; apply
    // commits via upsert by (partId, minQuantity).

    [HttpPost("{id:int}/entries/import-preview")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<BulkImportPreviewResponseModel>> PreviewImport(
        int id, IFormFile file)
    {
        var result = await mediator.Send(new PreviewPriceListEntryImportCommand(id, file));
        return Ok(result);
    }

    [HttpPost("{id:int}/entries/import-apply")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<BulkImportResultResponseModel>> ApplyImport(
        int id, IFormFile file)
    {
        var result = await mediator.Send(new ApplyPriceListEntryImportCommand(id, file));
        return Ok(result);
    }
}

/// <summary>
/// Flat-URL controller for individual <see cref="Forge.Core.Entities.PriceListEntry"/>
/// rows. Per the dispatch decision, GET-list and POST live under the parent
/// (<c>/price-lists/{id}/entries</c>) but PUT / DELETE live here at the flat
/// <c>/price-list-entries/{id}</c> path because the entry id is globally
/// unique.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-PRICELIST")]
public class PriceListEntriesController(IMediator mediator) : ControllerBase
{
    [HttpGet("price-list-entries/{id:int}")]
    public async Task<ActionResult<PriceListEntryResponseModel>> GetEntry(int id)
    {
        // Reused by the CreatedAtAction redirect on POST. We don't ship a
        // separate GET-by-id query handler; reuse the repo's FindEntryWithPart
        // to keep the surface small.
        var result = await mediator.Send(new GetPriceListEntryByIdQuery(id));
        return Ok(result);
    }

    [HttpPut("price-list-entries/{id:int}")]
    public async Task<ActionResult<PriceListEntryResponseModel>> UpdateEntry(
        int id, UpdatePriceListEntryRequestModel request)
    {
        var result = await mediator.Send(new UpdatePriceListEntryCommand(
            id, request.UnitPrice, request.MinQuantity, request.Currency, request.Notes));
        return Ok(result);
    }

    [HttpDelete("price-list-entries/{id:int}")]
    public async Task<IActionResult> DeleteEntry(int id)
    {
        await mediator.Send(new DeletePriceListEntryCommand(id));
        return NoContent();
    }

    // --- Customer-scoped read for the Customer detail Pricing tab ----------

    [HttpGet("customers/{customerId:int}/price-lists")]
    public async Task<ActionResult<List<PriceListListItemModel>>> GetForCustomer(int customerId)
    {
        var result = await mediator.Send(new GetPriceListsQuery(customerId));
        return Ok(result);
    }
}

