using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorParts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Pillar 3 — VendorPart CRUD. Owns the (Vendor, Part) intersection rows
/// plus their tiered-pricing children. Convenience routes for browsing the
/// catalog from a Part or a Vendor live on this controller too — they share
/// the same handlers, so cohesion outweighs putting them on Parts/Vendors.
/// Gated by the vendor-master capability.
/// </summary>
[ApiController]
[Route("api/v1/vendor-parts")]
[Authorize(Roles = "Admin,Manager,Engineer,OfficeManager")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorPartsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<VendorPartResponseModel>> CreateVendorPart(
        [FromBody] CreateVendorPartRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateVendorPartCommand(request), ct);
        return Created($"/api/v1/vendor-parts/{result.Id}", result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorPartResponseModel>> GetVendorPart(
        int id,
        [FromQuery] bool showHistory,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetVendorPartQuery(id, showHistory), ct);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<VendorPartResponseModel>> UpdateVendorPart(
        int id,
        [FromBody] UpdateVendorPartRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateVendorPartCommand(id, request), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteVendorPart(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteVendorPartCommand(id), ct);
        return NoContent();
    }

    // ── Convenience listing routes (cross-controller) ─────────────────────

    /// <summary>
    /// Catalog of vendors that source a given Part — Sources tab on the part
    /// detail page. Sorted preferred → approved → name.
    /// </summary>
    [HttpGet("/api/v1/parts/{partId:int}/vendor-parts")]
    public async Task<ActionResult<List<VendorPartResponseModel>>> ListByPart(
        int partId,
        [FromQuery] bool showHistory,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListVendorPartsByPartQuery(partId, showHistory), ct);
        return Ok(result);
    }

    /// <summary>
    /// Catalog of parts a given Vendor sources — Catalog tab on the vendor
    /// detail page. Sorted by part number.
    /// </summary>
    [HttpGet("/api/v1/vendors/{vendorId:int}/vendor-parts")]
    public async Task<ActionResult<List<VendorPartResponseModel>>> ListByVendor(
        int vendorId, CancellationToken ct)
    {
        var result = await mediator.Send(new ListVendorPartsByVendorQuery(vendorId), ct);
        return Ok(result);
    }

    // ── CSV bulk import: dry-run preview + apply ──────────────────────────
    // Two-step flow mirroring the price-list-entry importer. Preview is
    // read-only; apply upserts by (vendorId, partId). Scoped under the vendor
    // since you import a single vendor's catalog at a time.

    [HttpPost("/api/v1/vendors/{vendorId:int}/vendor-parts/import-preview")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<VendorPartImportPreviewResponseModel>> PreviewImport(
        int vendorId, IFormFile file, CancellationToken ct)
    {
        var result = await mediator.Send(new PreviewVendorPartImportCommand(vendorId, file), ct);
        return Ok(result);
    }

    [HttpPost("/api/v1/vendors/{vendorId:int}/vendor-parts/import-apply")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<VendorPartImportResultResponseModel>> ApplyImport(
        int vendorId, IFormFile file, CancellationToken ct)
    {
        var result = await mediator.Send(new ApplyVendorPartImportCommand(vendorId, file), ct);
        return Ok(result);
    }

    // ── Price tier routes ─────────────────────────────────────────────────

    [HttpPost("{vendorPartId:int}/price-tiers")]
    public async Task<ActionResult<VendorPartPriceTierResponseModel>> UpsertPriceTier(
        int vendorPartId,
        [FromBody] UpsertVendorPartPriceTierRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UpsertVendorPartPriceTierCommand(vendorPartId, request), ct);
        return Ok(result);
    }

    [HttpDelete("{vendorPartId:int}/price-tiers/{tierId:int}")]
    public async Task<IActionResult> DeletePriceTier(int vendorPartId, int tierId, CancellationToken ct)
    {
        await mediator.Send(new DeleteVendorPartPriceTierCommand(vendorPartId, tierId), ct);
        return NoContent();
    }

    /// <summary>
    /// Read-only history of all price-tier rows for a VendorPart — current +
    /// closed — sorted by EffectiveFrom DESC then MinQuantity ASC. Powers the
    /// price-tier history dialog on the Vendor catalog.
    /// </summary>
    [HttpGet("{vendorPartId:int}/price-tiers/history")]
    public async Task<ActionResult<List<VendorPartPriceTierResponseModel>>> GetPriceTierHistory(
        int vendorPartId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetVendorPartPriceTierHistoryQuery(vendorPartId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Bought-parts effort PR4 — variance check for the off-tier prompt.
    /// Single-call evaluation of every line on a draft PO so the UI shows
    /// one consolidated prompt instead of one-per-line. See
    /// <see cref="CheckTierVarianceHandler"/> for resolution rules.
    /// </summary>
    [HttpPost("check-tier-variance")]
    public async Task<ActionResult<CheckTierVarianceResponseModel>> CheckTierVariance(
        [FromBody] CheckTierVarianceRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CheckTierVarianceQuery(request.VendorId, request.Lines), ct);
        return Ok(result);
    }
}
