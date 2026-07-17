using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record UseStockCommand(UseStockRequestModel Data) : IRequest;

public class UseStockCommandValidator : AbstractValidator<UseStockCommand>
{
    public UseStockCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        // LocationId is optional — single-location mode omits it and uses the default.
        RuleFor(x => x.Data.LocationId!.Value).GreaterThan(0).When(x => x.Data.LocationId.HasValue);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.Reason).MaximumLength(500).When(x => x.Data.Reason is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

/// <summary>
/// Friendly stock-out for a standalone inventory shop: consume a quantity of a
/// part without a shipment or job issue, so a clerk can record "stock used" in one
/// step. When no location is supplied (single-location mode) the default location
/// is used. The amount used can never drop on-hand below what is reserved (S-RI1),
/// nor below zero. Writes a BinMovement (Reason = Issue) as the audit trail; never
/// posts to a ledger.
/// </summary>
public class UseStockHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<UseStockCommand>
{
    public async Task Handle(UseStockCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTimeOffset.UtcNow;

        if (!await repo.PartExistsAsync(data.PartId, cancellationToken))
            throw new KeyNotFoundException($"Part {data.PartId} not found");

        int locationId;
        if (data.LocationId is int requested)
        {
            _ = await repo.FindLocationAsync(requested, cancellationToken)
                ?? throw new KeyNotFoundException($"Location {requested} not found.");
            locationId = requested;
        }
        else
        {
            locationId = (await repo.EnsureDefaultLocationAsync(cancellationToken)).Id;
        }

        var existing = await repo.FindActiveBinContentByPartLocationAsync(data.PartId, locationId, cancellationToken)
            ?? throw new InvalidOperationException(
                "No stock of this part is on hand to use. Receive stock before using it.");

        // S-RI1: reserved units are spoken for, so only the free balance can be used.
        var available = existing.Quantity - existing.ReservedQuantity;
        if (data.Quantity > available)
            throw new InvalidOperationException(
                $"Cannot use {data.Quantity}: only {available} available " +
                $"({existing.ReservedQuantity} of {existing.Quantity} on hand are reserved).");

        existing.Quantity -= data.Quantity;
        if (existing.Quantity == 0)
        {
            existing.RemovedAt = now;
            existing.RemovedBy = userId;
        }

        await repo.AddMovementAsync(new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = data.Quantity,
            FromLocationId = locationId,
            ToLocationId = null,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Issue,
            Notes = BuildNote(data),
        }, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string BuildNote(UseStockRequestModel d)
    {
        var note = string.IsNullOrWhiteSpace(d.Reason) ? "Manual use" : d.Reason!.Trim();
        if (!string.IsNullOrWhiteSpace(d.Notes)) note += $" — {d.Notes!.Trim()}";
        return note.Length > 1000 ? note[..1000] : note;
    }
}
