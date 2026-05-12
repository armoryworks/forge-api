using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Inventory;

public sealed record RemoveBinContentCommand(int Id) : IRequest;

public sealed class RemoveBinContentHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<RemoveBinContentCommand>
{
    public async Task Handle(RemoveBinContentCommand request, CancellationToken cancellationToken)
    {
        var content = await repo.FindBinContentAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bin content {request.Id} not found");

        var userId = int.Parse(
            httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        content.RemovedAt = DateTimeOffset.UtcNow;
        content.RemovedBy = userId;

        var movement = new BinMovement
        {
            EntityType = content.EntityType,
            EntityId = content.EntityId,
            Quantity = content.Quantity,
            LotNumber = content.LotNumber,
            FromLocationId = content.LocationId,
            ToLocationId = null,
            MovedBy = userId,
            MovedAt = DateTimeOffset.UtcNow,
            Reason = BinMovementReason.Adjustment
        };

        await repo.AddMovementAsync(movement, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
