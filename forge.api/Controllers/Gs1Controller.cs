using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Barcodes;

namespace Forge.Api.Controllers;

/// <summary>
/// Optional GS1 identity: configure a licensed company prefix and assign globally-unique GTINs to parts.
/// The whole surface is gated by CAP-MD-GS1 (off by default) — installs that don't pay for GS1 keep the
/// free internal barcode scheme and never see this.
/// </summary>
[ApiController]
[Route("api/v1/gs1")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-GS1")]
public class Gs1Controller(IMediator mediator) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<Gs1SettingsResponseModel>> GetSettings()
        => Ok(await mediator.Send(new GetGs1SettingsQuery()));

    [HttpPut("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateGs1SettingsCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    /// <summary>Assign a GTIN to a part — pass a purchased GTIN in the body, or omit it to auto-allocate.</summary>
    [HttpPost("parts/{partId:int}/gtin")]
    public async Task<ActionResult<AssignPartGtinResponseModel>> AssignGtin(int partId, [FromBody] AssignGtinRequest? request)
        => Ok(await mediator.Send(new AssignPartGtinCommand(partId, request?.ManualGtin)));

    [HttpDelete("parts/{partId:int}/gtin")]
    public async Task<IActionResult> RemoveGtin(int partId)
    {
        await mediator.Send(new RemovePartGtinCommand(partId));
        return NoContent();
    }
}

/// <summary>Body for assigning a GTIN. Omit ManualGtin to auto-allocate from the company prefix.</summary>
public record AssignGtinRequest(string? ManualGtin);
