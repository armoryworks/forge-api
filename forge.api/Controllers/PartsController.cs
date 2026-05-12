using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Parts;
using Forge.Api.Features.Parts.PromoteStatus;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/parts")]
[Authorize(Roles = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager")]
[RequiresCapability("CAP-MD-PARTS")]
public class PartsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-partial / WU-17 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /parts?page=1&amp;pageSize=25&amp;sort=partNumber&amp;order=asc&amp;q=ASM&amp;status=Active&amp;type=Assembly&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31&amp;defaultVendorId=4</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?search=&amp;status=&amp;type=</c> form
    /// continues to work — <c>q</c> wins over <c>search</c> when both are
    /// supplied. Existing UI callers that don't pass any query params get the
    /// standard default (page 1, 25 records, createdAt desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<PartListResponseModel>>> GetParts(
        [FromQuery] PartListQuery query,
        [FromQuery(Name = "search")] string? legacySearch,
        CancellationToken ct)
    {
        var effective = string.IsNullOrEmpty(query.Q) && !string.IsNullOrEmpty(legacySearch)
            ? query with { Q = legacySearch }
            : query;
        var result = await mediator.Send(new GetPartsQuery(effective), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PartDetailResponseModel>> GetPartById(int id)
    {
        var result = await mediator.Send(new GetPartByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PartDetailResponseModel>> CreatePart([FromBody] CreatePartRequestModel request)
    {
        var result = await mediator.Send(new CreatePartCommand(
            request.Name, request.Description, request.Revision,
            request.ProcurementSource, request.InventoryClass,
            request.MaterialSpecId));
        return Created($"/api/v1/parts/{result.Id}", result);
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<PartDetailResponseModel>> UpdatePart(int id, [FromBody] UpdatePartRequestModel request)
    {
        var result = await mediator.Send(new UpdatePartCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/bom")]
    public async Task<ActionResult<PartDetailResponseModel>> CreateBOMEntry(int id, [FromBody] CreateBOMEntryRequestModel request)
    {
        var result = await mediator.Send(new CreateBOMEntryCommand(id, request));
        return Created($"/api/v1/parts/{id}", result);
    }

    [HttpPatch("{id:int}/bom/{bomEntryId:int}")]
    public async Task<ActionResult<PartDetailResponseModel>> UpdateBOMEntry(int id, int bomEntryId, [FromBody] UpdateBOMEntryRequestModel request)
    {
        var result = await mediator.Send(new UpdateBOMEntryCommand(id, bomEntryId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/bom/{bomEntryId:int}")]
    public async Task<ActionResult<PartDetailResponseModel>> DeleteBOMEntry(int id, int bomEntryId)
    {
        var result = await mediator.Send(new DeleteBOMEntryCommand(id, bomEntryId));
        return Ok(result);
    }

    /// <summary>Phase 3 H4 / WU-20 — list all BOM revisions for a part.</summary>
    [HttpGet("{id:int}/bom/revisions")]
    public async Task<ActionResult<List<BomRevisionSummaryResponseModel>>> GetBomRevisions(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBomRevisionsQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Phase 3 H4 / WU-20 — read one immutable BOM revision in detail.</summary>
    [HttpGet("{id:int}/bom/revisions/{revId:int}")]
    public async Task<ActionResult<BomRevisionDetailResponseModel>> GetBomRevisionById(int id, int revId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBomRevisionByIdQuery(id, revId), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePart(int id)
    {
        await mediator.Send(new DeletePartCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Workflow Pattern Phase 3 — Authoritative Part status promotion.
    /// Runs entity readiness validators server-side; success → 200 with the
    /// updated detail, failure → 409 with the missing-validators envelope.
    /// </summary>
    [HttpPost("{id:int}/promote-status")]
    public async Task<ActionResult<PartDetailResponseModel>> PromoteStatus(
        int id, [FromBody] PromoteEntityStatusRequestModel body, CancellationToken ct)
    {
        var result = await mediator.Send(new PromotePartStatusCommand(id, body), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/revisions")]
    public async Task<ActionResult<List<PartRevisionResponseModel>>> GetPartRevisions(int id)
    {
        var result = await mediator.Send(new GetPartRevisionsQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/revisions")]
    public async Task<ActionResult<PartRevisionResponseModel>> CreatePartRevision(int id, [FromBody] CreatePartRevisionRequestModel request)
    {
        var result = await mediator.Send(new CreatePartRevisionCommand(
            id, request.Revision, request.ChangeDescription, request.ChangeReason, request.EffectiveDate));
        return Created($"/api/v1/parts/{id}/revisions/{result.Id}", result);
    }

    [HttpGet("{id:int}/operations")]
    public async Task<ActionResult<List<OperationResponseModel>>> GetOperations(int id)
        => Ok(await mediator.Send(new GetOperationsQuery(id)));

    [HttpPost("{id:int}/operations")]
    public async Task<ActionResult<OperationResponseModel>> CreateOperation(int id, [FromBody] CreateOperationRequestModel request)
        => StatusCode(201, await mediator.Send(new CreateOperationCommand(id, request)));

    [HttpPatch("{id:int}/operations/{operationId:int}")]
    public async Task<ActionResult<OperationResponseModel>> UpdateOperation(int id, int operationId, [FromBody] UpdateOperationRequestModel request)
        => Ok(await mediator.Send(new UpdateOperationCommand(id, operationId, request)));

    [HttpDelete("{id:int}/operations/{operationId:int}")]
    public async Task<IActionResult> DeleteOperation(int id, int operationId)
    {
        await mediator.Send(new DeleteOperationCommand(id, operationId));
        return NoContent();
    }

    [HttpPost("{id:int}/operations/{operationId:int}/materials")]
    public async Task<ActionResult<OperationMaterialResponseModel>> CreateOperationMaterial(int id, int operationId, [FromBody] CreateOperationMaterialRequestModel request)
        => StatusCode(201, await mediator.Send(new CreateOperationMaterialCommand(id, operationId, request)));

    [HttpDelete("{id:int}/operations/{operationId:int}/materials/{materialId:int}")]
    public async Task<IActionResult> DeleteOperationMaterial(int id, int operationId, int materialId)
    {
        await mediator.Send(new DeleteOperationMaterialCommand(id, operationId, materialId));
        return NoContent();
    }

    [HttpGet("{id:int}/operations/{operationId:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetOperationActivity(int id, int operationId)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Operation", operationId));
        return Ok(result);
    }

    [HttpPost("{id:int}/operations/{operationId:int}/activity")]
    public async Task<IActionResult> AddOperationComment(int id, int operationId, [FromBody] AddOperationCommentRequestModel request)
    {
        await mediator.Send(new AddOperationCommentCommand(id, operationId, request.Comment));
        return StatusCode(201);
    }

    [HttpPost("{id:int}/link-accounting-item")]
    public async Task<IActionResult> LinkAccountingItem(int id, [FromBody] LinkAccountingItemRequestModel request)
    {
        await mediator.Send(new LinkPartToAccountingItemCommand(id, request.ExternalId, request.ExternalRef));
        return NoContent();
    }

    [HttpDelete("{id:int}/link-accounting-item")]
    public async Task<IActionResult> UnlinkAccountingItem(int id)
    {
        await mediator.Send(new UnlinkPartFromAccountingItemCommand(id));
        return NoContent();
    }

    [HttpGet("thumbnails")]
    public async Task<ActionResult<List<PartThumbnailResponseModel>>> GetThumbnails([FromQuery] List<int> partIds)
    {
        var result = await mediator.Send(new GetPartThumbnailsQuery(partIds));
        return Ok(result);
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetPartActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Part", id));
        return Ok(result);
    }

    // ── Pricing ───────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/prices")]
    public async Task<ActionResult<List<PartPriceResponseModel>>> GetPrices(int id)
    {
        var result = await mediator.Send(new GetPartPricesQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/prices")]
    public async Task<ActionResult<PartPriceResponseModel>> AddPrice(
        int id, [FromBody] AddPartPriceRequestModel model)
    {
        var result = await mediator.Send(new AddPartPriceCommand(id, model.UnitPrice, model.Currency, model.EffectiveFrom, model.Notes));
        return CreatedAtAction(nameof(GetPrices), new { id }, result);
    }

    [HttpDelete("{id:int}/prices/{priceId:int}")]
    public async Task<IActionResult> DeletePrice(int id, int priceId)
    {
        await mediator.Send(new DeletePartPriceCommand(id, priceId));
        return NoContent();
    }

    // ── Alternates ───────────────────────────────────────────────────────────

    [HttpGet("{id:int}/alternates")]
    public async Task<ActionResult<List<PartAlternateResponseModel>>> GetAlternates(int id)
    {
        var result = await mediator.Send(new GetPartAlternatesQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/alternates")]
    public async Task<ActionResult<PartAlternateResponseModel>> CreateAlternate(
        int id, [FromBody] CreatePartAlternateRequestModel request)
    {
        var result = await mediator.Send(new CreatePartAlternateCommand(id, request));
        return Created($"/api/v1/parts/{id}/alternates/{result.Id}", result);
    }

    [HttpPatch("{id:int}/alternates/{alternateId:int}")]
    public async Task<ActionResult<PartAlternateResponseModel>> UpdateAlternate(
        int id, int alternateId, [FromBody] UpdatePartAlternateRequestModel request)
    {
        var result = await mediator.Send(new UpdatePartAlternateCommand(id, alternateId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/alternates/{alternateId:int}")]
    public async Task<IActionResult> DeleteAlternate(int id, int alternateId)
    {
        await mediator.Send(new DeletePartAlternateCommand(id, alternateId));
        return NoContent();
    }

    // ── Inventory Summary ────────────────────────────────────────────────────

    [HttpGet("{id:int}/inventory-summary")]
    public async Task<ActionResult<PartInventorySummaryResponseModel>> GetInventorySummary(int id)
    {
        var result = await mediator.Send(new GetPartInventorySummaryQuery(id));
        return Ok(result);
    }

    // ── Purchase Order history (backward-from-part view) ─────────────────────
    //
    // Capped at 50 rows server-side (see GetPartPurchaseHistoryHandler.MaxRows).
    // Optional `search` term filters on PO #, vendor name, line description.
    // Capability-gated by CAP-P2P-PO so installs with PO disabled don't
    // surface a tab that would always be empty + confuse the user.

    [HttpGet("{id:int}/purchase-history")]
    [RequiresCapability("CAP-P2P-PO")]
    public async Task<ActionResult<List<PartPurchaseHistoryItemResponseModel>>> GetPurchaseHistory(
        int id, [FromQuery] string? search = null)
    {
        var result = await mediator.Send(new GetPartPurchaseHistoryQuery(id, search));
        return Ok(result);
    }

    // Bought-parts effort PR3 — landed cost surface. Capability-gated by
    // CAP-P2P-PO since landed cost is a PO-derived view; no PO history =
    // an empty result anyway, but this keeps the route family consistent.
    [HttpGet("{id:int}/landed-cost")]
    [RequiresCapability("CAP-P2P-PO")]
    public async Task<ActionResult<PartLandedCostResponseModel>> GetLandedCost(
        int id, [FromQuery] int maxReceipts = 3)
    {
        var result = await mediator.Send(new GetPartLandedCostQuery(id, maxReceipts));
        return Ok(result);
    }
}
