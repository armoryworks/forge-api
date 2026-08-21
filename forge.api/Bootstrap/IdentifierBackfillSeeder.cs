using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Bootstrap;

/// <summary>
/// Idempotent boot backfill: gives every existing record an active row in the business-identifier
/// registry from its current number, so a rename can preserve history and old numbers resolve. Runs
/// on every boot (a fresh install has nothing to seed; a populated one is seeded once, then no-ops).
/// Covers every entity wired to the registry; nullable numbers (master-data + estimates) are skipped
/// until they carry a value.
/// </summary>
public interface IIdentifierBackfillSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class IdentifierBackfillSeeder(
    AppDbContext db,
    IBusinessIdentifierService identifiers,
    ILogger<IdentifierBackfillSeeder> logger) : IIdentifierBackfillSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seeded = 0;

        // NOTE: the "not already registered" check must be INLINED into each Where as a subquery
        // expression — EF Core cannot translate a helper-method call inside a LINQ predicate to SQL
        // (it throws "The LINQ expression ... could not be translated" at runtime against Postgres).
        seeded += await IssueAllAsync(BusinessEntityType.Part, await db.Parts.AsNoTracking()
            .Where(p => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Part && b.EntityId == p.Id && b.EffectiveTo == null))
            .Select(p => new Row { Id = p.Id, Number = p.PartNumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Customer, await db.Customers.AsNoTracking()
            .Where(c => c.CustomerNumber != null && !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Customer && b.EntityId == c.Id && b.EffectiveTo == null))
            .Select(c => new Row { Id = c.Id, Number = c.CustomerNumber! }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Vendor, await db.Vendors.AsNoTracking()
            .Where(v => v.VendorNumber != null && !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Vendor && b.EntityId == v.Id && b.EffectiveTo == null))
            .Select(v => new Row { Id = v.Id, Number = v.VendorNumber! }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Lead, await db.Leads.AsNoTracking()
            .Where(l => l.LeadNumber != null && !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Lead && b.EntityId == l.Id && b.EffectiveTo == null))
            .Select(l => new Row { Id = l.Id, Number = l.LeadNumber! }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.SalesOrder, await db.SalesOrders.AsNoTracking()
            .Where(s => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.SalesOrder && b.EntityId == s.Id && b.EffectiveTo == null))
            .Select(s => new Row { Id = s.Id, Number = s.OrderNumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Quote, await db.Quotes.AsNoTracking()
            .Where(q => q.QuoteNumber != null && !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Quote && b.EntityId == q.Id && b.EffectiveTo == null))
            .Select(q => new Row { Id = q.Id, Number = q.QuoteNumber! }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Invoice, await db.Invoices.AsNoTracking()
            .Where(i => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Invoice && b.EntityId == i.Id && b.EffectiveTo == null))
            .Select(i => new Row { Id = i.Id, Number = i.InvoiceNumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.PurchaseOrder, await db.PurchaseOrders.AsNoTracking()
            .Where(p => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.PurchaseOrder && b.EntityId == p.Id && b.EffectiveTo == null))
            .Select(p => new Row { Id = p.Id, Number = p.PONumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Job, await db.Jobs.AsNoTracking()
            .Where(j => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Job && b.EntityId == j.Id && b.EffectiveTo == null))
            .Select(j => new Row { Id = j.Id, Number = j.JobNumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Shipment, await db.Shipments.AsNoTracking()
            .Where(s => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Shipment && b.EntityId == s.Id && b.EffectiveTo == null))
            .Select(s => new Row { Id = s.Id, Number = s.ShipmentNumber }).ToListAsync(ct), ct);

        seeded += await IssueAllAsync(BusinessEntityType.Payment, await db.Payments.AsNoTracking()
            .Where(p => !db.BusinessIdentifiers.Any(b => b.EntityType == BusinessEntityType.Payment && b.EntityId == p.Id && b.EffectiveTo == null))
            .Select(p => new Row { Id = p.Id, Number = p.PaymentNumber }).ToListAsync(ct), ct);

        if (seeded > 0)
            logger.LogInformation("[IDENTIFIER-BACKFILL] Seeded {Count} identifier row(s).", seeded);
    }

    private async Task<int> IssueAllAsync(BusinessEntityType type, List<Row> rows, CancellationToken ct)
    {
        var n = 0;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Number)) continue;
            try
            {
                await identifiers.IssueAsync(type, r.Id, r.Number, ct);
                n++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[IDENTIFIER-BACKFILL] Skipped {Type} {Id} ({Number})", type, r.Id, r.Number);
            }
        }
        return n;
    }

    private sealed class Row
    {
        public int Id { get; init; }
        public string Number { get; init; } = string.Empty;
    }
}
