using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Terms;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// S3 — CRUD over terms-and-conditions documents. Reuses the quotes
/// capability (terms exist to ride on quotes). Role split: customer/part
/// scope is open to the class-level roles; company-scope mutations are
/// Admin-only — enforced in the handlers via <c>CallerIsAdmin</c> because
/// the scope of an existing row isn't known at the attribute edge.
/// </summary>
[ApiController]
[Route("api/v1/terms")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-QUOTE")]
public class TermsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TermsDocumentResponseModel>>> GetTermsDocuments(
        [FromQuery] TermsScope? scope,
        [FromQuery] int? customerId,
        [FromQuery] int? partId,
        [FromQuery] bool? isActive)
    {
        var result = await mediator.Send(new GetTermsDocumentsQuery(scope, customerId, partId, isActive));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TermsDocumentResponseModel>> CreateTermsDocument(
        CreateTermsDocumentRequestModel request)
    {
        var result = await mediator.Send(new CreateTermsDocumentCommand(request, CallerIsAdmin()));
        return Created($"/api/v1/terms/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TermsDocumentResponseModel>> UpdateTermsDocument(
        int id, UpdateTermsDocumentRequestModel request)
    {
        var result = await mediator.Send(new UpdateTermsDocumentCommand(id, request, CallerIsAdmin()));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTermsDocument(int id)
    {
        await mediator.Send(new DeleteTermsDocumentCommand(id, CallerIsAdmin()));
        return NoContent();
    }

    private bool CallerIsAdmin() => User.IsInRole("Admin");
}
