using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Watchtower;

/// <summary>regulatory-watchtower: list the monitored regulatory sources.</summary>
public record GetRegulatorySourcesQuery : IRequest<List<RegulatorySourceResponseModel>>;

public class GetRegulatorySourcesHandler(AppDbContext db)
    : IRequestHandler<GetRegulatorySourcesQuery, List<RegulatorySourceResponseModel>>
{
    public async Task<List<RegulatorySourceResponseModel>> Handle(
        GetRegulatorySourcesQuery request, CancellationToken cancellationToken)
        => await db.RegulatorySources.AsNoTracking()
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Domain).ThenBy(s => s.Name)
            .Select(s => new RegulatorySourceResponseModel(
                s.Id, s.Name, s.IssuingBody, s.Domain, s.Url, s.FeedType.ToString(),
                s.IndustryGate, s.IsActive, s.LastPolledAt))
            .ToListAsync(cancellationToken);
}
