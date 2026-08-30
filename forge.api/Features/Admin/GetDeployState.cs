using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetDeployStateQuery : IRequest<DeployStateModel>;

public class GetDeployStateHandler(IDeployAgentClient agent) : IRequestHandler<GetDeployStateQuery, DeployStateModel>
{
    public Task<DeployStateModel> Handle(GetDeployStateQuery request, CancellationToken ct) =>
        agent.GetStateAsync(ct);
}
