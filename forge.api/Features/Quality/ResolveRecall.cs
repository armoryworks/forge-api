using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record ResolveRecallCommand(int Id, ResolveRecallRequestModel Data) : IRequest<RecallDetailResponseModel>;

public class ResolveRecallValidator : AbstractValidator<ResolveRecallCommand>
{
    public ResolveRecallValidator()
    {
        RuleFor(x => x.Data.ResolutionNotes)
            .NotEmpty()
            .WithMessage("Resolution notes are required to resolve a recall")
            .MaximumLength(2000);
    }
}

public class ResolveRecallHandler(AppDbContext db, IClock clock) : IRequestHandler<ResolveRecallCommand, RecallDetailResponseModel>
{
    public async Task<RecallDetailResponseModel> Handle(ResolveRecallCommand request, CancellationToken cancellationToken)
    {
        var recall = await db.Recalls
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Recall {request.Id} not found.");

        if (recall.Status != RecallStatus.Active)
            throw new InvalidOperationException($"Recall {recall.Id} is {recall.Status}; only an active recall can be resolved.");

        recall.Status = RecallStatus.Resolved;
        recall.ResolvedAt = clock.UtcNow;
        recall.ResolutionNotes = request.Data.ResolutionNotes?.Trim();
        await db.SaveChangesAsync(cancellationToken);

        if (request.Data.ReleaseHeldStock)
        {
            var lots = await db.RecallAffectedLots
                .IgnoreQueryFilters()
                .Where(a => a.RecallId == recall.Id && a.DeletedAt == null)
                .Select(a => new { a.Lot.Id, a.Lot.PartId, a.Lot.LotNumber })
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var lot in lots)
                await ReleaseLotAsync(recall.Id, lot.Id, lot.PartId, lot.LotNumber, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await RecallMapping.LoadDetailAsync(db, recall.Id, cancellationToken);
    }

    private async Task ReleaseLotAsync(int recallId, int lotId, int partId, string lotNumber, CancellationToken ct)
    {
        if (await db.NcrsHoldingLot(partId, lotNumber).AnyAsync(ct)
            || await db.IsLotUnderActiveRecallAsync(lotNumber, ct))
            return;

        var held = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LotNumber == lotNumber
                && bc.RemovedAt == null
                && bc.Status == BinContentStatus.QcHold)
            .ToListAsync(ct);
        if (held.Count == 0)
            return;

        var released = await ReceivingStockHold.ReleaseHeldRowsAsync(db, partId, lotNumber, held, ct);
        if (released > 0)
            db.LogActivityAt(
                "quality-hold-released",
                $"Lot {lotNumber} released from quality hold: {released:0.####} released when recall {recallId} was resolved",
                ("Lot", lotId));
    }
}
