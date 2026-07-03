using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Watchtower;

/// <summary>regulatory-watchtower: proposals filtered by status (default Pending).</summary>
public record GetRegulatoryProposalsQuery(string? Status) : IRequest<List<RegulatoryProposalResponseModel>>;

public class GetRegulatoryProposalsHandler(AppDbContext db)
    : IRequestHandler<GetRegulatoryProposalsQuery, List<RegulatoryProposalResponseModel>>
{
    public async Task<List<RegulatoryProposalResponseModel>> Handle(
        GetRegulatoryProposalsQuery request, CancellationToken cancellationToken)
    {
        var status = Enum.TryParse<RegulatoryProposalStatus>(request.Status, true, out var s)
            ? s
            : RegulatoryProposalStatus.Pending;

        return await db.RegulatoryChangeProposals.AsNoTracking()
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new RegulatoryProposalResponseModel(
                p.Id, p.RegulatorySourceId, p.RegulatorySource.Name, p.Title, p.SummaryUrl,
                p.Details, p.Status.ToString(), p.TargetEventTypeId, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
