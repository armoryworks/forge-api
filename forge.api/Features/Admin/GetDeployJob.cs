using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetDeployJobQuery(string JobId) : IRequest<DeployJobModel?>;

public class GetDeployJobHandler(IDeployAgentClient agent) : IRequestHandler<GetDeployJobQuery, DeployJobModel?>
{
    public Task<DeployJobModel?> Handle(GetDeployJobQuery request, CancellationToken ct) =>
        agent.GetJobAsync(request.JobId, ct);
}

public record GetDeployJobLogQuery(string JobId, long Offset) : IRequest<string>;

public class GetDeployJobLogHandler(IDeployAgentClient agent) : IRequestHandler<GetDeployJobLogQuery, string>
{
    public Task<string> Handle(GetDeployJobLogQuery request, CancellationToken ct) =>
        agent.GetJobLogAsync(request.JobId, request.Offset, ct);
}
