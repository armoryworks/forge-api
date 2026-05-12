using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Leads;

public record GetLeadByIdQuery(int Id) : IRequest<LeadResponseModel?>;

public class GetLeadByIdHandler(ILeadRepository repo) : IRequestHandler<GetLeadByIdQuery, LeadResponseModel?>
{
    public Task<LeadResponseModel?> Handle(GetLeadByIdQuery request, CancellationToken cancellationToken)
        => repo.GetByIdAsync(request.Id, cancellationToken);
}
