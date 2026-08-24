using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models.PieceRates;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PieceRates;

/// <summary>
/// Sets the piece rate for a part (optionally one operation) from an effective
/// date: the open timeline row is closed the day before, a new open row starts.
/// Rates are timelines — history is never edited; a change effective mid-week
/// pays that week's earlier pieces at the old rate automatically.
/// </summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record SetPieceRateCommand(
    int PartId,
    int? OperationId,
    decimal RatePerPiece,
    DateOnly? EffectiveFrom,
    string? Notes) : IRequest<PieceRateModel>;

public class SetPieceRateValidator : AbstractValidator<SetPieceRateCommand>
{
    public SetPieceRateValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.RatePerPiece).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(512);
    }
}

public class SetPieceRateHandler(AppDbContext db, IClock clock)
    : IRequestHandler<SetPieceRateCommand, PieceRateModel>
{
    public async Task<PieceRateModel> Handle(SetPieceRateCommand request, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found.");

        var effectiveFrom = request.EffectiveFrom
            ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var active = await db.PieceRates.FirstOrDefaultAsync(
            r => r.PartId == request.PartId
              && r.OperationId == request.OperationId
              && r.EffectiveTo == null,
            ct);

        if (active is not null)
        {
            if (effectiveFrom <= active.EffectiveFrom)
                throw new InvalidOperationException(
                    $"The new rate must start after the current rate's effective date ({active.EffectiveFrom:MM/dd/yyyy}). "
                    + "History is never rewritten — a retroactive change is a payroll adjustment, not a rate edit.");
            active.EffectiveTo = effectiveFrom.AddDays(-1);
        }

        var rate = new PieceRate
        {
            PartId = request.PartId,
            OperationId = request.OperationId,
            RatePerPiece = request.RatePerPiece,
            EffectiveFrom = effectiveFrom,
            Notes = request.Notes,
        };
        db.PieceRates.Add(rate);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("piece-rate-set",
            $"Piece rate {request.RatePerPiece:C4}/pc effective {effectiveFrom:MM/dd/yyyy}"
            + (request.OperationId is not null ? $" (operation {request.OperationId})" : string.Empty),
            ("Part", part.Id));
        await db.SaveChangesAsync(ct);

        return new PieceRateModel(
            rate.Id, part.Id, part.PartNumber, part.Description, rate.OperationId,
            rate.RatePerPiece, rate.EffectiveFrom, rate.EffectiveTo, rate.Notes);
    }
}
