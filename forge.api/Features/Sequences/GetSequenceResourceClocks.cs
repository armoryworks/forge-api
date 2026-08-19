using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceResourceClocksQuery(string? ResourceType, int? ResourceId, bool IncludeFired = false) : IRequest<IReadOnlyList<SequenceResourceClockResponseModel>>;

public class GetSequenceResourceClocksHandler(AppDbContext db, IClock clock) : IRequestHandler<GetSequenceResourceClocksQuery, IReadOnlyList<SequenceResourceClockResponseModel>>
{
    public async Task<IReadOnlyList<SequenceResourceClockResponseModel>> Handle(GetSequenceResourceClocksQuery request, CancellationToken cancellationToken)
    {
        var q = db.SequenceResourceClocks.Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(request.ResourceType)) q = q.Where(c => c.ResourceType == request.ResourceType);
        if (request.ResourceId.HasValue) q = q.Where(c => c.ResourceId == request.ResourceId);
        if (!request.IncludeFired) q = q.Where(c => c.FiredAt == null);
        var now = clock.UtcNow;
        var list = await q.OrderBy(c => c.ExpiresAt).Take(500).ToListAsync(cancellationToken);
        return list.Select(c => SequenceMapping.ToModel(c, now)).ToList();
    }
}
