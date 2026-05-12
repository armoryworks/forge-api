using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Concurrency;

/// <summary>
/// Phase 3 / WU-11 / TODO E1 — helpers for reading the optimistic-locking
/// Version of a transactional entity by Id. Uses a per-type switch so EF Core
/// can translate the query against the typed DbSet (a generic IQueryable
/// over IConcurrencyVersioned would not translate).
/// </summary>
internal static class ConcurrencyLookup
{
    public static async Task<uint?> LoadVersionAsync(AppDbContext db, Type entityType, int id, CancellationToken ct)
    {
        return entityType.Name switch
        {
            nameof(Job) => await db.Jobs.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(Invoice) => await db.Invoices.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(PurchaseOrder) => await db.PurchaseOrders.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(Payment) => await db.Payments.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(Shipment) => await db.Shipments.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(SalesOrder) => await db.SalesOrders.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            nameof(Quote) => await db.Quotes.AsNoTracking().Where(e => e.Id == id).Select(e => (uint?)e.Version).FirstOrDefaultAsync(ct),
            _ => null
        };
    }
}
