using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Operations;

public record GetJobOperationsQuery(int JobId) : IRequest<JobOperationsResponseModel>;

public class GetJobOperationsHandler(IJobOperationService operations)
    : IRequestHandler<GetJobOperationsQuery, JobOperationsResponseModel>
{
    public Task<JobOperationsResponseModel> Handle(GetJobOperationsQuery request, CancellationToken ct)
        => operations.BuildAsync(request.JobId, ct);
}
