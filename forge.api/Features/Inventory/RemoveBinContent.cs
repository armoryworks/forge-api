using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public sealed record RemoveBinContentCommand(int Id) : IRequest;

public sealed class RemoveBinContentHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    IInventoryAdjustmentPostingService posting)
    : IRequestHandler<RemoveBinContentCommand>
{
    public async Task Handle(RemoveBinContentCommand request, CancellationToken cancellationToken)
    {
        var content = await repo.FindBinContentAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bin content {request.Id} not found");

        if (content.RemovedAt is not null)
            throw new InvalidOperationException("This bin content has already been removed.");

        // S-RI1: a bin with committed reservations can't be removed wholesale —
        // that would silently drop reserved stock and inflate `available`.
        if (content.ReservedQuantity > 0)
            throw new InvalidOperationException(
                $"Cannot remove this bin: {content.ReservedQuantity} unit(s) are reserved. Release the reservation first.");

        var userId = int.Parse(
            httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var removedQuantity = content.Quantity;
        content.Quantity = 0;
        content.RemovedAt = DateTimeOffset.UtcNow;
        content.RemovedBy = userId;

        var movement = new BinMovement
        {
            EntityType = content.EntityType,
            EntityId = content.EntityId,
            Quantity = removedQuantity,
            LotNumber = content.LotNumber,
            FromLocationId = content.LocationId,
            ToLocationId = null,
            MovedBy = userId,
            MovedAt = DateTimeOffset.UtcNow,
            Reason = BinMovementReason.Adjustment
        };

        await repo.AddMovementAsync(movement, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        if (string.Equals(content.EntityType, "part", StringComparison.OrdinalIgnoreCase))
            await posting.PostAdjustmentAsync(new InventoryAdjustmentPosting(
                "BinMovement", movement.Id, content.EntityId, -removedQuantity,
                DateOnly.FromDateTime(movement.MovedAt.UtcDateTime), userId, "bin content removed"), cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
