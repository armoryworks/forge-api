using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Imports;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// CSV import of historical invoices (file-based import capability, invoicing on). Admin, Manager and
/// OfficeManager import; the Controller imports too when full GL is on. Imported invoices never post to
/// the general ledger. <c>validate</c> is a dry run; <c>commit</c> writes every invoice or none.
/// </summary>
[ApiController]
[Route("api/v1/imports/invoices")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Controller")]
[RequiresCapability("CAP-CROSS-INTEG-FILE")]
[RequiresAnyCapability("CAP-O2C-INVOICE")]
public class InvoiceImportsController(IMediator mediator, ICapabilitySnapshotProvider capabilities) : ControllerBase
{
    private static readonly string[] AlwaysAllowedRoles = ["Admin", "Manager", "OfficeManager"];

    [HttpPost("validate")]
    public async Task<ActionResult<DataImportResultModel>> Validate([FromBody] InvoiceImportRequestModel request, CancellationToken ct)
    {
        if (!CallerMayImport()) return Forbid();
        return Ok(await mediator.Send(new ImportHistoricalInvoicesCommand(request.Rows ?? [], Commit: false), ct));
    }

    [HttpPost("commit")]
    public async Task<ActionResult<DataImportResultModel>> Commit([FromBody] InvoiceImportRequestModel request, CancellationToken ct)
    {
        if (!CallerMayImport()) return Forbid();
        return Ok(await mediator.Send(new ImportHistoricalInvoicesCommand(request.Rows ?? [], Commit: true), ct));
    }

    private bool CallerMayImport() =>
        AlwaysAllowedRoles.Any(User.IsInRole)
        || (User.IsInRole("Controller") && capabilities.IsEnabled("CAP-ACCT-FULLGL"));
}
