using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Soft-delete a VendorPart row. Stamps DeletedAt; the global
/// query filter on BaseEntity hides it from subsequent reads. Price tiers
/// remain in the table but become orphaned (cascade-delete on the FK is set
/// at the DB level — but soft delete here doesn't trigger cascade).
/// </summary>
public record DeleteVendorPartCommand(int Id) : IRequest;

public class DeleteVendorPartHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteVendorPartCommand>
{
    public async Task Handle(DeleteVendorPartCommand request, CancellationToken ct)
    {
        var vp = await db.VendorParts
            .Include(x => x.Vendor)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"VendorPart {request.Id} not found");

        vp.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "vendor-source-removed",
            $"Removed vendor source: {vp.Vendor?.CompanyName ?? "(unknown)"}",
            ("Part", vp.PartId),
            ("Vendor", vp.VendorId));

        await db.SaveChangesAsync(ct);
    }
}
