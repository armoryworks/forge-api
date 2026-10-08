using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Operations;

public record GetJobOperationsConfigQuery : IRequest<JobOperationsConfigResponseModel>;

public class GetJobOperationsConfigHandler(IJobOperationService operations)
    : IRequestHandler<GetJobOperationsConfigQuery, JobOperationsConfigResponseModel>
{
    public async Task<JobOperationsConfigResponseModel> Handle(GetJobOperationsConfigQuery request, CancellationToken ct)
        => new(await operations.IsTrackingEnabledAsync(ct));
}
