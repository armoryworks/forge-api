using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Preview;

/// <summary>
/// Loads a minimal, non-sensitive preview for a linked record. Returns null
/// for unknown/unsupported types (the controller maps that to 404). Read-only:
/// AsNoTracking, no ActivityLog. Surfaces ONLY list-row-visible basics —
/// identity, status, a date, a headline figure, and jump links — never costs,
/// margins, tax ids, credit limits, bank details, or scan/security tokens.
/// </summary>
public record GetEntityPreviewQuery(string Type, int Id) : IRequest<EntityPreviewModel?>;

public class GetEntityPreviewHandler(AppDbContext db)
    : IRequestHandler<GetEntityPreviewQuery, EntityPreviewModel?>
{
    public async Task<EntityPreviewModel?> Handle(GetEntityPreviewQuery request, CancellationToken ct)
    {
        var id = request.Id;
        return request.Type?.Trim().ToLowerInvariant() switch
        {
            "customer" => await Customer(id, ct),
            "vendor" => await Vendor(id, ct),
            "job" => await Job(id, ct),
            "sales-order" => await SalesOrder(id, ct),
            "purchase-order" => await PurchaseOrder(id, ct),
            "invoice" => await Invoice(id, ct),
            "quote" => await Quote(id, ct),
            "shipment" => await Shipment(id, ct),
            "part" => await Part(id, ct),
            _ => null,
        };
    }

    private async Task<EntityPreviewModel?> Customer(int id, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", Active(c.IsActive));

        return new EntityPreviewModel("customer", c.Id, Display(c.CompanyName, c.Name),
            c.CustomerNumber, fields, []);
    }

    private async Task<EntityPreviewModel?> Vendor(int id, CancellationToken ct)
    {
        var v = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", Active(v.IsActive));
        Add(fields, "Contact", v.ContactName);

        return new EntityPreviewModel("vendor", v.Id, v.CompanyName, v.VendorNumber, fields, []);
    }

    private async Task<EntityPreviewModel?> Job(int id, CancellationToken ct)
    {
        var j = await db.Jobs.AsNoTracking()
            .Include(x => x.CurrentStage)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Stage", j.CurrentStage?.Name);
        Add(fields, "Priority", j.Priority.ToString());
        Add(fields, "Due", Date(j.DueDate));

        var links = new List<PreviewLink>();
        Link(links, "customer", j.CustomerId, "Customer");
        Link(links, "part", j.PartId, "Part");

        return new EntityPreviewModel("job", j.Id, j.JobNumber, j.Title, fields, links);
    }

    private async Task<EntityPreviewModel?> SalesOrder(int id, CancellationToken ct)
    {
        var so = await db.SalesOrders.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (so is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", so.Status.ToString());
        Add(fields, "Requested delivery", Date(so.RequestedDeliveryDate));
        Add(fields, "Total", Money(so.Total));

        var links = new List<PreviewLink>();
        Link(links, "customer", so.CustomerId, "Customer");
        Link(links, "quote", so.QuoteId, "Quote");

        return new EntityPreviewModel("sales-order", so.Id, so.OrderNumber,
            Display(so.Customer?.CompanyName, so.Customer?.Name), fields, links);
    }

    private async Task<EntityPreviewModel?> PurchaseOrder(int id, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking()
            .Include(x => x.Vendor)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (po is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", po.Status.ToString());
        Add(fields, "Expected delivery", Date(po.ExpectedDeliveryDate));

        var links = new List<PreviewLink>();
        Link(links, "vendor", po.VendorId, "Vendor");
        Link(links, "job", po.JobId, "Job");

        return new EntityPreviewModel("purchase-order", po.Id, po.PONumber,
            po.Vendor?.CompanyName, fields, links);
    }

    private async Task<EntityPreviewModel?> Invoice(int id, CancellationToken ct)
    {
        var inv = await db.Invoices.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inv is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", inv.Status.ToString());
        Add(fields, "Invoice date", Date(inv.InvoiceDate));
        Add(fields, "Due", Date(inv.DueDate));
        Add(fields, "Total", Money(inv.Total));

        var links = new List<PreviewLink>();
        Link(links, "customer", inv.CustomerId, "Customer");
        Link(links, "sales-order", inv.SalesOrderId, "Sales order");
        Link(links, "shipment", inv.ShipmentId, "Shipment");

        return new EntityPreviewModel("invoice", inv.Id, inv.InvoiceNumber,
            Display(inv.Customer?.CompanyName, inv.Customer?.Name), fields, links);
    }

    private async Task<EntityPreviewModel?> Quote(int id, CancellationToken ct)
    {
        var q = await db.Quotes.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Type", q.Type.ToString());
        Add(fields, "Status", q.Status.ToString());
        Add(fields, "Expires", Date(q.ExpirationDate));
        Add(fields, "Total", Money(q.EstimatedAmount ?? q.Total));

        var links = new List<PreviewLink>();
        Link(links, "customer", q.CustomerId, "Customer");

        var title = q.QuoteNumber ?? q.Title ?? $"Quote #{q.Id}";
        return new EntityPreviewModel("quote", q.Id, title,
            Display(q.Customer?.CompanyName, q.Customer?.Name), fields, links);
    }

    private async Task<EntityPreviewModel?> Shipment(int id, CancellationToken ct)
    {
        var s = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", s.Status.ToString());
        Add(fields, "Shipped", Date(s.ShippedDate));
        Add(fields, "Est. delivery", Date(s.EstimatedDeliveryDate));

        var links = new List<PreviewLink>();
        Link(links, "sales-order", s.SalesOrderId, "Sales order");

        return new EntityPreviewModel("shipment", s.Id, s.ShipmentNumber, s.TrackingNumber, fields, links);
    }

    private async Task<EntityPreviewModel?> Part(int id, CancellationToken ct)
    {
        var p = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        var fields = new List<PreviewField>();
        Add(fields, "Status", p.Status.ToString());
        Add(fields, "Revision", p.Revision);

        var links = new List<PreviewLink>();
        Link(links, "vendor", p.PreferredVendorId, "Preferred vendor");

        return new EntityPreviewModel("part", p.Id, p.PartNumber, p.Name, fields, links);
    }

    private static string Active(bool isActive) => isActive ? "Active" : "Inactive";

    private static string Display(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred
        : !string.IsNullOrWhiteSpace(fallback) ? fallback
        : string.Empty;

    private static string? Date(DateTimeOffset? d) => d?.ToString("yyyy-MM-dd");

    private static string Money(decimal amount) => amount.ToString("N2");

    private static void Add(List<PreviewField> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) fields.Add(new PreviewField(label, value));
    }

    private static void Link(List<PreviewLink> links, string type, int? id, string label)
    {
        if (id is > 0) links.Add(new PreviewLink(type, id.Value, label));
    }
}
