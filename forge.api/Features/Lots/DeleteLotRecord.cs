using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Lots;

// L2: lots had no archive path despite the DeletedAt column. Soft-delete a mistaken lot.
public sealed record DeleteLotRecordCommand(int Id) : IRequest;

public sealed class DeleteLotRecordHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteLotRecordCommand>
{
    public async Task Handle(DeleteLotRecordCommand request, CancellationToken cancellationToken)
    {
        var lot = await db.LotRecords.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot {request.Id} not found");

        if (await HasTraceabilityHistoryAsync(lot.Id, lot.LotNumber, cancellationToken))
            throw new InvalidOperationException(
                $"Lot {lot.LotNumber} has traceability history and cannot be deleted. Adjust its quantity or place it on hold instead.");

        lot.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Lot {lot.LotNumber} deleted", ("Lot", lot.Id));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasTraceabilityHistoryAsync(int lotId, string lotNumber, CancellationToken ct) =>
        await db.LotConsumptions.AnyAsync(c => c.ConsumedLotId == lotId || c.ProducedLotId == lotId, ct)
        || await db.RecallAffectedLots.AnyAsync(r => r.LotId == lotId, ct)
        || await db.QcInspections.AnyAsync(i => i.LotNumber == lotNumber, ct)
        || await db.NonConformances.AnyAsync(n => n.LotNumber == lotNumber, ct)
        || await db.MaterialIssues.AnyAsync(m => m.LotNumber == lotNumber, ct)
        || await db.SpcMeasurements.AnyAsync(s => s.LotNumber == lotNumber, ct)
        || await db.BinContents.AnyAsync(b => b.LotNumber == lotNumber && b.Quantity > 0, ct);
}
