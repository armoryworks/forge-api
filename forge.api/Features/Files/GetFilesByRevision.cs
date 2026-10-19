using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Files;

public record GetFilesByRevisionQuery(int PartId, int RevisionId) : IRequest<List<FileAttachmentResponseModel>>;

public class GetFilesByRevisionHandler(
    AppDbContext db,
    IEntityAccessResolver access,
    IHttpContextAccessor httpContext) : IRequestHandler<GetFilesByRevisionQuery, List<FileAttachmentResponseModel>>
{
    public async Task<List<FileAttachmentResponseModel>> Handle(GetFilesByRevisionQuery request, CancellationToken ct)
    {
        if (!await access.CanAccessAsync("parts", request.PartId, httpContext.HttpContext?.User, forWrite: false, ct))
            throw new ForbiddenException("You do not have access to this part's files.");

        var revision = await db.PartRevisions
            .FirstOrDefaultAsync(r => r.Id == request.RevisionId && r.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Revision {request.RevisionId} not found for part {request.PartId}.");

        var files = await db.FileAttachments
            .Where(f => f.PartRevisionId == request.RevisionId && f.DeletedAt == null)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FileAttachmentResponseModel(
                f.Id,
                f.FileName,
                f.ContentType,
                f.Size,
                $"/api/v1/files/{f.Id}/download",
                f.EntityType,
                f.EntityId,
                f.UploadedById,
                "",
                f.CreatedAt,
                f.PartRevisionId,
                f.RequiredRole))
            .ToListAsync(ct);

        return files;
    }
}
