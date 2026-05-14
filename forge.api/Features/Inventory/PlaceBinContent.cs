using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record PlaceBinContentCommand(PlaceBinContentRequestModel Data) : IRequest<BinContentResponseModel>;

public class PlaceBinContentCommandValidator : AbstractValidator<PlaceBinContentCommand>
{
    public PlaceBinContentCommandValidator()
    {
        RuleFor(x => x.Data.LocationId).GreaterThan(0);
        RuleFor(x => x.Data.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.EntityId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
    }
}

public class PlaceBinContentHandler(IInventoryRepository repo, IHttpContextAccessor httpContext) : IRequestHandler<PlaceBinContentCommand, BinContentResponseModel>
{
    public async Task<BinContentResponseModel> Handle(PlaceBinContentCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var location = await repo.FindLocationAsync(data.LocationId, cancellationToken)
            ?? throw new KeyNotFoundException("Location not found.");

        if (location.LocationType != LocationType.Bin)
            throw new InvalidOperationException("Can only place items in bin-level locations.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var content = new BinContent
        {
            LocationId = data.LocationId,
            EntityType = data.EntityType,
            EntityId = data.EntityId,
            Quantity = data.Quantity,
            LotNumber = data.LotNumber,
            JobId = data.JobId,
            Status = data.Status,
            PlacedBy = userId,
            PlacedAt = DateTimeOffset.UtcNow,
            Notes = data.Notes,
        };

        await repo.AddBinContentAsync(content, cancellationToken);

        var movement = new BinMovement
        {
            EntityType = data.EntityType,
            EntityId = data.EntityId,
            Quantity = data.Quantity,
            LotNumber = data.LotNumber,
            ToLocationId = data.LocationId,
            MovedBy = userId,
            MovedAt = DateTimeOffset.UtcNow,
            Reason = BinMovementReason.Receive,
        };

        await repo.AddMovementAsync(movement, cancellationToken);

        return new BinContentResponseModel(
            content.Id, content.LocationId, location.Name, location.Name,
            content.EntityType, content.EntityId, $"{content.EntityType}:{content.EntityId}",
            content.Quantity, content.LotNumber, content.JobId, null,
            content.Status, content.PlacedAt);
    }
}
