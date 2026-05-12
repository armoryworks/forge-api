using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.ComplianceForms;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/identity-documents")]
[Authorize]
[RequiresCapability("CAP-QC-COMPLIANCE-FORMS")]
public class IdentityDocumentsController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<ActionResult<List<IdentityDocumentResponseModel>>> GetMyDocuments(CancellationToken ct)
    {
        var result = await mediator.Send(new GetMyIdentityDocumentsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("me")]
    public async Task<ActionResult<IdentityDocumentResponseModel>> Upload(
        [FromBody] UploadIdentityDocumentRequestModel model,
        [FromQuery] int fileAttachmentId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UploadIdentityDocumentCommand(
            GetUserId(), model.DocumentType, model.ExpiresAt, fileAttachmentId), ct);
        return CreatedAtAction(nameof(GetMyDocuments), result);
    }

    [HttpDelete("me/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteIdentityDocumentCommand(GetUserId(), id), ct);
        return NoContent();
    }

    [HttpGet("admin/users/{userId:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<List<IdentityDocumentResponseModel>>> GetUserDocuments(
        int userId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetUserIdentityDocumentsQuery(userId), ct);
        return Ok(result);
    }

    [HttpPost("admin/{id:int}/verify")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<IActionResult> Verify(int id, CancellationToken ct)
    {
        await mediator.Send(new VerifyIdentityDocumentCommand(id, GetUserId()), ct);
        return NoContent();
    }
}
