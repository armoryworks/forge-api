using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Posts a new effective-dated PartPrice row. Closes out any previously open
/// row (one with EffectiveTo IS NULL) by setting its EffectiveTo to the new
/// row's EffectiveFrom — keeps the history coherent with at most one open
/// row at a time.
/// </summary>
public record AddPartPriceCommand(
    int PartId,
    decimal UnitPrice,
    string? Currency,
    DateTimeOffset? EffectiveFrom,
    string? Notes) : IRequest<PartPriceResponseModel>;

public class AddPartPriceHandler(AppDbContext db, ICurrencyService currency, IClock clock)
    : IRequestHandler<AddPartPriceCommand, PartPriceResponseModel>
{
    public async Task<PartPriceResponseModel> Handle(AddPartPriceCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var effectiveFrom = (request.EffectiveFrom ?? now).ToUniversalTime();

        var currencyCode = string.IsNullOrWhiteSpace(request.Currency)
            ? await currency.GetBaseCurrencyAsync(ct)
            : request.Currency.Trim().ToUpperInvariant();

        // Close out any currently-open row (the dispatch contract: only one
        // row may have EffectiveTo == null at a time). We close every row
        // whose EffectiveFrom is strictly before the new effective date —
        // posting a row dated earlier than an existing open row is a no-op
        // for the open row (the timeline is layered, not chained).
        var open = await db.PartPrices
            .Where(p => p.PartId == request.PartId
                && p.EffectiveTo == null
                && p.EffectiveFrom < effectiveFrom)
            .ToListAsync(ct);

        foreach (var price in open)
            price.EffectiveTo = effectiveFrom;

        var newPrice = new PartPrice
        {
            PartId = request.PartId,
            UnitPrice = request.UnitPrice,
            Currency = currencyCode,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            Notes = request.Notes,
            CreatedAt = now,
        };

        db.PartPrices.Add(newPrice);
        await db.SaveChangesAsync(ct);

        return new PartPriceResponseModel(
            newPrice.Id,
            newPrice.PartId,
            newPrice.UnitPrice,
            newPrice.Currency,
            newPrice.EffectiveFrom,
            newPrice.EffectiveTo,
            newPrice.Notes,
            newPrice.CreatedAt);
    }
}
