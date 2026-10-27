using System.Security.Claims;

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
    private int CallerUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    // ── Review Cycles ──

    [HttpGet("cycles")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<ReviewCycleResponseModel>>> GetCycles(CancellationToken ct)
        => Ok(await mediator.Send(new GetReviewCyclesQuery(), ct));

    [HttpGet("cycles/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ReviewCycleResponseModel>> GetCycle(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetReviewCycleQuery(id), ct));

    [HttpPost("cycles")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ReviewCycleResponseModel>> CreateCycle(
        [FromBody] CreateReviewCycleRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateReviewCycleCommand(request), ct);
        return Created($"/api/v1/reviews/cycles/{result.Id}", result);
    }

    [HttpPut("cycles/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ReviewCycleResponseModel>> UpdateCycle(
        int id, [FromBody] UpdateReviewCycleRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateReviewCycleCommand(id, request), ct));

    /// <summary>Create one review per in-scope employee and open the cycle.</summary>
    [HttpPost("cycles/{id:int}/launch")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LaunchReviewCycleResponseModel>> LaunchCycle(int id, CancellationToken ct)
        => Ok(await mediator.Send(new LaunchReviewCycleCommand(id, CallerUserId), ct));

    [HttpPost("cycles/{id:int}/close")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CloseCycle(int id, CancellationToken ct)
    {
        await mediator.Send(new CloseReviewCycleCommand(id), ct);
        return NoContent();
    }

    // ── Performance Reviews ──

    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<PerformanceReviewResponseModel>>> GetReviews(
        [FromQuery] int? cycleId, [FromQuery] int? employeeId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPerformanceReviewsQuery(cycleId, employeeId), ct));

    /// <summary>The caller's own reviews.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<PerformanceReviewResponseModel>>> GetMine([FromQuery] int? cycleId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPerformanceReviewsQuery(cycleId, null, GetPerformanceReviewsHandler.ViewMine), ct));

    /// <summary>The reviews the caller writes as reviewer.</summary>
    [HttpGet("team")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<PerformanceReviewResponseModel>>> GetTeam([FromQuery] int? cycleId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPerformanceReviewsQuery(cycleId, null, GetPerformanceReviewsHandler.ViewTeam), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PerformanceReviewDetailResponseModel>> GetReview(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetPerformanceReviewQuery(id), ct));

    /// <summary>Save a draft of the caller's part of the review; stages move through the actions below.</summary>
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

    [HttpPost("{id:int}/self-submit")]
    public async Task<ActionResult<PerformanceReviewResponseModel>> SubmitSelf(
        int id, [FromBody] SubmitSelfReviewRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new SubmitSelfReviewCommand(id, request), ct));

    [HttpPost("{id:int}/manager-submit")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<PerformanceReviewResponseModel>> SubmitManager(
        int id, [FromBody] SubmitManagerReviewRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new SubmitManagerReviewCommand(id, request), ct));

    [HttpPost("{id:int}/acknowledge")]
    public async Task<ActionResult<PerformanceReviewResponseModel>> Acknowledge(
        int id, [FromBody] AcknowledgeReviewRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new AcknowledgeReviewCommand(id, request), ct));

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<PerformanceReviewResponseModel>> CloseReview(int id, CancellationToken ct)
        => Ok(await mediator.Send(new ClosePerformanceReviewCommand(id), ct));
}

public record UpdateReviewRequestBody(
    ReviewStatus? Status,
    decimal? OverallRating,
    string? GoalsJson,
    string? CompetenciesJson,
    string? StrengthsComments,
    string? ImprovementComments,
    string? EmployeeSelfAssessment);
