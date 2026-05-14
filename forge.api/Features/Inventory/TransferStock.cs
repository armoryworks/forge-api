using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record TransferStockCommand(TransferStockRequestModel Data) : IRequest;

public class TransferStockCommandValidator : AbstractValidator<TransferStockCommand>
{
    public TransferStockCommandValidator()
    {
        RuleFor(x => x.Data.SourceBinContentId).GreaterThan(0);
        RuleFor(x => x.Data.DestinationLocationId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
    }
}

public class TransferStockHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<TransferStockCommand>
{
    public async Task Handle(TransferStockCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var source = await repo.FindBinContentWithLocationAsync(data.SourceBinContentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bin content {data.SourceBinContentId} not found");

        if (source.Quantity < data.Quantity)
            throw new InvalidOperationException(
                $"Cannot transfer {data.Quantity} — only {source.Quantity} available");

        var destination = await repo.FindLocationAsync(data.DestinationLocationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Destination location {data.DestinationLocationId} not found");

        // Reduce source quantity
        source.Quantity -= data.Quantity;
        if (source.Quantity == 0)
        {
            source.RemovedAt = DateTimeOffset.UtcNow;
            source.RemovedBy = userId;
        }

        // Create destination bin content
        var destContent = new BinContent
        {
            LocationId = data.DestinationLocationId,
            EntityType = source.EntityType,
            EntityId = source.EntityId,
            Quantity = data.Quantity,
            LotNumber = source.LotNumber,
            JobId = source.JobId,
            Status = source.Status,
            PlacedBy = userId,
            PlacedAt = DateTimeOffset.UtcNow,
            Notes = data.Notes,
        };

        await repo.AddBinContentAsync(destContent, cancellationToken);

        // Movement out from source
        var movementOut = new BinMovement
        {
            EntityType = source.EntityType,
            EntityId = source.EntityId,
            Quantity = data.Quantity,
            LotNumber = source.LotNumber,
            FromLocationId = source.LocationId,
            ToLocationId = data.DestinationLocationId,
            MovedBy = userId,
            MovedAt = DateTimeOffset.UtcNow,
            Reason = BinMovementReason.Transfer,
        };

        await repo.AddMovementAsync(movementOut, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
