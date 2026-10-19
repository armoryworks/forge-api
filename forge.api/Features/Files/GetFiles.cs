using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Files;

public record GetFilesQuery(string EntityType, int EntityId) : IRequest<List<FileAttachmentResponseModel>>;

public class GetFilesHandler(
    IFileRepository repo,
    IEntityAccessResolver access,
    IHttpContextAccessor httpContext) : IRequestHandler<GetFilesQuery, List<FileAttachmentResponseModel>>
{
    public async Task<List<FileAttachmentResponseModel>> Handle(GetFilesQuery request, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(
                request.EntityType, request.EntityId, httpContext.HttpContext?.User, forWrite: false, cancellationToken))
            throw new ForbiddenException("You do not have access to this record's files.");

        return await repo.GetByEntityAsync(request.EntityType, request.EntityId, cancellationToken);
    }
}
