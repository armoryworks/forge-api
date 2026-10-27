using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Imports;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// CSV job import (file-based import capability, jobs capability on). <c>validate</c> is a dry run that
/// reports every row's errors; <c>commit</c> writes all rows or none.
/// </summary>
[ApiController]
[Route("api/v1/imports/jobs")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-CROSS-INTEG-FILE")]
[RequiresAnyCapability("CAP-MFG-WO-RELEASE")]
public class JobImportsController(IMediator mediator) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<DataImportResultModel>> Validate([FromBody] JobImportRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ImportJobsCommand(request.Rows ?? [], Commit: false), ct));

    [HttpPost("commit")]
    public async Task<ActionResult<DataImportResultModel>> Commit([FromBody] JobImportRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ImportJobsCommand(request.Rows ?? [], Commit: true), ct));
}
