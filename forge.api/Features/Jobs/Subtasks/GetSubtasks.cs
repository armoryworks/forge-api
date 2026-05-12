using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Subtasks;

public record GetSubtasksQuery(int JobId) : IRequest<List<SubtaskResponseModel>>;

public class GetSubtasksHandler(ISubtaskRepository repo) : IRequestHandler<GetSubtasksQuery, List<SubtaskResponseModel>>
{
    public Task<List<SubtaskResponseModel>> Handle(GetSubtasksQuery request, CancellationToken cancellationToken)
        => repo.GetByJobIdAsync(request.JobId, cancellationToken);
}
