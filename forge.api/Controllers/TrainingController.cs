using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Training;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/training")]
[Authorize]
// The training LMS is always available — it is no longer capability-gated. Other
// capabilities influence which content is relevant, but the feature can't be removed.
public class TrainingController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin() => User.IsInRole("Admin");

    // Modules

    [HttpGet("modules")]
    public async Task<ActionResult<TrainingPaginatedResult<TrainingModuleListItemResponseModel>>> GetModules(
        [FromQuery] string? search = null,
        [FromQuery] string? contentType = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool includeUnpublished = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetTrainingModulesQuery(GetUserId(), IsAdmin(), search, contentType, tag, includeUnpublished, page, pageSize, lang), ct);
        return Ok(result);
    }

    // Must be declared BEFORE {id:int} to avoid routing conflict
    [HttpGet("modules/by-route")]
    public async Task<ActionResult<List<TrainingModuleListItemResponseModel>>> GetModulesByRoute(
        [FromQuery] string route,
        [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetModulesByRouteQuery(route, GetUserId(), lang), ct);
        return Ok(result);
    }

    [HttpGet("modules/{id:int}")]
    public async Task<ActionResult<TrainingModuleDetailResponseModel>> GetModule(int id, [FromQuery] string? lang = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTrainingModuleQuery(id, GetUserId(), IsAdmin(), lang), ct);
        return Ok(result);
    }

    [HttpPost("modules")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TrainingModuleDetailResponseModel>> CreateModule(
        [FromBody] CreateTrainingModuleRequestModel model,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateTrainingModuleCommand(
            model.Title, model.Slug, model.Summary, model.ContentType, model.ContentJson,
            model.CoverImageUrl, model.EstimatedMinutes, model.Tags, model.AppRoutes,
            model.IsPublished, model.IsOnboardingRequired, model.SortOrder, GetUserId()), ct);
        return CreatedAtAction(nameof(GetModule), new { id = result.Id }, result);
    }

    [HttpPut("modules/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TrainingModuleDetailResponseModel>> UpdateModule(
        int id,
        [FromBody] UpdateTrainingModuleRequestModel model,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateTrainingModuleCommand(
            id, model.Title, model.Slug, model.Summary, model.ContentType, model.ContentJson,
            model.CoverImageUrl, model.EstimatedMinutes, model.Tags, model.AppRoutes,
            model.IsPublished, model.IsOnboardingRequired, model.SortOrder), ct);
        return Ok(result);
    }

    [HttpDelete("modules/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteModule(int id, CancellationToken ct = default)
    {
        await mediator.Send(new DeleteTrainingModuleCommand(id), ct);
        return NoContent();
    }

    [HttpPatch("modules/{moduleId:int}/walkthrough-steps")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TrainingModuleDetailResponseModel>> UpdateWalkthroughSteps(
        int moduleId,
        [FromBody] UpdateWalkthroughStepsRequestModel model,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateWalkthroughStepsCommand(moduleId, model.Steps), ct);
        return Ok(result);
    }

    /// <summary>
    /// Navigates to the module's target page in a headless browser (authenticated as the
    /// calling admin), extracts the live DOM, sends it to Ollama, and saves generated
    /// driver.js tour steps back to the module's ContentJson.
    /// </summary>
    [HttpPost("modules/{id:int}/generate-walkthrough")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GenerateWalkthroughResponseModel>> GenerateWalkthrough(
        int id,
        CancellationToken ct = default)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var jwtToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : string.Empty;

        if (string.IsNullOrEmpty(jwtToken))
            return Unauthorized("A Bearer token is required for walkthrough generation.");

        var result = await mediator.Send(new GenerateWalkthroughStepsCommand(id, jwtToken), ct);
        return Ok(result);
    }

    // Paths

    [HttpGet("paths")]
    public async Task<ActionResult<List<TrainingPathResponseModel>>> GetPaths([FromQuery] string? lang = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTrainingPathsQuery(GetUserId(), IsAdmin(), lang), ct);
        return Ok(result);
    }

    [HttpGet("paths/{id:int}")]
    public async Task<ActionResult<TrainingPathResponseModel>> GetPath(int id, [FromQuery] string? lang = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTrainingPathQuery(id, GetUserId(), IsAdmin(), lang), ct);
        return Ok(result);
    }

    [HttpPost("paths")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TrainingPathResponseModel>> CreatePath(
        [FromBody] CreateTrainingPathRequestModel model, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateTrainingPathCommand(model), ct);
        return CreatedAtAction(nameof(GetPath), new { id = result.Id }, result);
    }

    [HttpPut("paths/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TrainingPathResponseModel>> UpdatePath(
        int id, [FromBody] UpdateTrainingPathRequestModel model, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateTrainingPathCommand(id, model), ct);
        return Ok(result);
    }

    [HttpDelete("paths/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeletePath(int id, CancellationToken ct = default)
    {
        await mediator.Send(new DeleteTrainingPathCommand(id), ct);
        return NoContent();
    }

    // Enrollments

    [HttpPost("enrollments")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EnrollUser([FromBody] EnrollUserRequestModel model, CancellationToken ct = default)
    {
        await mediator.Send(new EnrollUserCommand(model.UserId, model.PathId, GetUserId()), ct);
        return NoContent();
    }

    // My endpoints

    [HttpGet("my-enrollments")]
    public async Task<ActionResult<List<TrainingEnrollmentResponseModel>>> GetMyEnrollments(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyEnrollmentsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpGet("my-progress")]
    public async Task<ActionResult<List<TrainingProgressResponseModel>>> GetMyProgress(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyProgressQuery(GetUserId()), ct);
        return Ok(result);
    }

    // Progress tracking

    [HttpPost("progress/{moduleId:int}/start")]
    public async Task<IActionResult> StartModule(int moduleId, CancellationToken ct = default)
    {
        await mediator.Send(new RecordModuleStartCommand(GetUserId(), moduleId), ct);
        return NoContent();
    }

    [HttpPost("progress/{moduleId:int}/heartbeat")]
    public async Task<IActionResult> Heartbeat(int moduleId, [FromBody] HeartbeatRequestModel model, CancellationToken ct = default)
    {
        await mediator.Send(new RecordProgressHeartbeatCommand(GetUserId(), moduleId, model.Seconds), ct);
        return NoContent();
    }

    [HttpPost("progress/{moduleId:int}/complete")]
    public async Task<IActionResult> CompleteModule(int moduleId, CancellationToken ct = default)
    {
        await mediator.Send(new CompleteModuleCommand(GetUserId(), moduleId), ct);
        return NoContent();
    }

    [HttpPost("progress/{moduleId:int}/submit-quiz")]
    public async Task<ActionResult<QuizSubmissionResponseModel>> SubmitQuiz(
        int moduleId,
        [FromBody] SubmitQuizRequestModel model,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new SubmitQuizCommand(GetUserId(), moduleId, model.Answers), ct);
        return Ok(result);
    }

    // Admin

    [HttpGet("admin/progress-summary")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<TrainingAdminProgressSummaryResponseModel>>> GetAdminProgressSummary(
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAdminProgressSummaryQuery(), ct);
        return Ok(result);
    }

    [HttpGet("admin/users/{userId:int}/detail")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<UserTrainingDetailResponseModel>> GetUserTrainingDetail(
        int userId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserTrainingDetailQuery(userId), ct);
        return Ok(result);
    }

}
