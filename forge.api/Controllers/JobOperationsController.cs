using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
[RequiresCapability("CAP-MFG-MULTIOP")]
public class JobOperationsController(IMediator mediator) : ControllerBase
{
    [HttpGet("job-operations/config")]
    public async Task<ActionResult<JobOperationsConfigResponseModel>> GetConfig(CancellationToken ct)
        => Ok(await mediator.Send(new GetJobOperationsConfigQuery(), ct));

    [HttpGet("jobs/{jobId:int}/operations")]
    public async Task<ActionResult<JobOperationsResponseModel>> GetOperations(int jobId, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobOperationsQuery(jobId), ct));

    [HttpGet("jobs/{jobId:int}/operations/{operationId:int}/events")]
    public async Task<ActionResult<IReadOnlyList<JobOperationEventResponseModel>>> GetEvents(
        int jobId, int operationId, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobOperationEventsQuery(jobId, operationId), ct));

    [HttpPost("jobs/{jobId:int}/operations/{operationId:int}/timer/start")]
    public async Task<ActionResult<JobOperationTimerResponseModel>> StartTimer(
        int jobId, int operationId, [FromBody] StartJobOperationTimerRequestModel? request, CancellationToken ct)
        => Ok(await mediator.Send(
            new StartJobOperationTimerCommand(jobId, operationId, request ?? new StartJobOperationTimerRequestModel()), ct));

    [HttpPost("jobs/{jobId:int}/operations/{operationId:int}/timer/stop")]
    public async Task<ActionResult<JobOperationTimerStopResponseModel>> StopTimer(
        int jobId, int operationId, [FromBody] StopJobOperationTimerRequestModel? request, CancellationToken ct)
        => Ok(await mediator.Send(
            new StopJobOperationTimerCommand(jobId, operationId, request ?? new StopJobOperationTimerRequestModel()), ct));

    [HttpPatch("jobs/{jobId:int}/operations/{operationId:int}")]
    public async Task<ActionResult<JobOperationProgressResponseModel>> UpdateProgress(
        int jobId, int operationId, [FromBody] UpdateJobOperationProgressRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateJobOperationProgressCommand(jobId, operationId, request), ct));
}
