using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>Gated Sequence Engine — definitions (versioned templates), instances (runs), and resource clocks.</summary>
[ApiController]
[Route("api/v1/sequences")]
[Authorize]
[RequiresCapability("CAP-CROSS-SEQUENCES")]
public class SequencesController(IMediator mediator) : ControllerBase
{
    // ----- definitions -----

    [HttpGet("definitions")]
    public async Task<IActionResult> GetDefinitions([FromQuery] string? code, [FromQuery] SequenceDefinitionStatus? status, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceDefinitionsQuery(code, status), ct));

    [HttpGet("definitions/{id:int}")]
    public async Task<IActionResult> GetDefinition(int id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceDefinitionQuery(id), ct));

    [HttpPost("definitions")]
    public async Task<IActionResult> CreateDefinition([FromBody] SequenceDefinitionRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateSequenceDefinitionCommand(model), ct);
        return CreatedAtAction(nameof(GetDefinition), new { id = result.Id }, result);
    }

    [HttpPut("definitions/{id:int}")]
    public async Task<IActionResult> UpdateDefinition(int id, [FromBody] SequenceDefinitionRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new UpdateSequenceDefinitionCommand(id, model), ct));

    [HttpPost("definitions/{id:int}/publish")]
    public async Task<IActionResult> PublishDefinition(int id, CancellationToken ct) =>
        Ok(await mediator.Send(new PublishSequenceDefinitionCommand(id, GetUserId()), ct));

    [HttpPost("definitions/{id:int}/new-version")]
    public async Task<IActionResult> NewVersion(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new NewSequenceDefinitionVersionCommand(id), ct);
        return CreatedAtAction(nameof(GetDefinition), new { id = result.Id }, result);
    }

    [HttpDelete("definitions/{id:int}")]
    public async Task<IActionResult> RetireDefinition(int id, CancellationToken ct)
    {
        await mediator.Send(new RetireSequenceDefinitionCommand(id), ct);
        return NoContent();
    }

    // ----- instances -----

    [HttpGet("instances")]
    public async Task<IActionResult> GetInstances([FromQuery] string? subjectEntityType, [FromQuery] int? subjectEntityId,
        [FromQuery] SequenceInstanceStatus? status, [FromQuery] int? definitionId, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceInstancesQuery(subjectEntityType, subjectEntityId, status, definitionId), ct));

    [HttpGet("instances/{id:int}")]
    public async Task<IActionResult> GetInstance(int id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceInstanceQuery(id), ct));

    [HttpGet("instances/{id:int}/events")]
    public async Task<IActionResult> GetEvents(int id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceEventsQuery(id), ct));

    [HttpPost("instances")]
    public async Task<IActionResult> Start([FromBody] StartSequenceRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new StartSequenceInstanceCommand(model, GetUserId()), ct);
        return CreatedAtAction(nameof(GetInstance), new { id = result.Id }, result);
    }

    [HttpPost("instances/{id:int}/reevaluate")]
    public async Task<IActionResult> Reevaluate(int id, CancellationToken ct) =>
        Ok(await mediator.Send(new ReevaluateSequenceCommand(id, GetUserId()), ct));

    [HttpPost("instances/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] SequenceReasonRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new CancelSequenceInstanceCommand(id, model.Reason, GetUserId()), ct));

    [HttpPost("instances/{id:int}/rework")]
    public async Task<IActionResult> Rework(int id, [FromBody] SequenceReworkRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new ReworkSequenceCommand(id, model.TargetStepKey, model.Reason, GetUserId()), ct));

    [HttpPost("instances/{id:int}/steps/{stepKey}/start")]
    public async Task<IActionResult> StartStep(int id, string stepKey, CancellationToken ct) =>
        Ok(await mediator.Send(new StartSequenceStepCommand(id, stepKey, GetUserId()), ct));

    [HttpPost("instances/{id:int}/steps/{stepKey}/complete")]
    public async Task<IActionResult> CompleteStep(int id, string stepKey, CancellationToken ct) =>
        Ok(await mediator.Send(new CompleteSequenceStepCommand(id, stepKey, GetUserId()), ct));

    [HttpPost("instances/{id:int}/steps/{stepKey}/skip")]
    public async Task<IActionResult> SkipStep(int id, string stepKey, [FromBody] SequenceReasonRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new SkipSequenceStepCommand(id, stepKey, model.Reason, GetUserId()), ct));

    [HttpPost("instances/{id:int}/gates/{stepKey}/{gateKey}/clear")]
    public async Task<IActionResult> ClearGate(int id, string stepKey, string gateKey, CancellationToken ct) =>
        Ok(await mediator.Send(new ClearSequenceGateCommand(id, stepKey, gateKey, GetUserId()), ct));

    [HttpPost("instances/{id:int}/gates/{stepKey}/{gateKey}/override")]
    public async Task<IActionResult> OverrideGate(int id, string stepKey, string gateKey, [FromBody] SequenceReasonRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new OverrideSequenceGateCommand(id, stepKey, gateKey, model.Reason, GetUserId()), ct));

    // ----- resource clocks -----

    [HttpGet("resource-clocks")]
    public async Task<IActionResult> GetResourceClocks([FromQuery] string? resourceType, [FromQuery] int? resourceId, [FromQuery] bool includeFired, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSequenceResourceClocksQuery(resourceType, resourceId, includeFired), ct));

    [HttpPost("resource-clocks")]
    public async Task<IActionResult> CreateResourceClock([FromBody] SequenceResourceClockRequestModel model, CancellationToken ct) =>
        Ok(await mediator.Send(new CreateSequenceResourceClockCommand(model), ct));

    [HttpDelete("resource-clocks/{id:int}")]
    public async Task<IActionResult> DeleteResourceClock(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteSequenceResourceClockCommand(id), ct);
        return NoContent();
    }

    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
