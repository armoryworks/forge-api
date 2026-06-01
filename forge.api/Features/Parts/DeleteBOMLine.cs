using System.Security.Claims;
using MediatR;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record DeleteBOMLineCommand(int ParentPartId, int BomLineId) : IRequest<PartDetailResponseModel>;

public class DeleteBOMLineHandler(
    IPartRepository repo,
    IBomRevisionService bomRevisions,
    IHttpContextAccessor httpContext) : IRequestHandler<DeleteBOMLineCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(DeleteBOMLineCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindBomLineAsync(request.BomLineId, request.ParentPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"BOM line {request.BomLineId} not found on part {request.ParentPartId}");

        await repo.RemoveBomLineAsync(entry);

        // Phase 3 H4 / WU-20 — removing a component is a structural change.
        var userId = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : (int?)null;
        await bomRevisions.CaptureCurrentStateAsync(request.ParentPartId, userId, "Component removed", cancellationToken);

        return (await repo.GetDetailAsync(request.ParentPartId, cancellationToken))!;
    }
}
