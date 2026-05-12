using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Files;

public record DownloadFileQuery(int Id) : IRequest<FileDownloadResult>;

public record FileDownloadResult(Stream Stream, string ContentType, string FileName);

public class DownloadFileHandler(
    IFileRepository fileRepo,
    IStorageService storage,
    IHttpContextAccessor httpContext) : IRequestHandler<DownloadFileQuery, FileDownloadResult>
{
    public async Task<FileDownloadResult> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var file = await fileRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"File {request.Id} not found.");

        // Check role-based access restriction
        if (!string.IsNullOrEmpty(file.RequiredRole))
        {
            var user = httpContext.HttpContext?.User;
            if (user == null || !user.IsInRole(file.RequiredRole))
            {
                throw new UnauthorizedAccessException($"Access denied. Role '{file.RequiredRole}' is required.");
            }
        }

        var stream = await storage.DownloadAsync(file.BucketName, file.ObjectKey, cancellationToken);

        return new FileDownloadResult(stream, file.ContentType, file.FileName);
    }
}
