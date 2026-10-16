using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Parts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// The parts list. Procurement reads it to pick parts on purchase orders and RFQs. It is split
/// from <see cref="PartsController"/> because that controller's role list is ANDed with any
/// action-level list and leaves Procurement out of the part writes it guards.
/// </summary>
[ApiController]
[Route("api/v1/parts")]
[Authorize(Roles = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager,Procurement")]
[RequiresCapability("CAP-MD-PARTS")]
public class PartListController(IMediator mediator) : ControllerBase
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
}
