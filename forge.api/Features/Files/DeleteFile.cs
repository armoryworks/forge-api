using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Files;

public record DeleteFileCommand(int Id) : IRequest;

public class DeleteFileHandler(
    IFileRepository fileRepo,
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

        file.DeletedAt = clock.UtcNow;
        file.DeletedBy = userId;

        await fileRepo.SaveChangesAsync(cancellationToken);
    }
}
