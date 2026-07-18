using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Barcodes;

/// <summary>
/// Remove a part's licensed GTIN — it reverts to its free internal code (its barcode is re-synced back to
/// the internal scheme). The item reference is not reclaimed. Gated by CAP-MD-GS1.
/// </summary>
public record RemovePartGtinCommand(int PartId) : IRequest;

public class RemovePartGtinHandler(AppDbContext db, IBarcodeService barcodes) : IRequestHandler<RemovePartGtinCommand>
{
    public async Task Handle(RemovePartGtinCommand request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found.");

        if (string.IsNullOrWhiteSpace(part.Gtin))
            return;

        var removed = part.Gtin;
        part.Gtin = null;
        db.LogActivityAt("part-gtin-removed", $"GS1 GTIN {removed} removed — reverted to internal code", ("Part", part.Id));
        await db.SaveChangesAsync(cancellationToken);

        await barcodes.RefreshPartBarcodeAsync(part.Id, cancellationToken);
    }
}
