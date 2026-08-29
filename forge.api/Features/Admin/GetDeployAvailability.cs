using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetDeployAvailabilityQuery : IRequest<DeployAvailabilityModel>;

public class GetDeployAvailabilityHandler(IDeployAgentClient agent)
    : IRequestHandler<GetDeployAvailabilityQuery, DeployAvailabilityModel>
{
    public Task<DeployAvailabilityModel> Handle(GetDeployAvailabilityQuery request, CancellationToken ct) =>
        agent.CheckAvailableAsync(ct);
}
