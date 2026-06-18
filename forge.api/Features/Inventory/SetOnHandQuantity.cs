using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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
/// Creates the bin content when none exists yet (opening stock / found inventory)
/// or adjusts the existing one. Operational only — writes a BinMovement audit row
/// carrying the mandatory reason + optional PO/vendor provenance, and never posts
/// to a general ledger (see docs/delivery/in-progress/inventory-override/design.md).
/// </summary>
public class SetOnHandQuantityHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<SetOnHandQuantityCommand>
{
    public async Task Handle(SetOnHandQuantityCommand request, CancellationToken cancellationToken)
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
        var auditNote = BuildNote(data);

        decimal delta;
        if (existing is null)
        {
            // Opening stock / found inventory — no prior bin content at this location.
            delta = data.Quantity;
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
        }
        else
        {
            // S-RI1: never let the override drop on-hand below what's reserved.
            if (data.Quantity < existing.ReservedQuantity)
                throw new InvalidOperationException(
                    $"Cannot set this bin to {data.Quantity}: {existing.ReservedQuantity} unit(s) are reserved. " +
                    "Release the reservation first.");

            delta = data.Quantity - existing.Quantity;
            existing.Quantity = data.Quantity;
            if (data.Quantity == 0)
            {
                existing.RemovedAt = now;
                existing.RemovedBy = userId;
            }
        }

        await repo.AddMovementAsync(new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = Math.Abs(delta),
            FromLocationId = delta < 0 ? locationId : null,
            ToLocationId = delta >= 0 ? locationId : null,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Adjustment,
            Notes = auditNote,
        }, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);
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
