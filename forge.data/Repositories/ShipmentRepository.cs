using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ShipmentRepository(AppDbContext db) : IShipmentRepository
{
    public async Task<List<ShipmentListItemModel>> GetAllAsync(
        int? salesOrderId, ShipmentStatus? status, CancellationToken ct)
    {
        var query = db.Shipments
            .Include(s => s.SalesOrder)
                .ThenInclude(so => so.Customer)
            .AsQueryable();

        if (salesOrderId.HasValue)
            query = query.Where(s => s.SalesOrderId == salesOrderId.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShipmentListItemModel(
                s.Id,
                s.ShipmentNumber,
                s.SalesOrderId,
                s.SalesOrder.OrderNumber,
                s.SalesOrder.Customer.Name,
                s.Status.ToString(),
                s.Carrier,
                s.TrackingNumber,
                s.ShippedDate,
                s.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Shipment?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Shipments.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Shipment?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.Shipments
            .Include(s => s.SalesOrder)
                .ThenInclude(so => so.Customer)
            .Include(s => s.Lines)
                .ThenInclude(l => l.SalesOrderLine)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .Include(s => s.Invoice)
            // ShippingAddress drives rate-shopping + label creation (they read the navigation, not just
            // the FK) — without this Include those handlers see a null address and wrongly 409.
            .Include(s => s.ShippingAddress)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<string> GenerateNextShipmentNumberAsync(CancellationToken ct)
    {
        var last = await db.Shipments
            .IgnoreQueryFilters()
            .OrderByDescending(s => s.Id)
            .Select(s => s.ShipmentNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("SH-") && int.TryParse(last[3..], out var lastNum))
            return $"SH-{lastNum + 1:D5}";

        return "SH-00001";
    }

    public async Task AddAsync(Shipment shipment, CancellationToken ct)
    {
        await db.Shipments.AddAsync(shipment, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
