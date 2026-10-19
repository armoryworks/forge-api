using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Documents;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/documents/controlled")]
[Authorize]
[RequiresCapability("CAP-CROSS-DOCS")]
public class ControlledDocumentsController(IMediator mediator) : ControllerBase
{
    private const string AuthorRoles = "Admin,Manager,Engineer,ComplianceOfficer";
    private const string ApproverRoles = "Admin,Manager,ComplianceOfficer";

    [HttpGet]
    public async Task<ActionResult<List<ControlledDocumentResponseModel>>> GetDocuments(
        [FromQuery] string? category,
        [FromQuery] ControlledDocumentStatus? status)
    {
        var result = await mediator.Send(new GetControlledDocumentsQuery(category, status));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = AuthorRoles)]
    public async Task<ActionResult<ControlledDocumentResponseModel>> CreateDocument([FromBody] CreateControlledDocumentRequestModel request)
    {
        var result = await mediator.Send(new CreateControlledDocumentCommand(
            request.Title,
            request.Description,
            request.Category,
            request.ReviewIntervalDays));

        return CreatedAtAction(nameof(GetDocuments), new { }, result);
    }

    [HttpPost("{documentId:int}/obsolete")]
    [Authorize(Roles = ApproverRoles)]
    public async Task<IActionResult> ObsoleteDocument(int documentId)
    {
        await mediator.Send(new ObsoleteControlledDocumentCommand(documentId));
        return NoContent();
    }

    [HttpGet("{documentId:int}/revisions")]
    public async Task<ActionResult<List<DocumentRevisionResponseModel>>> GetRevisions(int documentId)
    {
        var result = await mediator.Send(new GetDocumentRevisionsQuery(documentId));
        return Ok(result);
    }

    [HttpPost("{documentId:int}/revisions")]
    [Authorize(Roles = AuthorRoles)]
    public async Task<ActionResult<DocumentRevisionResponseModel>> CreateRevision(
        int documentId, [FromBody] CreateDocumentRevisionRequestModel request)
    {
        var result = await mediator.Send(new CreateDocumentRevisionCommand(
            documentId,
            request.FileAttachmentId,
            request.ChangeDescription));

        return CreatedAtAction(nameof(GetRevisions), new { documentId }, result);
    }

    [HttpPost("{documentId:int}/revisions/{revisionId:int}/submit")]
    [Authorize(Roles = AuthorRoles)]
    public async Task<ActionResult<DocumentRevisionResponseModel>> SubmitRevision(int documentId, int revisionId)
    {
        var result = await mediator.Send(new SubmitDocumentRevisionCommand(documentId, revisionId));
        return Ok(result);
    }

    [HttpPost("{documentId:int}/revisions/{revisionId:int}/approve")]
    [Authorize(Roles = ApproverRoles)]
    public async Task<ActionResult<DocumentRevisionResponseModel>> ApproveRevision(int documentId, int revisionId)
    {
        var result = await mediator.Send(new ApproveDocumentRevisionCommand(documentId, revisionId));
        return Ok(result);
    }

    [HttpPost("{documentId:int}/revisions/{revisionId:int}/reject")]
    [Authorize(Roles = ApproverRoles)]
    public async Task<ActionResult<DocumentRevisionResponseModel>> RejectRevision(int documentId, int revisionId)
    {
        var result = await mediator.Send(new RejectDocumentRevisionCommand(documentId, revisionId));
        return Ok(result);
    }
}
