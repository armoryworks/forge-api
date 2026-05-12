using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Files;

public record DeleteFileCommand(int Id) : IRequest;

public class DeleteFileHandler(IFileRepository fileRepo, IHttpContextAccessor httpContext) : IRequestHandler<DeleteFileCommand>
{
    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var file = await fileRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"File {request.Id} not found.");

        var userId = httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        file.DeletedAt = DateTimeOffset.UtcNow;
        file.DeletedBy = userId;

        await fileRepo.SaveChangesAsync(cancellationToken);
    }
}
