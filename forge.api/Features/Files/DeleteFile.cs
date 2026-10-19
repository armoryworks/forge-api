using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Files;

public record DeleteFileCommand(int Id) : IRequest;

public class DeleteFileHandler(
    IFileRepository fileRepo,
    AppDbContext db,
    IEntityAccessResolver access,
    IHttpContextAccessor httpContext,
    IClock clock) : IRequestHandler<DeleteFileCommand>
{
    private static readonly string[] SupervisorRoles = ["Admin", "Manager", "OfficeManager"];

    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var file = await fileRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"File {request.Id} not found.");

        var user = httpContext.HttpContext!.User;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var isUploader = file.UploadedById.ToString() == userId;
        if (!(isUploader || SupervisorRoles.Any(user.IsInRole))
            || !await access.CanAccessFileAsync(file, user, forWrite: true, cancellationToken))
            throw new ForbiddenException("You do not have permission to delete this file.");

        if (await db.Attestations.AnyAsync(a => a.FileAttachmentId == file.Id && a.Status != AcceptanceStatus.Superseded, cancellationToken)
            || await db.IdentityDocuments.AnyAsync(d => d.FileAttachmentId == file.Id, cancellationToken)
            || await db.DocumentRevisions.AnyAsync(r => r.FileAttachmentId == file.Id, cancellationToken))
            throw new InvalidOperationException("This file is the evidence on record for a signed acceptance or a controlled document and cannot be deleted.");

        file.DeletedAt = clock.UtcNow;
        file.DeletedBy = userId;

        await fileRepo.SaveChangesAsync(cancellationToken);
    }
}
