using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PieceRates;

/// <summary>Soft-deletes a mis-keyed piece-work entry (the correction path).</summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record DeletePieceWorkEntryCommand(int Id) : IRequest;

public class DeletePieceWorkEntryHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeletePieceWorkEntryCommand>
{
    public async Task Handle(DeletePieceWorkEntryCommand request, CancellationToken ct)
    {
        var entry = await db.PieceWorkEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Piece-work entry {request.Id} not found.");

        entry.DeletedAt = clock.UtcNow;
        db.LogActivityAt("piece-work-deleted",
            $"Removed piece-work entry {entry.Id} ({entry.Quantity:0.##} pcs, {entry.Earnings:C})",
            ("PieceWorkEntry", entry.Id));
        await db.SaveChangesAsync(ct);
    }
}
