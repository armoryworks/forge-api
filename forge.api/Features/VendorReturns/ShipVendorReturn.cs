using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.VendorCredits;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorReturns;

public record ShipVendorReturnCommand(int Id, ShipVendorReturnRequestModel Request) : IRequest<VendorReturnDetailResponseModel>;

public class ShipVendorReturnValidator : AbstractValidator<ShipVendorReturnCommand>
{
    public ShipVendorReturnValidator()
    {
        RuleFor(x => x.Request.Carrier).MaximumLength(100);
        RuleFor(x => x.Request.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.Request.RmaNumber).MaximumLength(100);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

/// <summary>
/// Ships an open return to vendor: each line's quantity leaves its QcHold bin (a Return bin movement out), which is how
/// the lot leaves quality hold, and with CAP-ACCT-FULLGL on its value moves from inventory to the vendor returns
/// clearing account at the receipt cost. Refused when the held stock is no longer there
/// (<c>vendor-return-stock</c>).
/// </summary>
public class ShipVendorReturnHandler(
    AppDbContext db, IVendorCreditMemoPostingService posting, IClock clock, IMediator mediator)
    : IRequestHandler<ShipVendorReturnCommand, VendorReturnDetailResponseModel>
{
    public const string NotOpenCode = "vendor-return-not-open";
    public const string StockCode = "vendor-return-stock";

    public async Task<VendorReturnDetailResponseModel> Handle(ShipVendorReturnCommand command, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("vendor-returns.ship", command.Id.ToString(CultureInfo.InvariantCulture), ct);

        var rtv = await db.VendorReturns
            .Include(r => r.Lines)
            .Include(r => r.NonConformance)
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor return {command.Id} not found");
        if (rtv.Status != VendorReturnStatus.Open)
            throw VendorCreditMemoRules.Failure("Id", NotOpenCode, $"Return {rtv.ReturnNumber} is {rtv.Status}; only an open return can ship.");

        var userId = VendorCreditMemoRules.UserId(db);
        var now = clock.UtcNow;
        var request = command.Request;
        var shipDate = request.ShipDate ?? now;

        foreach (var line in rtv.Lines.OrderBy(l => l.LineNumber))
        {
            var bin = line.BinContentId is int binId
                ? await db.BinContents.FirstOrDefaultAsync(b => b.Id == binId, ct)
                : null;
            if (bin is null || bin.RemovedAt != null || bin.Status != BinContentStatus.QcHold
                || bin.Quantity - bin.ReservedQuantity < line.Quantity)
                throw VendorCreditMemoRules.Failure("Id", StockCode,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Return {rtv.ReturnNumber} line {line.LineNumber}: {line.Quantity:0.####} of lot {line.LotNumber} is no longer on quality hold in its bin."));

            bin.Quantity -= line.Quantity;
            if (bin.Quantity == 0m)
            {
                bin.RemovedAt = now;
                bin.RemovedBy = userId;
            }
            var movement = new BinMovement
            {
                EntityType = "part",
                EntityId = line.PartId,
                Quantity = line.Quantity,
                LotNumber = line.LotNumber,
                FromLocationId = bin.LocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.Return,
                Notes = $"Returned to vendor on {rtv.ReturnNumber}",
            };
            db.BinMovements.Add(movement);
            await db.SaveChangesAsync(ct);
            line.BinMovementId = movement.Id;
        }

        rtv.Status = VendorReturnStatus.Shipped;
        rtv.ShipDate = shipDate;
        rtv.Carrier = Trim(request.Carrier) ?? rtv.Carrier;
        rtv.TrackingNumber = Trim(request.TrackingNumber) ?? rtv.TrackingNumber;
        rtv.RmaNumber = Trim(request.RmaNumber) ?? rtv.RmaNumber;
        rtv.Notes = Trim(request.Notes) ?? rtv.Notes;
        rtv.ShippedByUserId = userId;
        await db.SaveChangesAsync(ct);

        var relieved = await posting.PostReturnShippedAsync(rtv.Id, await CreditMemoRules.ShopTodayAsync(db, shipDate, ct), userId, ct);
        foreach (var line in rtv.Lines)
            line.ShippedCost = relieved.TryGetValue(line.Id, out var value) ? value : Math.Round(line.Quantity * line.UnitCost, 2, MidpointRounding.AwayFromZero);

        var quantity = rtv.Lines.Sum(l => l.Quantity);
        var shipped = string.Create(CultureInfo.InvariantCulture, $"Return to vendor {rtv.ReturnNumber} shipped: {quantity:0.####} left quality hold")
            + (rtv.TrackingNumber is { Length: > 0 } tracking ? $" (tracking {tracking})" : string.Empty);
        if (rtv.NonConformanceId is int ncrId)
            db.LogActivityAt("shipped", shipped, ("VendorReturn", rtv.Id), ("NonConformance", ncrId), ("Vendor", rtv.VendorId));
        else
            db.LogActivityAt("shipped", shipped, ("VendorReturn", rtv.Id), ("Vendor", rtv.VendorId));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetVendorReturnByIdQuery(rtv.Id), ct);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
