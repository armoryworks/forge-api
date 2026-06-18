using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record ReceiveStockCommand(ReceiveStockRequestModel Data) : IRequest;

public class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
{
    public ReceiveStockCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        // LocationId is optional — single-location mode omits it and uses the default.
        RuleFor(x => x.Data.LocationId!.Value).GreaterThan(0).When(x => x.Data.LocationId.HasValue);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.Reason).MaximumLength(500).When(x => x.Data.Reason is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
        RuleFor(x => x.Data.LotNumber).MaximumLength(100).When(x => x.Data.LotNumber is not null);
    }
}

/// <summary>
/// Friendly stock-in for a standalone inventory shop: add a quantity of a part to
/// stock without a purchase order. Uses the same bin primitives as the receiving
/// pipeline but needs no PO, so a clerk can record "stock arrived" in one step. It
/// is additive — it bumps an existing bin or opens a new one. When no location is
/// supplied (single-location mode) the default location is used. Writes a
/// BinMovement (Reason = Receive) as the audit trail; never posts to a ledger.
/// </summary>
public class ReceiveStockHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ReceiveStockCommand>
{
    public async Task Handle(ReceiveStockCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTimeOffset.UtcNow;

        int locationId;
        if (data.LocationId is int requested)
        {
            _ = await repo.FindLocationAsync(requested, cancellationToken)
                ?? throw new KeyNotFoundException($"Location {requested} not found.");
            locationId = requested;
        }
        else
        {
            // Single-location mode: no location chosen, so use (or create) the default.
            locationId = (await repo.EnsureDefaultLocationAsync(cancellationToken)).Id;
        }

        var existing = await repo.FindActiveBinContentByPartLocationAsync(data.PartId, locationId, cancellationToken);
        if (existing is null)
        {
            await repo.AddBinContentAsync(new BinContent
            {
                EntityType = "part",
                EntityId = data.PartId,
                LocationId = locationId,
                Quantity = data.Quantity,
                LotNumber = string.IsNullOrWhiteSpace(data.LotNumber) ? null : data.LotNumber!.Trim(),
                Status = BinContentStatus.Stored,
                PlacedBy = userId,
                PlacedAt = now,
                Notes = data.Notes,
            }, cancellationToken);
        }
        else
        {
            existing.Quantity += data.Quantity;
        }

        await repo.AddMovementAsync(new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = data.Quantity,
            FromLocationId = null,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Receive,
            Notes = BuildNote(data),
        }, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string BuildNote(ReceiveStockRequestModel d)
    {
        var note = string.IsNullOrWhiteSpace(d.Reason) ? "Manual receipt" : d.Reason!.Trim();
        if (!string.IsNullOrWhiteSpace(d.Notes)) note += $" — {d.Notes!.Trim()}";
        if (!string.IsNullOrWhiteSpace(d.LotNumber)) note += $" (Lot {d.LotNumber!.Trim()})";
        return note.Length > 1000 ? note[..1000] : note;
    }
}
