using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Admin;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Database dump / clean-rebuild import (the in-app face of forge-db's dump/import — same zip
/// layout, so archives are interchangeable with the CLI). Dump is read-only; import truncates and
/// reloads the selected tables, so both sit behind the Admin role and the UI double-confirms.
/// Bootstrap-exempt like the capability/preset admin surface: this is the recovery tool an admin
/// reaches for when an install is in a bad state, so it must never itself be gated off.
/// </summary>
[ApiController]
[Route("api/v1/admin/database")]
[Authorize(Roles = "Admin")]
[CapabilityBootstrap]
public class AdminDatabaseController(IMediator mediator) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DatabaseTransferSummaryModel>> GetSummary()
    {
        var result = await mediator.Send(new GetDatabaseTransferSummaryQuery());
        return Ok(result);
    }

    [HttpGet("dump")]
    public async Task<IActionResult> Dump()
    {
        var result = await mediator.Send(new DumpDatabaseQuery());
        return File(result.Stream, "application/zip", result.FileName);
    }

    // A real database archive runs to hundreds of MB. DisableRequestSizeLimit lifts Kestrel's cap;
    // the multipart reader has its own (128 MB default) that must be lifted separately or the
    // upload dies with "Multipart body length limit exceeded" partway through.
    [HttpPost("import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<ActionResult<DatabaseImportReportModel>> Import(
        IFormFile file,
        [FromForm] string? excludePatterns,
        [FromForm] bool purgeSoftDeleted,
        [FromForm] bool allowFkOrphans)
    {
        await using var stream = file.OpenReadStream();
        var result = await mediator.Send(new ImportDatabaseCommand(
            stream, excludePatterns, purgeSoftDeleted, allowFkOrphans));
        return Ok(result);
    }
}
