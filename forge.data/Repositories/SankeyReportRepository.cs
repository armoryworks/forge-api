using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Data.Repositories;

public class SankeyReportRepository(AppDbContext db) : ISankeyReportRepository
{
    /// <summary>
    /// Quote-to-Cash: Estimate → Quote → Sales Order → Invoiced → Paid.
    /// Counts quotes up to the order, then counts sales orders, so every node after it balances.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetQuoteToCashFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var flows = new List<SankeyFlowItem>();

        var quotesQuery = db.Quotes.AsNoTracking();
        if (start.HasValue) quotesQuery = quotesQuery.Where(q => q.CreatedAt >= start.Value);
        if (end.HasValue) quotesQuery = quotesQuery.Where(q => q.CreatedAt <= end.Value);

        var estimateToQuote = await quotesQuery
            .Where(q => q.Type == Core.Enums.QuoteType.Estimate && q.Status == Core.Enums.QuoteStatus.ConvertedToQuote)
            .CountAsync(ct);
        if (estimateToQuote > 0)
            flows.Add(new SankeyFlowItem("Estimates", "Quotes", estimateToQuote, SankeyFlowItem.CountUnit));

        var estimatesDead = await quotesQuery
            .Where(q => q.Type == Core.Enums.QuoteType.Estimate &&
                        (q.Status == Core.Enums.QuoteStatus.Declined || q.Status == Core.Enums.QuoteStatus.Expired))
            .CountAsync(ct);
        if (estimatesDead > 0)
            flows.Add(new SankeyFlowItem("Estimates", "Lost", estimatesDead, SankeyFlowItem.CountUnit));

        var quotesAwaitingOrder = await quotesQuery
            .Where(q => q.Type == Core.Enums.QuoteType.Quote && q.Status == Core.Enums.QuoteStatus.Accepted)
            .CountAsync(ct);
        if (quotesAwaitingOrder > 0)
            flows.Add(new SankeyFlowItem("Quotes", "Accepted, No Order Yet", quotesAwaitingOrder, SankeyFlowItem.CountUnit));

        var quotesLost = await quotesQuery
            .Where(q => q.Type == Core.Enums.QuoteType.Quote &&
                        (q.Status == Core.Enums.QuoteStatus.Declined || q.Status == Core.Enums.QuoteStatus.Expired))
            .CountAsync(ct);
        if (quotesLost > 0)
            flows.Add(new SankeyFlowItem("Quotes", "Lost", quotesLost, SankeyFlowItem.CountUnit));

        var ordersQuery = db.SalesOrders.AsNoTracking()
            .Where(so => so.Status != Core.Enums.SalesOrderStatus.Cancelled);
        if (start.HasValue) ordersQuery = ordersQuery.Where(so => so.CreatedAt >= start.Value);
        if (end.HasValue) ordersQuery = ordersQuery.Where(so => so.CreatedAt <= end.Value);

        var orders = await ordersQuery
            .Select(so => new
            {
                FromQuote = so.QuoteId != null,
                InvoiceStatuses = so.Invoices
                    .Where(i => i.Status != Core.Enums.InvoiceStatus.Draft && i.Status != Core.Enums.InvoiceStatus.Voided)
                    .Select(i => i.Status)
                    .ToList(),
            })
            .ToListAsync(ct);

        AddCount(flows, "Quotes", "Sales Orders", orders.Count(o => o.FromQuote));
        AddCount(flows, "Direct Orders", "Sales Orders", orders.Count(o => !o.FromQuote));
        AddCount(flows, "Sales Orders", "Not Yet Invoiced", orders.Count(o => o.InvoiceStatuses.Count == 0));
        AddCount(flows, "Sales Orders", "Invoiced", orders.Count(o => o.InvoiceStatuses.Count > 0));
        AddCount(flows, "Invoiced", "Paid in Full",
            orders.Count(o => o.InvoiceStatuses.Count > 0 && o.InvoiceStatuses.All(st => st == Core.Enums.InvoiceStatus.Paid)));
        AddCount(flows, "Invoiced", "Balance Outstanding",
            orders.Count(o => o.InvoiceStatuses.Any(st => st != Core.Enums.InvoiceStatus.Paid)));

        return flows;
    }

    private static void AddCount(List<SankeyFlowItem> flows, string from, string to, int count)
    {
        if (count > 0)
            flows.Add(new SankeyFlowItem(from, to, count, SankeyFlowItem.CountUnit));
    }

    /// <summary>
    /// Job Stage Flow: shows how many jobs are at each stage, grouped by track type.
    /// TrackType → Stage1 → Stage2 → ... (based on stage sort order)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetJobStageFlowAsync(CancellationToken ct)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => !j.IsArchived)
            .Select(j => new
            {
                j.TrackTypeId,
                j.CurrentStageId,
            })
            .ToListAsync(ct);

        var trackTypes = await db.TrackTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var stages = await db.JobStages.AsNoTracking()
            .OrderBy(s => s.TrackTypeId).ThenBy(s => s.SortOrder)
            .ToListAsync(ct);

        var stageMap = stages.ToDictionary(s => s.Id, s => s.Name);

        // Group: TrackType → CurrentStage, count
        return jobs
            .GroupBy(j => new { j.TrackTypeId, j.CurrentStageId })
            .Where(g => trackTypes.ContainsKey(g.Key.TrackTypeId) && stageMap.ContainsKey(g.Key.CurrentStageId))
            .Select(g => new SankeyFlowItem(
                trackTypes[g.Key.TrackTypeId],
                stageMap[g.Key.CurrentStageId],
                g.Count()))
            .Where(f => f.Flow > 0)
            .ToList();
    }

    /// <summary>
    /// Material to Product: BOM hierarchy showing raw material → sub-assembly → end product flows.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetMaterialToProductFlowAsync(CancellationToken ct)
    {
        var bomLines = await db.BOMLines.AsNoTracking()
            .Select(b => new { b.ParentPartId, b.ChildPartId, b.Quantity })
            .ToListAsync(ct);

        var partIds = bomLines.SelectMany(b => new[] { b.ParentPartId, b.ChildPartId }).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber ?? $"Part #{p.Id}", ct);

        return bomLines
            .Where(b => parts.ContainsKey(b.ChildPartId) && parts.ContainsKey(b.ParentPartId))
            .GroupBy(b => new { b.ChildPartId, b.ParentPartId })
            .Select(g => new SankeyFlowItem(
                parts[g.Key.ChildPartId],
                parts[g.Key.ParentPartId],
                g.Sum(b => b.Quantity)))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Worker → Orders: shows which workers are assigned to which jobs, grouped by track type.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetWorkerOrdersFlowAsync(CancellationToken ct)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => !j.IsArchived && j.AssigneeId != null)
            .Select(j => new { j.AssigneeId, j.TrackTypeId })
            .ToListAsync(ct);

        var userIds = jobs.Select(j => j.AssigneeId!.Value).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}".Trim(' ', ','), ct);

        var trackTypes = await db.TrackTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return jobs
            .Where(j => j.AssigneeId.HasValue && users.ContainsKey(j.AssigneeId.Value) && trackTypes.ContainsKey(j.TrackTypeId))
            .GroupBy(j => new { j.AssigneeId, j.TrackTypeId })
            .Select(g => new SankeyFlowItem(
                users[g.Key.AssigneeId!.Value],
                trackTypes[g.Key.TrackTypeId],
                g.Count()))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Expense Flow: Category → Status (Pending/Approved/Rejected/Reimbursed)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetExpenseFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var query = db.Expenses.AsNoTracking();
        if (start.HasValue) query = query.Where(e => e.ExpenseDate >= start.Value);
        if (end.HasValue) query = query.Where(e => e.ExpenseDate <= end.Value);

        var expenses = await query
            .Select(e => new { e.Category, e.Status, e.Amount })
            .ToListAsync(ct);

        return expenses
            .GroupBy(e => new { e.Category, Status = e.Status.ToString() })
            .Select(g => new SankeyFlowItem(
                string.IsNullOrWhiteSpace(g.Key.Category) ? "Uncategorized" : g.Key.Category,
                g.Key.Status,
                g.Sum(e => e.Amount),
                SankeyFlowItem.CurrencyUnit))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .ToList();
    }

    /// <summary>
    /// Vendor Supply Chain: Vendor → Part (via PO lines), weighted by ordered value in base currency (the PO's locked
    /// FX rate) so parts bought in different units and currencies compare.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetVendorSupplyChainFlowAsync(CancellationToken ct)
    {
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Include(l => l.PurchaseOrder)
            // Part-less lines (services / described material) have no part node to flow to.
            .Where(l => l.PurchaseOrder != null && l.PartId != null)
            .Select(l => new
            {
                VendorId = l.PurchaseOrder!.VendorId,
                PartId = l.PartId!.Value,
                Value = l.OrderedQuantity * l.UnitPrice * (l.PurchaseOrder.FxRate ?? 1m),
            })
            .ToListAsync(ct);

        var vendorIds = poLines.Select(l => l.VendorId).Distinct().ToList();
        var vendors = await db.Vendors.AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName ?? $"Vendor #{v.Id}", ct);

        var partIds = poLines.Select(l => l.PartId).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber ?? $"Part #{p.Id}", ct);

        return poLines
            .Where(l => vendors.ContainsKey(l.VendorId) && parts.ContainsKey(l.PartId))
            .GroupBy(l => new { l.VendorId, l.PartId })
            .Select(g => new SankeyFlowItem(
                vendors[g.Key.VendorId],
                parts[g.Key.PartId],
                Math.Round(g.Sum(l => l.Value), 2, MidpointRounding.AwayFromZero),
                SankeyFlowItem.CurrencyUnit))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Quality/Rejection Flow: Part → Inspection Result (Pass/Fail)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetQualityRejectionFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var query = db.QcInspections.AsNoTracking()
            .Where(i => i.Status != null);
        if (start.HasValue) query = query.Where(i => i.CreatedAt >= start.Value);
        if (end.HasValue) query = query.Where(i => i.CreatedAt <= end.Value);

        var inspections = await query
            .Select(i => new
            {
                i.JobId,
                i.Status,
            })
            .ToListAsync(ct);

        // Get job → part mapping for inspections that have a job
        var jobIds = inspections.Where(i => i.JobId.HasValue).Select(i => i.JobId!.Value).Distinct().ToList();
        var jobParts = jobIds.Count > 0
            ? await db.Jobs.AsNoTracking()
                .Where(j => jobIds.Contains(j.Id) && j.PartId != null)
                .ToDictionaryAsync(j => j.Id, j => j.PartId!.Value, ct)
            : new Dictionary<int, int>();

        var partIds = jobParts.Values.Distinct().ToList();
        var parts = partIds.Count > 0
            ? await db.Parts.AsNoTracking()
                .Where(p => partIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.PartNumber ?? $"Part #{p.Id}", ct)
            : new Dictionary<int, string>();

        var flows = new List<SankeyFlowItem>();

        foreach (var group in inspections.GroupBy(i => new { i.JobId, i.Status }))
        {
            var partName = "Unknown Part";
            if (group.Key.JobId.HasValue && jobParts.TryGetValue(group.Key.JobId.Value, out var partId) && parts.TryGetValue(partId, out var pn))
                partName = pn;

            var status = group.Key.Status ?? "Unknown";
            flows.Add(new SankeyFlowItem(partName, status, group.Count()));
        }

        return flows
            .GroupBy(f => new { f.From, f.To })
            .Select(g => new SankeyFlowItem(g.Key.From, g.Key.To, g.Sum(f => f.Flow)))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Inventory Location Flow: Storage Location → Part, weighted by on-hand value in base currency (quantity in the
    /// part's stock unit × its average cost, falling back to its standard cost) so parts kept in different units add up.
    /// Bin quantities with no conversion to the stock unit, and parts with no cost, carry no value and are left out.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetInventoryLocationFlowAsync(CancellationToken ct)
    {
        var binContents = await db.BinContents.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.EntityType == "part")
            .Select(b => new { b.LocationId, b.EntityId, b.Quantity, b.UomId })
            .ToListAsync(ct);

        var locationIds = binContents.Select(b => b.LocationId).Distinct().ToList();
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        var partIds = binContents.Select(b => b.EntityId).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.PartNumber,
                p.StockUomId,
                StandardCost = p.ManualCostOverride
                    ?? (p.CurrentCostCalculation != null ? p.CurrentCostCalculation.ResultAmount : (decimal?)null),
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(ct);
        var averageCosts = bookId is int book
            ? await db.InventoryValuations.AsNoTracking()
                .Where(v => v.BookId == book && partIds.Contains(v.PartId) && v.AverageUnitCost > 0)
                .ToDictionaryAsync(v => v.PartId, v => v.AverageUnitCost, ct)
            : new Dictionary<int, decimal>();

        var uom = new UomService(db);
        var stockFactors = new Dictionary<(int PartId, int UomId), decimal?>();
        foreach (var pair in binContents
                     .Where(b => parts.ContainsKey(b.EntityId))
                     .Select(b => (PartId: b.EntityId, FromUomId: b.UomId, ToUomId: parts[b.EntityId].StockUomId))
                     .Where(x => x.FromUomId is int from && x.ToUomId is int to && from != to)
                     .Select(x => (x.PartId, UomId: x.FromUomId!.Value, StockUomId: x.ToUomId!.Value))
                     .Distinct())
        {
            stockFactors[(pair.PartId, pair.UomId)] =
                await uom.TryConvertAsync(1m, pair.UomId, pair.StockUomId, pair.PartId, ct);
        }

        return binContents
            .Where(b => locations.ContainsKey(b.LocationId) && parts.ContainsKey(b.EntityId))
            .Select(b =>
            {
                var part = parts[b.EntityId];
                var unitCost = averageCosts.TryGetValue(part.Id, out var average) ? average : part.StandardCost ?? 0m;
                var factor = b.UomId is int from && part.StockUomId is int to && from != to
                    ? stockFactors.GetValueOrDefault((part.Id, from))
                    : 1m;
                var value = factor is decimal f ? b.Quantity * f * unitCost : 0m;
                return new { b.LocationId, Part = part.PartNumber ?? $"Part #{part.Id}", Value = value };
            })
            .GroupBy(b => new { b.LocationId, b.Part })
            .Select(g => new SankeyFlowItem(
                locations[g.Key.LocationId],
                g.Key.Part,
                Math.Round(g.Sum(b => b.Value), 2, MidpointRounding.AwayFromZero),
                SankeyFlowItem.CurrencyUnit))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Customer Revenue Breakdown: Customer → size bucket, from invoiced revenue (excluding sales tax, draft and voided
    /// invoices) converted to base currency at each invoice's FX rate.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetCustomerRevenueFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking()
            .Where(i => i.Status != Core.Enums.InvoiceStatus.Draft &&
                        i.Status != Core.Enums.InvoiceStatus.Voided);
        if (start.HasValue) query = query.Where(i => i.InvoiceDate >= start.Value);
        if (end.HasValue) query = query.Where(i => i.InvoiceDate <= end.Value);

        var invoices = (await query
                .Include(i => i.Lines)
                .ToListAsync(ct))
            .Select(i => new { i.CustomerId, Total = Math.Round(i.Subtotal * i.FxRate, 2, MidpointRounding.AwayFromZero) })
            .ToList();

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name ?? $"Customer #{c.Id}", ct);

        return invoices
            .Where(i => customers.ContainsKey(i.CustomerId))
            .GroupBy(i => i.CustomerId)
            .Select(g =>
            {
                var total = g.Sum(i => i.Total);
                var bucket = total >= 100000 ? "Large (>$100K)"
                           : total >= 10000 ? "Medium ($10K-$100K)"
                           : "Small (<$10K)";
                return new SankeyFlowItem(customers[g.Key], bucket, total, SankeyFlowItem.CurrencyUnit);
            })
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(30)
            .ToList();
    }

    /// <summary>
    /// Training Completion: Training Module → Completion Status (Not Started / In Progress / Completed)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetTrainingCompletionFlowAsync(CancellationToken ct)
    {
        var modules = await db.TrainingModules.AsNoTracking()
            .Where(m => m.IsPublished)
            .Select(m => new { m.Id, m.Title })
            .ToListAsync(ct);

        var progress = await db.TrainingProgress.AsNoTracking()
            .Select(p => new { p.ModuleId, p.Status })
            .ToListAsync(ct);

        var moduleMap = modules.ToDictionary(m => m.Id, m => m.Title);
        var progressByModule = progress
            .Where(p => moduleMap.ContainsKey(p.ModuleId))
            .GroupBy(p => new { p.ModuleId, p.Status });

        return progressByModule
            .Select(g => new SankeyFlowItem(
                moduleMap[g.Key.ModuleId],
                g.Key.Status.ToString(),
                g.Count()))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }
}
