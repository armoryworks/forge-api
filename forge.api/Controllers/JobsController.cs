using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Api.Features.Jobs.Bulk;
using Forge.Api.Features.Jobs.Links;
using Forge.Api.Features.Jobs.Parts;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Api.Features.Jobs.Subtasks;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
[Authorize(Roles = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager")]
[RequiresCapability("CAP-MFG-WO-RELEASE")]
public class JobsController(IMediator mediator) : ControllerBase
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

    [HttpGet("calendar.ics")]
    public async Task<IActionResult> ExportCalendar(
        [FromQuery] int? assigneeId,
        [FromQuery] int? trackTypeId)
    {
        var ics = await mediator.Send(new ExportJobsCalendarQuery(assigneeId, trackTypeId));
        return File(ics, "text/calendar", "jobs.ics");
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobDetailResponseModel>> GetJob(int id)
    {
        var result = await mediator.Send(new GetJobByIdQuery(id));
        return Ok(result);
    }

    /// <summary>
    /// Phase 3 H4 / WU-20 — return the BOM revision the job was released
    /// against, with a flag indicating whether the part's BOM has been
    /// updated since.
    /// </summary>
    [HttpGet("{id:int}/bom-at-release")]
    public async Task<ActionResult<JobBomAtReleaseResponseModel>> GetJobBomAtRelease(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetJobBomAtReleaseQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<JobDetailResponseModel>> CreateJob(CreateJobCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetJob), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(Job))]
    public async Task<ActionResult<JobDetailResponseModel>> UpdateJob(int id, UpdateJobCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpPatch("{id:int}/stage")]
    [IfMatch(typeof(Job))]
    public async Task<ActionResult<JobDetailResponseModel>> MoveJobStage(int id, MoveJobStageCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpPatch("{id:int}/position")]
    [IfMatch(typeof(Job))]
    public async Task<ActionResult> UpdateJobPosition(int id, UpdateJobPositionCommand command)
    {
        var cmd = command with { JobId = id };
        await mediator.Send(cmd);
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetJobActivity(int id)
    {
        var result = await mediator.Send(new GetJobActivityQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/comments")]
    public async Task<ActionResult<ActivityResponseModel>> CreateJobComment(int id, CreateJobCommentCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return Created($"/api/v1/jobs/{id}/activity", result);
    }

    [HttpGet("{id:int}/subtasks")]
    public async Task<ActionResult<List<SubtaskResponseModel>>> GetSubtasks(int id)
    {
        var result = await mediator.Send(new GetSubtasksQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/subtasks")]
    public async Task<ActionResult<SubtaskResponseModel>> CreateSubtask(int id, CreateSubtaskCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return CreatedAtAction(nameof(GetSubtasks), new { id }, result);
    }

    [HttpPatch("{id:int}/subtasks/{subtaskId:int}")]
    public async Task<ActionResult<SubtaskResponseModel>> UpdateSubtask(int id, int subtaskId, UpdateSubtaskCommand command)
    {
        var cmd = command with { JobId = id, SubtaskId = subtaskId };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpGet("{id:int}/links")]
    public async Task<ActionResult<List<JobLinkResponseModel>>> GetJobLinks(int id)
    {
        var result = await mediator.Send(new GetJobLinksQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/links")]
    public async Task<ActionResult<JobLinkResponseModel>> CreateJobLink(int id, CreateJobLinkCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return CreatedAtAction(nameof(GetJobLinks), new { id }, result);
    }

    [HttpDelete("{id:int}/links/{linkId:int}")]
    public async Task<ActionResult> DeleteJobLink(int id, int linkId)
    {
        await mediator.Send(new DeleteJobLinkCommand(id, linkId));
        return NoContent();
    }

    // Parts
    [HttpGet("{id:int}/parts")]
    public async Task<ActionResult<List<JobPartResponseModel>>> GetJobParts(int id)
    {
        var result = await mediator.Send(new GetJobPartsQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/parts")]
    public async Task<ActionResult<JobPartResponseModel>> AddJobPart(int id, AddJobPartCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return CreatedAtAction(nameof(GetJobParts), new { id }, result);
    }

    [HttpPatch("{id:int}/parts/{jobPartId:int}")]
    public async Task<ActionResult<JobPartResponseModel>> UpdateJobPart(int id, int jobPartId, UpdateJobPartCommand command)
    {
        var cmd = command with { JobId = id, JobPartId = jobPartId };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("{id:int}/parts/{jobPartId:int}")]
    public async Task<ActionResult> RemoveJobPart(int id, int jobPartId)
    {
        await mediator.Send(new RemoveJobPartCommand(id, jobPartId));
        return NoContent();
    }

    // Custom fields
    [HttpGet("{id:int}/custom-fields")]
    public async Task<ActionResult<Dictionary<string, object?>>> GetCustomFieldValues(int id)
    {
        var result = await mediator.Send(new GetCustomFieldValuesQuery(id));
        return Ok(result);
    }

    [HttpPut("{id:int}/custom-fields")]
    public async Task<ActionResult<Dictionary<string, object?>>> UpdateCustomFieldValues(
        int id, UpdateCustomFieldValuesCommand command)
    {
        var cmd = command with { JobId = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    // Production Runs
    [HttpGet("{id:int}/production-runs")]
    [RequiresCapability("CAP-MFG-COMPLETE")]
    public async Task<ActionResult<List<ProductionRunResponseModel>>> GetProductionRuns(int id)
    {
        var result = await mediator.Send(new GetProductionRunsQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/production-runs")]
    [RequiresCapability("CAP-MFG-COMPLETE")]
    public async Task<ActionResult<ProductionRunResponseModel>> CreateProductionRun(
        int id, CreateProductionRunRequestModel request)
    {
        var command = new CreateProductionRunCommand(id, request.PartId, request.TargetQuantity, request.OperatorId, request.Notes);
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetProductionRuns), new { id }, result);
    }

    [HttpPut("{id:int}/production-runs/{runId:int}")]
    [RequiresCapability("CAP-MFG-COMPLETE")]
    public async Task<ActionResult<ProductionRunResponseModel>> UpdateProductionRun(
        int id, int runId, UpdateProductionRunRequestModel request)
    {
        var command = new UpdateProductionRunCommand(
            id, runId, request.CompletedQuantity, request.ScrapQuantity,
            request.Status, request.Notes, request.SetupTimeMinutes, request.RunTimeMinutes);
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id:int}/production-runs/{runId:int}")]
    [RequiresCapability("CAP-MFG-COMPLETE")]
    public async Task<ActionResult> DeleteProductionRun(int id, int runId)
    {
        await mediator.Send(new DeleteProductionRunCommand(id, runId));
        return NoContent();
    }

    // Bulk operations
    [HttpPatch("bulk/stage")]
    public async Task<ActionResult<BulkOperationResponseModel>> BulkMoveStage(BulkMoveJobStageCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPatch("bulk/assign")]
    public async Task<ActionResult<BulkOperationResponseModel>> BulkAssign(BulkAssignJobCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPatch("bulk/priority")]
    public async Task<ActionResult<BulkOperationResponseModel>> BulkSetPriority(BulkSetPriorityCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPatch("bulk/archive")]
    public async Task<ActionResult<BulkOperationResponseModel>> BulkArchive(BulkArchiveJobsCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    // Unarchive — inverse of bulk/archive. Admin-only because restoring an
    // archived job has audit/visibility implications. Phase 3 / WU-07 / F2.
    [HttpPatch("bulk/unarchive")]
    [HttpPost("bulk/unarchive")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BulkOperationResponseModel>> BulkUnarchive(BulkUnarchiveJobsCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("{id:int}/unarchive")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BulkOperationResponseModel>> Unarchive(int id)
    {
        var result = await mediator.Send(new BulkUnarchiveJobsCommand(new List<int> { id }));
        return Ok(result);
    }

    // Cover photo
    [HttpPatch("{id:int}/cover-photo")]
    public async Task<IActionResult> SetCoverPhoto(int id, [FromBody] SetCoverPhotoRequest request)
    {
        await mediator.Send(new SetJobCoverPhotoCommand(id, request.FileAttachmentId));
        return NoContent();
    }

    // Disposition
    [HttpPost("{id:int}/dispose")]
    public async Task<ActionResult<JobDetailResponseModel>> DisposeJob(int id, DisposeJobRequestModel request)
        => Ok(await mediator.Send(new DisposeJobCommand(id, request)));

    // R&D Handoff
    [HttpPost("{id:int}/handoff-to-production")]
    public async Task<ActionResult<object>> HandoffToProduction(int id)
    {
        var prodJobId = await mediator.Send(new HandoffToProductionCommand(id));
        return Created($"/api/v1/jobs/{prodJobId}", new { jobId = prodJobId });
    }

    // BOM Explosion
    [HttpPost("{id:int}/explode-bom")]
    public async Task<ActionResult<BomExplosionResponseModel>> ExplodeJobBom(int id)
        => Ok(await mediator.Send(new ExplodeJobBomCommand(id)));

    // Child Jobs
    [HttpGet("{id:int}/child-jobs")]
    public async Task<ActionResult<List<ChildJobResponseModel>>> GetChildJobs(int id)
        => Ok(await mediator.Send(new GetChildJobsQuery(id)));

    // Internal Project Types
    [HttpGet("internal-project-types")]
    public async Task<ActionResult<List<ReferenceDataResponseModel>>> GetInternalProjectTypes()
    {
        var result = await mediator.Send(new GetInternalProjectTypesQuery());
        return Ok(result);
    }

    // Notes
    [HttpGet("{id:int}/notes")]
    public async Task<ActionResult<List<JobNoteResponseModel>>> GetNotes(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobNotesQuery(id), ct));

    [HttpPost("{id:int}/notes")]
    public async Task<ActionResult<JobNoteResponseModel>> CreateNote(int id, [FromBody] CreateNoteRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var note = await mediator.Send(new CreateJobNoteCommand(id, req.Text, userId, req.MentionedUserIds ?? []), ct);
        return Created($"/api/v1/jobs/{id}/notes/{note.Id}", note);
    }

    [HttpDelete("{id:int}/notes/{noteId:int}")]
    public async Task<ActionResult> DeleteNote(int id, int noteId, CancellationToken ct)
    {
        await mediator.Send(new DeleteJobNoteCommand(noteId), ct);
        return NoContent();
    }

    // History
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetHistory(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobHistoryQuery(id), ct));

    // Job Costing
    [HttpGet("{id:int}/cost-summary")]
    public async Task<ActionResult<JobCostSummaryModel>> GetCostSummary(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobCostSummaryQuery(id), ct));

    [HttpGet("{id:int}/material-issues")]
    [RequiresCapability("CAP-MFG-MATL-ISSUE")]
    public async Task<ActionResult<List<MaterialIssueResponseModel>>> GetMaterialIssues(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetJobMaterialIssuesQuery(id, page, pageSize), ct));

    [HttpPost("{id:int}/material-issues")]
    [RequiresCapability("CAP-MFG-MATL-ISSUE")]
    public async Task<ActionResult<MaterialIssueResponseModel>> CreateMaterialIssue(
        int id, [FromBody] MaterialIssueRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new CreateMaterialIssueCommand(
            id, req.PartId, req.OperationId, req.Quantity,
            req.BinContentId, req.StorageLocationId, req.LotNumber,
            req.IssueType, req.Notes, userId), ct);
        return Created($"/api/v1/jobs/{id}/material-issues/{result.Id}", result);
    }

    [HttpPost("{id:int}/material-issues/{issueId:int}/return")]
    [RequiresCapability("CAP-MFG-MATL-ISSUE")]
    public async Task<ActionResult<MaterialIssueResponseModel>> ReturnMaterialIssue(
        int id, int issueId, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new ReturnMaterialIssueCommand(id, issueId, userId), ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/recalculate-costs")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult> RecalculateCosts(int id, CancellationToken ct)
    {
        await mediator.Send(new RecalculateJobCostsCommand(id), ct);
        return NoContent();
    }

    // ─── Operation Time Tracking ───

    [HttpGet("{id:int}/operation-time-summary")]
    public async Task<ActionResult<List<OperationTimeAnalysisModel>>> GetOperationTimeSummary(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetJobOperationTimeSummaryQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/operations/{operationId:int}/time-entries")]
    public async Task<ActionResult<List<TimeEntryResponseModel>>> GetOperationTimeEntries(
        int id, int operationId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetOperationTimeEntriesQuery(id, operationId), ct);
        return Ok(result);
    }
}

public record CreateNoteRequest(string Text, int[]? MentionedUserIds = null);

public record MaterialIssueRequest(
    int PartId,
    int? OperationId,
    decimal Quantity,
    int? BinContentId,
    int? StorageLocationId,
    string? LotNumber,
    MaterialIssueType IssueType = MaterialIssueType.Issue,
    string? Notes = null);
