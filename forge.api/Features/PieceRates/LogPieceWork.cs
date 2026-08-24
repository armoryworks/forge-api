using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Models.PieceRates;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PieceRates;

/// <summary>
/// Records pieces a worker completed on a date, at the rate in force THAT day —
/// an operation-specific rate wins over the part-level one; the resolved row is
/// pinned (FK + snapshot) so the earnings are immutable evidence.
/// </summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record LogPieceWorkCommand(
    int UserId,
    int PartId,
    int? OperationId,
    DateOnly WorkDate,
    decimal Quantity,
    string? Notes) : IRequest<PieceWorkEntryModel>;

public class LogPieceWorkValidator : AbstractValidator<LogPieceWorkCommand>
{
    public LogPieceWorkValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(512);
    }
}

public class LogPieceWorkHandler(AppDbContext db)
    : IRequestHandler<LogPieceWorkCommand, PieceWorkEntryModel>
{
    public async Task<PieceWorkEntryModel> Handle(LogPieceWorkCommand request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found.");

        // Resolve the rate as-of the work date: operation-specific first, part-level fallback.
        var d = request.WorkDate;
        var rate = await ResolveAsync(request.PartId, request.OperationId, d, ct)
            ?? (request.OperationId is not null ? await ResolveAsync(request.PartId, null, d, ct) : null)
            ?? throw new KeyNotFoundException(
                $"No piece rate in force for part {part.PartNumber} on {d:MM/dd/yyyy} — set one first.");

        var entry = new PieceWorkEntry
        {
            UserId = request.UserId,
            PartId = request.PartId,
            OperationId = request.OperationId,
            PieceRateId = rate.Id,
            WorkDate = d,
            Quantity = request.Quantity,
            RateSnapshot = rate.RatePerPiece,
            Earnings = Math.Round(request.Quantity * rate.RatePerPiece, 2, MidpointRounding.AwayFromZero),
            Notes = request.Notes,
        };
        db.PieceWorkEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("piece-work-logged",
            $"{request.Quantity:0.##} × {part.PartNumber} @ {rate.RatePerPiece:C4} = {entry.Earnings:C} on {d:MM/dd/yyyy}",
            ("PieceWorkEntry", entry.Id));
        await db.SaveChangesAsync(ct);

        return new PieceWorkEntryModel(
            entry.Id, user.Id, $"{user.LastName}, {user.FirstName}".Trim(' ', ','),
            part.Id, part.PartNumber, entry.OperationId, entry.WorkDate,
            entry.Quantity, entry.RateSnapshot, entry.Earnings, entry.Notes);
    }

    private Task<PieceRate?> ResolveAsync(int partId, int? operationId, DateOnly date, CancellationToken ct)
        => db.PieceRates.AsNoTracking()
            .Where(r => r.PartId == partId && r.OperationId == operationId
                     && r.EffectiveFrom <= date
                     && (r.EffectiveTo == null || r.EffectiveTo >= date))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
}
