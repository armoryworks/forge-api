using System.Security.Claims;
using MediatR;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record DeleteBOMEntryCommand(int ParentPartId, int BomEntryId) : IRequest<PartDetailResponseModel>;

public class DeleteBOMEntryHandler(
    IPartRepository repo,
    IBomRevisionService bomRevisions,
    IHttpContextAccessor httpContext) : IRequestHandler<DeleteBOMEntryCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(DeleteBOMEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindBomEntryAsync(request.BomEntryId, request.ParentPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"BOM entry {request.BomEntryId} not found on part {request.ParentPartId}");

        await repo.RemoveBomEntryAsync(entry);

        // Phase 3 H4 / WU-20 — removing a component is a structural change.
        var userId = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : (int?)null;
        await bomRevisions.CaptureCurrentStateAsync(request.ParentPartId, userId, "Component removed", cancellationToken);

        return (await repo.GetDetailAsync(request.ParentPartId, cancellationToken))!;
    }
}
