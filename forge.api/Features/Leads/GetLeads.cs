using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Leads;

public record GetLeadsQuery(LeadStatus? Status, string? Search) : IRequest<List<LeadResponseModel>>;

public class GetLeadsHandler(ILeadRepository repo) : IRequestHandler<GetLeadsQuery, List<LeadResponseModel>>
{
    public Task<List<LeadResponseModel>> Handle(GetLeadsQuery request, CancellationToken cancellationToken)
        => repo.GetLeadsAsync(request.Status, request.Search, cancellationToken);
}
