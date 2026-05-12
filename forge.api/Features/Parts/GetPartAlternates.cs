using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetPartAlternatesQuery(int PartId) : IRequest<List<PartAlternateResponseModel>>;

public class GetPartAlternatesHandler(AppDbContext db) : IRequestHandler<GetPartAlternatesQuery, List<PartAlternateResponseModel>>
{
    public async Task<List<PartAlternateResponseModel>> Handle(GetPartAlternatesQuery request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        // Get alternates where this part is the primary
        var directAlternates = await db.PartAlternates
            .AsNoTracking()
            .Include(a => a.AlternatePart)
            .Where(a => a.PartId == request.PartId)
            .OrderBy(a => a.Priority)
            .ToListAsync(cancellationToken);

        // Get bidirectional alternates where this part is the alternate
        var reverseAlternates = await db.PartAlternates
            .AsNoTracking()
            .Include(a => a.Part)
            .Where(a => a.AlternatePartId == request.PartId && a.IsBidirectional)
            .OrderBy(a => a.Priority)
            .ToListAsync(cancellationToken);

        // Look up approver names
        var approverIds = directAlternates
            .Where(a => a.ApprovedById.HasValue)
            .Select(a => a.ApprovedById!.Value)
            .Concat(reverseAlternates.Where(a => a.ApprovedById.HasValue).Select(a => a.ApprovedById!.Value))
            .Distinct()
            .ToList();

        var approverNames = approverIds.Count > 0
            ? await db.Users
                .AsNoTracking()
                .Where(u => approverIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken)
            : new Dictionary<int, string>();

        var results = directAlternates.Select(a => new PartAlternateResponseModel
        {
            Id = a.Id,
            PartId = a.PartId,
            AlternatePartId = a.AlternatePartId,
            AlternatePartNumber = a.AlternatePart.PartNumber,
            AlternatePartName = a.AlternatePart.Name,
            Priority = a.Priority,
            Type = a.Type,
            ConversionFactor = a.ConversionFactor,
            IsApproved = a.IsApproved,
            ApprovedByName = a.ApprovedById.HasValue && approverNames.TryGetValue(a.ApprovedById.Value, out var name) ? name : null,
            ApprovedAt = a.ApprovedAt,
            Notes = a.Notes,
            IsBidirectional = a.IsBidirectional,
            CreatedAt = a.CreatedAt,
        }).ToList();

        // Add reverse alternates (swapping part info)
        results.AddRange(reverseAlternates.Select(a => new PartAlternateResponseModel
        {
            Id = a.Id,
            PartId = a.AlternatePartId,
            AlternatePartId = a.PartId,
            AlternatePartNumber = a.Part.PartNumber,
            AlternatePartName = a.Part.Name,
            Priority = a.Priority,
            Type = a.Type,
            ConversionFactor = a.ConversionFactor.HasValue ? 1m / a.ConversionFactor.Value : null,
            IsApproved = a.IsApproved,
            ApprovedByName = a.ApprovedById.HasValue && approverNames.TryGetValue(a.ApprovedById.Value, out var name) ? name : null,
            ApprovedAt = a.ApprovedAt,
            Notes = a.Notes,
            IsBidirectional = a.IsBidirectional,
            CreatedAt = a.CreatedAt,
        }));

        return results.OrderBy(r => r.Priority).ToList();
    }
}
