using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Payroll;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/payroll")]
[Authorize]
[RequiresCapability("CAP-HR-PAYROLL")]
public class PayrollController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdminRole() => User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("OfficeManager");

    // ─── Employee Self-Service ───

    [HttpGet("pay-stubs/me")]
    public async Task<ActionResult<List<PayStubResponseModel>>> GetMyPayStubs(CancellationToken ct)
    {
        var result = await mediator.Send(new GetMyPayStubsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpGet("pay-stubs/{id:int}/pdf")]
    public async Task<IActionResult> GetPayStubPdf(int id, CancellationToken ct)
    {
        var fileAttachmentId = await mediator.Send(
            new GetPayStubPdfQuery(id, GetUserId(), IsAdminRole()), ct);

        if (fileAttachmentId is null)
            return NotFound("No PDF attached to this pay stub.");

        return Redirect($"/api/v1/files/{fileAttachmentId}");
    }

    [HttpGet("tax-documents/me")]
    public async Task<ActionResult<List<TaxDocumentResponseModel>>> GetMyTaxDocuments(CancellationToken ct)
    {
        var result = await mediator.Send(new GetMyTaxDocumentsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpGet("tax-documents/{id:int}/pdf")]
    public async Task<IActionResult> GetTaxDocumentPdf(int id, CancellationToken ct)
    {
        var fileAttachmentId = await mediator.Send(
            new GetTaxDocumentPdfQuery(id, GetUserId(), IsAdminRole()), ct);

        if (fileAttachmentId is null)
            return NotFound("No PDF attached to this tax document.");

        return Redirect($"/api/v1/files/{fileAttachmentId}");
    }

    // ─── Admin / Manager / Office Manager ───

    [HttpGet("pay-stubs/users/{userId:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<List<PayStubResponseModel>>> GetUserPayStubs(int userId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetUserPayStubsQuery(userId), ct);
        return Ok(result);
    }

    [HttpPost("pay-stubs/users/{userId:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PayStubResponseModel>> UploadPayStub(
        int userId, [FromBody] UploadPayStubRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UploadPayStubCommand(userId, request), ct);
        return Created($"/api/v1/payroll/pay-stubs/{result.Id}", result);
    }

    [HttpDelete("pay-stubs/{id:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<IActionResult> DeletePayStub(int id, CancellationToken ct)
    {
        await mediator.Send(new DeletePayStubCommand(id), ct);
        return NoContent();
    }

    [HttpGet("tax-documents/users/{userId:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<List<TaxDocumentResponseModel>>> GetUserTaxDocuments(int userId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetUserTaxDocumentsQuery(userId), ct);
        return Ok(result);
    }

    [HttpPost("tax-documents/users/{userId:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<TaxDocumentResponseModel>> UploadTaxDocument(
        int userId, [FromBody] UploadTaxDocumentRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UploadTaxDocumentCommand(userId, request), ct);
        return Created($"/api/v1/payroll/tax-documents/{result.Id}", result);
    }

    [HttpDelete("tax-documents/{id:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<IActionResult> DeleteTaxDocument(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteTaxDocumentCommand(id), ct);
        return NoContent();
    }

    [HttpPost("sync")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<int>> SyncPayrollData(CancellationToken ct)
    {
        var count = await mediator.Send(new SyncPayrollDataCommand(), ct);
        return Ok(count);
    }
}
