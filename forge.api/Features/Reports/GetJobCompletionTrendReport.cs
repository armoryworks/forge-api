using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetJobCompletionTrendQuery(int Months = 6) : IRequest<List<JobCompletionTrendItem>>;

public class GetJobCompletionTrendHandler(IReportRepository repo) : IRequestHandler<GetJobCompletionTrendQuery, List<JobCompletionTrendItem>>
{
    public Task<List<JobCompletionTrendItem>> Handle(GetJobCompletionTrendQuery request, CancellationToken cancellationToken)
    {
        return repo.GetJobCompletionTrendAsync(request.Months, cancellationToken);
    }
}
