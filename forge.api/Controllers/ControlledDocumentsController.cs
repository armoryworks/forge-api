using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Documents;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/documents/controlled")]
[Authorize]
[RequiresCapability("CAP-CROSS-DOCS")]
public class ControlledDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ControlledDocumentResponseModel>>> GetDocuments(
        [FromQuery] string? category,
        [FromQuery] ControlledDocumentStatus? status)
    {
        var result = await mediator.Send(new GetControlledDocumentsQuery(category, status));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ControlledDocumentResponseModel>> CreateDocument([FromBody] CreateControlledDocumentRequestModel request)
    {
        var result = await mediator.Send(new CreateControlledDocumentCommand(
            request.Title,
            request.Description,
            request.Category,
            request.ReviewIntervalDays));

        return CreatedAtAction(nameof(GetDocuments), new { }, result);
    }

    [HttpGet("{documentId:int}/revisions")]
    public async Task<ActionResult<List<DocumentRevisionResponseModel>>> GetRevisions(int documentId)
    {
        var result = await mediator.Send(new GetDocumentRevisionsQuery(documentId));
        return Ok(result);
    }
}
