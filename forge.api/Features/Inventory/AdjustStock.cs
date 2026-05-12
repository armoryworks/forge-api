using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record AdjustStockCommand(AdjustStockRequestModel Data) : IRequest;

public class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(x => x.Data.BinContentId).GreaterThan(0);
        RuleFor(x => x.Data.NewQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(200);
    }
}

public class AdjustStockHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<AdjustStockCommand>
{
    public async Task Handle(AdjustStockCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var content = await repo.FindBinContentWithLocationAsync(data.BinContentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bin content {data.BinContentId} not found");

        var delta = data.NewQuantity - (int)content.Quantity;

        content.Quantity = data.NewQuantity;

        if (data.NewQuantity == 0)
        {
            content.RemovedAt = DateTimeOffset.UtcNow;
            content.RemovedBy = userId;
        }

        // Create movement record for the adjustment
        var movement = new BinMovement
        {
            EntityType = content.EntityType,
            EntityId = content.EntityId,
            Quantity = Math.Abs(delta),
            LotNumber = content.LotNumber,
            FromLocationId = delta < 0 ? content.LocationId : null,
            ToLocationId = delta > 0 ? content.LocationId : null,
            MovedBy = userId,
            MovedAt = DateTimeOffset.UtcNow,
            Reason = BinMovementReason.Adjustment,
        };

        await repo.AddMovementAsync(movement, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
