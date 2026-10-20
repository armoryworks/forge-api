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

public record SetOnHandQuantityCommand(SetOnHandQuantityRequestModel Data) : IRequest;

public class SetOnHandQuantityCommandValidator : AbstractValidator<SetOnHandQuantityCommand>
{
    public SetOnHandQuantityCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        // LocationId is optional — when omitted, single-location mode uses the
        // default location. When supplied it must be a real id.
        RuleFor(x => x.Data.LocationId!.Value).GreaterThan(0).When(x => x.Data.LocationId.HasValue);
        RuleFor(x => x.Data.Quantity).GreaterThanOrEqualTo(0);
        // Reason is mandatory precisely because there's no PO paper trail.
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

/// <summary>
/// Manual inventory override (forge-api#4): directly set the on-hand quantity of
/// an existing part at a location, bypassing receiving (RFQ → PO → receive).
/// The quantity is the location total across every lot held there. Creates the bin
/// content when none exists yet (opening stock / found inventory); otherwise a rise
/// lands on the un-lotted content (or the newest lot when all stock is lotted) and a
/// drop draws down un-lotted content first, then the oldest lots. Writes BinMovement
/// audit rows carrying the mandatory reason + optional PO/vendor provenance, and posts
/// each movement as an inventory adjustment when CAP-ACCT-FULLGL is on, so the ledger
/// and valuation store follow the bins (see
/// docs/delivery/in-progress/inventory-override/design.md).
/// </summary>
public class SetOnHandQuantityHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext,
    IClock clock,
    AppDbContext db,
    IInventoryAdjustmentPostingService posting)
    : IRequestHandler<SetOnHandQuantityCommand>
{
    public async Task Handle(SetOnHandQuantityCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

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

        var rows = await repo.GetActiveBinContentsByPartLocationAsync(data.PartId, locationId, cancellationToken);
        var auditNote = BuildNote(data);
        var movements = new List<(BinMovement Movement, decimal Delta)>();

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (rows.Count == 0)
        {
            // Opening stock / found inventory — no prior bin content at this location.
            await repo.AddBinContentAsync(new BinContent
            {
                EntityType = "part",
                EntityId = data.PartId,
                LocationId = locationId,
                Quantity = data.Quantity,
                Status = BinContentStatus.Stored,
                PlacedBy = userId,
                PlacedAt = now,
                Notes = data.Notes,
            }, cancellationToken);
            await AddAdjustmentAsync(data.PartId, locationId, data.Quantity, null, userId, now, auditNote, movements, cancellationToken);
        }
        else
        {
            // S-RI1: never let the override drop on-hand below what's reserved.
            var reserved = rows.Sum(r => r.ReservedQuantity);
            if (data.Quantity < reserved)
                throw new InvalidOperationException(
                    $"Cannot set this location to {data.Quantity}: {reserved} unit(s) are reserved. " +
                    "Release the reservation first.");

            var delta = data.Quantity - rows.Sum(r => r.Quantity);
            if (delta < 0)
            {
                foreach (var (row, taken) in BinContentDrawDown.Take(rows, -delta, userId, now))
                    await AddAdjustmentAsync(data.PartId, locationId, -taken, row.LotNumber, userId, now, auditNote, movements, cancellationToken);
            }
            else
            {
                var target = rows.FirstOrDefault(r => r.LotNumber is null)
                    ?? rows.OrderByDescending(r => r.PlacedAt).ThenByDescending(r => r.Id).First();
                target.Quantity += delta;
                await AddAdjustmentAsync(data.PartId, locationId, delta, target.LotNumber, userId, now, auditNote, movements, cancellationToken);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);

        foreach (var (movement, delta) in movements)
            await posting.PostAdjustmentAsync(new InventoryAdjustmentPosting(
                "BinMovement", movement.Id, data.PartId, delta,
                DateOnly.FromDateTime(now.UtcDateTime), userId, "on-hand override"), cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }

    private async Task AddAdjustmentAsync(
        int partId, int locationId, decimal delta, string? lotNumber, int userId, DateTimeOffset now, string note,
        List<(BinMovement Movement, decimal Delta)> movements, CancellationToken ct)
    {
        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = partId,
            Quantity = Math.Abs(delta),
            LotNumber = lotNumber,
            FromLocationId = delta < 0 ? locationId : null,
            ToLocationId = delta >= 0 ? locationId : null,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Adjustment,
            Notes = note,
        };
        await repo.AddMovementAsync(movement, ct);
        movements.Add((movement, delta));
    }

    private static string BuildNote(SetOnHandQuantityRequestModel d)
    {
        var note = d.Reason.Trim();
        if (!string.IsNullOrWhiteSpace(d.Notes)) note += $" — {d.Notes!.Trim()}";
        var provenance = new List<string>();
        if (d.SourcePurchaseOrderId is int po) provenance.Add($"PO #{po}");
        if (d.VendorId is int v) provenance.Add($"Vendor #{v}");
        if (provenance.Count > 0) note += $" ({string.Join(", ", provenance)})";
        return note.Length > 1000 ? note[..1000] : note;
    }
}
