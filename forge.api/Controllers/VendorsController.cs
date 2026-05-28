using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/vendors")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /vendors?page=1&amp;pageSize=25&amp;sort=companyName&amp;order=asc&amp;q=acme&amp;isActive=true&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?search=&amp;isActive=</c> form continues
    /// to work — when both <c>q</c> and <c>search</c> are present, <c>q</c>
    /// wins. Existing UI callers that don't pass any query params get the
    /// standard default (page 1, 25 records, createdAt desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<VendorListItemModel>>> GetVendors(
        [FromQuery] VendorListQuery query,
        [FromQuery(Name = "search")] string? legacySearch,
        CancellationToken ct)
    {
        var effective = string.IsNullOrEmpty(query.Q) && !string.IsNullOrEmpty(legacySearch)
            ? query with { Q = legacySearch }
            : query;
        var result = await mediator.Send(new GetVendorsQuery(effective), ct);
        return Ok(result);
    }

    [HttpGet("dropdown")]
    public async Task<ActionResult<List<VendorResponseModel>>> GetVendorDropdown()
    {
        var result = await mediator.Send(new GetVendorDropdownQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorDetailResponseModel>> GetVendor(int id)
    {
        var result = await mediator.Send(new GetVendorByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<VendorListItemModel>> CreateVendor(CreateVendorRequestModel request)
    {
        var result = await mediator.Send(new CreateVendorCommand(
            request.CompanyName, request.ContactName, request.Email, request.Phone,
            request.Address, request.City, request.State, request.ZipCode,
            request.Country, request.PaymentTerms, request.Notes));
        return CreatedAtAction(nameof(GetVendor), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateVendor(int id, UpdateVendorRequestModel request)
    {
        await mediator.Send(new UpdateVendorCommand(
            id, request.CompanyName, request.ContactName, request.Email, request.Phone,
            request.Address, request.City, request.State, request.ZipCode,
            request.Country, request.PaymentTerms, request.Notes,
            request.OffTierVariancePct, request.IsActive));
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteVendor(int id)
    {
        await mediator.Send(new DeleteVendorCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/scorecard")]
    public async Task<ActionResult<VendorScorecardResponseModel>> GetVendorScorecard(
        int id,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo)
    {
        var result = await mediator.Send(new GetVendorScorecardQuery(id, dateFrom, dateTo));
        return Ok(result);
    }

    [HttpGet("performance-report")]
    public async Task<ActionResult<List<VendorComparisonRowModel>>> GetPerformanceReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo)
    {
        var result = await mediator.Send(new GetVendorPerformanceReportQuery(dateFrom, dateTo));
        return Ok(result);
    }
}
