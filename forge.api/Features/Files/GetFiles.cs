using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Files;

public record GetFilesQuery(string EntityType, int EntityId) : IRequest<List<FileAttachmentResponseModel>>;

public class GetFilesHandler(IFileRepository repo) : IRequestHandler<GetFilesQuery, List<FileAttachmentResponseModel>>
{
    public Task<List<FileAttachmentResponseModel>> Handle(GetFilesQuery request, CancellationToken cancellationToken)
        => repo.GetByEntityAsync(request.EntityType, request.EntityId, cancellationToken);
}
