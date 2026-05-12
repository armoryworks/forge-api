using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Reviews;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/reviews")]
[Authorize]
[RequiresCapability("CAP-HR-REVIEW")]
public class ReviewsController(IMediator mediator) : ControllerBase
{
    // ── Review Cycles ──

    [HttpGet("cycles")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<ReviewCycleResponseModel>>> GetCycles(CancellationToken ct)
        => Ok(await mediator.Send(new GetReviewCyclesQuery(), ct));

    [HttpPost("cycles")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ReviewCycleResponseModel>> CreateCycle(
        [FromBody] CreateReviewCycleRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateReviewCycleCommand(request), ct);
        return Created($"/api/v1/reviews/cycles/{result.Id}", result);
    }

    // ── Performance Reviews ──

    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<PerformanceReviewResponseModel>>> GetReviews(
        [FromQuery] int? cycleId, [FromQuery] int? employeeId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPerformanceReviewsQuery(cycleId, employeeId), ct));

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<PerformanceReviewResponseModel>> UpdateReview(
        int id, [FromBody] UpdateReviewRequestBody body, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdatePerformanceReviewCommand(
            id, body.Status, body.OverallRating,
            body.GoalsJson, body.CompetenciesJson,
            body.StrengthsComments, body.ImprovementComments,
            body.EmployeeSelfAssessment), ct);
        return Ok(result);
    }
}

public record UpdateReviewRequestBody(
    ReviewStatus? Status,
    decimal? OverallRating,
    string? GoalsJson,
    string? CompetenciesJson,
    string? StrengthsComments,
    string? ImprovementComments,
    string? EmployeeSelfAssessment);
