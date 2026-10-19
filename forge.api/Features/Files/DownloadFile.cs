using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Files;

public record DownloadFileQuery(int Id) : IRequest<FileDownloadResult>;

public record FileDownloadResult(Stream Stream, string ContentType, string FileName);

public class DownloadFileHandler(
    IFileRepository fileRepo,
    IStorageService storage,
    IEntityAccessResolver access,
    IHttpContextAccessor httpContext) : IRequestHandler<DownloadFileQuery, FileDownloadResult>
{
    public async Task<FileDownloadResult> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var file = await fileRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"File {request.Id} not found.");

        if (!await access.CanAccessFileAsync(file, httpContext.HttpContext?.User, forWrite: false, cancellationToken))
            throw new ForbiddenException("You do not have access to this file.");

        var stream = await storage.DownloadAsync(file.BucketName, file.ObjectKey, cancellationToken);

        return new FileDownloadResult(stream, file.ContentType, file.FileName);
    }
}
