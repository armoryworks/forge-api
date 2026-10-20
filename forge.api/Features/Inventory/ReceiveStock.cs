using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

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
/// BinMovement (Reason = Receive) as the audit trail, and posts it as an inventory
/// adjustment when CAP-ACCT-FULLGL is on, so the ledger and valuation store follow
/// the bins.
/// </summary>
public class ReceiveStockHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext,
    AppDbContext db,
    IInventoryAdjustmentPostingService posting)
    : IRequestHandler<ReceiveStockCommand>
{
    public async Task Handle(ReceiveStockCommand request, CancellationToken cancellationToken)
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
            // Single-location mode: no location chosen, so use (or create) the default.
            locationId = (await repo.EnsureDefaultLocationAsync(cancellationToken)).Id;
        }

        var lot = string.IsNullOrWhiteSpace(data.LotNumber) ? null : data.LotNumber.Trim();
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var existing = await repo.FindActiveBinContentByPartLocationLotAsync(
            data.PartId, locationId, lot, BinContentStatus.Stored, cancellationToken);
        if (existing is null)
        {
            await repo.AddBinContentAsync(new BinContent
            {
                EntityType = "part",
                EntityId = data.PartId,
                LocationId = locationId,
                Quantity = data.Quantity,
                LotNumber = lot,
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

        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = data.Quantity,
            LotNumber = lot,
            FromLocationId = null,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Receive,
            Notes = BuildNote(data),
        };
        await repo.AddMovementAsync(movement, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);

        await posting.PostAdjustmentAsync(new InventoryAdjustmentPosting(
            "BinMovement", movement.Id, data.PartId, data.Quantity,
            DateOnly.FromDateTime(movement.MovedAt.UtcDateTime), userId, "manual receipt"), cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }

    private static string BuildNote(ReceiveStockRequestModel d)
    {
        var note = string.IsNullOrWhiteSpace(d.Reason) ? "Manual receipt" : d.Reason!.Trim();
        if (!string.IsNullOrWhiteSpace(d.Notes)) note += $" — {d.Notes!.Trim()}";
        if (!string.IsNullOrWhiteSpace(d.LotNumber)) note += $" (Lot {d.LotNumber!.Trim()})";
        return note.Length > 1000 ? note[..1000] : note;
    }
}
