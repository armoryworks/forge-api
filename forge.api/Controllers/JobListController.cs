using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// The job list. Procurement reads it to link a purchase order to the work order it buys for.
/// It is split from <see cref="JobsController"/> because that controller's role list is ANDed with
/// any action-level list and leaves Procurement out of the job writes it guards.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Authorize(Roles = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager,Procurement")]
[RequiresCapability("CAP-MFG-WO-RELEASE")]
public class JobListController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract for the
    /// table-view of jobs.
    ///
    /// New shape:
    ///   <c>GET /jobs?page=1&amp;pageSize=25&amp;sort=createdAt&amp;order=desc&amp;q=widget&amp;stageId=4&amp;customerId=2&amp;assigneeId=7&amp;dateFrom=2025-01-01</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: legacy query params (<c>trackTypeId</c>,
    /// <c>stageId</c>, <c>assigneeId</c>, <c>isArchived</c>, <c>search</c>,
    /// <c>customerId</c>) continue to bind directly via the JobListQuery
    /// model. The legacy <c>?search=</c> is plumbed into <c>q</c> when
    /// <c>q</c> is not supplied.
    ///
    /// Kanban-board view (<c>/api/v1/kanban-cards</c>) and the
    /// <c>/jobs/calendar.ics</c> export retain their existing specialised
    /// query semantics.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<JobListResponseModel>>> GetJobs(
        [FromQuery] JobListQuery query,
        [FromQuery(Name = "search")] string? legacySearch,
        CancellationToken ct)
    {
        var effective = string.IsNullOrEmpty(query.Q) && !string.IsNullOrEmpty(legacySearch)
            ? query with { Q = legacySearch }
            : query;
        var result = await mediator.Send(new GetJobsQuery(effective), ct);
        return Ok(result);
    }
}
