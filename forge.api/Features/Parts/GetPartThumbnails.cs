using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetPartThumbnailsQuery(List<int> PartIds) : IRequest<List<PartThumbnailResponseModel>>;

public record PartThumbnailResponseModel(int PartId, string? ThumbnailUrl);

public class GetPartThumbnailsHandler(AppDbContext db) : IRequestHandler<GetPartThumbnailsQuery, List<PartThumbnailResponseModel>>
{
    public async Task<List<PartThumbnailResponseModel>> Handle(GetPartThumbnailsQuery request, CancellationToken cancellationToken)
    {
        if (request.PartIds.Count == 0)
            return [];

        var attachments = await db.FileAttachments
            .Where(f => f.EntityType == "Part"
                && request.PartIds.Contains(f.EntityId)
                && f.DeletedAt == null)
            .Where(f => f.ContentType.StartsWith("image/"))
            .GroupBy(f => f.EntityId)
            .Select(g => new { PartId = g.Key, FileId = g.OrderBy(f => f.CreatedAt).First().Id })
            .ToListAsync(cancellationToken);

        var urlMap = attachments.ToDictionary(
            a => a.PartId,
            a => $"/api/v1/files/{a.FileId}/download");

        return request.PartIds
            .Select(id => new PartThumbnailResponseModel(id, urlMap.GetValueOrDefault(id)))
            .ToList();
    }
}
