using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetLeadPipelineReportQuery : IRequest<List<LeadPipelineReportItem>>;

public class GetLeadPipelineReportHandler(IReportRepository repo) : IRequestHandler<GetLeadPipelineReportQuery, List<LeadPipelineReportItem>>
{
    public Task<List<LeadPipelineReportItem>> Handle(GetLeadPipelineReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetLeadPipelineAsync(cancellationToken);
    }
}
