using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Leads;

// ExternalId is an exact-match filter on the intake idempotency key — relay
// clients (e.g. Tuyere) use it to ask "did I already create this lead?" before
// retrying a POST. Null = no filter.
public record GetLeadsQuery(LeadStatus? Status, string? Search, string? ExternalId = null) : IRequest<List<LeadResponseModel>>;

public class GetLeadsHandler(ILeadRepository repo) : IRequestHandler<GetLeadsQuery, List<LeadResponseModel>>
{
    public Task<List<LeadResponseModel>> Handle(GetLeadsQuery request, CancellationToken cancellationToken)
        => repo.GetLeadsAsync(request.Status, request.Search, request.ExternalId, cancellationToken);
}
