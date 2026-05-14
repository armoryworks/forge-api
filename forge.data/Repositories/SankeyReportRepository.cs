using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SankeyReportRepository(AppDbContext db) : ISankeyReportRepository
{
    /// <summary>
    /// Quote-to-Cash: Estimate → Quote → Sales Order → Invoice → Payment
    /// Shows how many documents flow through each stage of the pipeline.
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetQuoteToCashFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var flows = new List<SankeyFlowItem>();

        var quotesQuery = db.Quotes.AsNoTracking();
        if (start.HasValue) quotesQuery = quotesQuery.Where(q => q.CreatedAt >= start.Value);
        if (end.HasValue) quotesQuery = quotesQuery.Where(q => q.CreatedAt <= end.Value);

        // Estimates → Quotes (converted)
        var estimateToQuote = await quotesQuery
            .Where(q => q.Type == QuoteType.Estimate && q.Status == QuoteStatus.ConvertedToQuote)
            .CountAsync(ct);
        if (estimateToQuote > 0)
            flows.Add(new SankeyFlowItem("Estimates", "Quotes", estimateToQuote));

        // Estimates that didn't convert
        var estimatesDead = await quotesQuery
            .Where(q => q.Type == QuoteType.Estimate &&
                        (q.Status == QuoteStatus.Declined || q.Status == QuoteStatus.Expired))
            .CountAsync(ct);
        if (estimatesDead > 0)
            flows.Add(new SankeyFlowItem("Estimates", "Lost", estimatesDead));

        // Quotes → Accepted
        var quotesAccepted = await quotesQuery
            .Where(q => q.Type == QuoteType.Quote &&
                        (q.Status == QuoteStatus.Accepted || q.Status == QuoteStatus.ConvertedToOrder))
            .CountAsync(ct);
        if (quotesAccepted > 0)
            flows.Add(new SankeyFlowItem("Quotes", "Sales Orders", quotesAccepted));

        // Quotes → Declined/Expired
        var quotesLost = await quotesQuery
            .Where(q => q.Type == QuoteType.Quote &&
                        (q.Status == QuoteStatus.Declined || q.Status == QuoteStatus.Expired))
            .CountAsync(ct);
        if (quotesLost > 0)
            flows.Add(new SankeyFlowItem("Quotes", "Lost", quotesLost));

        // Sales Orders → Invoices
        var invoicesQuery = db.Invoices.AsNoTracking();
        if (start.HasValue) invoicesQuery = invoicesQuery.Where(i => i.CreatedAt >= start.Value);
        if (end.HasValue) invoicesQuery = invoicesQuery.Where(i => i.CreatedAt <= end.Value);

        var soToInvoice = await invoicesQuery.Where(i => i.SalesOrderId != null).CountAsync(ct);
        if (soToInvoice > 0)
            flows.Add(new SankeyFlowItem("Sales Orders", "Invoices", soToInvoice));

        // Invoices → Paid
        var invoicesPaid = await invoicesQuery
            .Where(i => i.Status == InvoiceStatus.Paid)
            .CountAsync(ct);
        if (invoicesPaid > 0)
            flows.Add(new SankeyFlowItem("Invoices", "Payments Received", invoicesPaid));

        // Invoices → Outstanding
        var invoicesOutstanding = await invoicesQuery
            .Where(i => i.Status == InvoiceStatus.Sent ||
                        i.Status == InvoiceStatus.Overdue ||
                        i.Status == InvoiceStatus.PartiallyPaid)
            .CountAsync(ct);
        if (invoicesOutstanding > 0)
            flows.Add(new SankeyFlowItem("Invoices", "Outstanding", invoicesOutstanding));

        return flows;
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
        var bomEntries = await db.BOMEntries.AsNoTracking()
            .Select(b => new { b.ParentPartId, b.ChildPartId, b.Quantity })
            .ToListAsync(ct);

        var partIds = bomEntries.SelectMany(b => new[] { b.ParentPartId, b.ChildPartId }).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber ?? $"Part #{p.Id}", ct);

        return bomEntries
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
                g.Sum(e => e.Amount)))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .ToList();
    }

    /// <summary>
    /// Vendor Supply Chain: Vendor → Part (via PO lines)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetVendorSupplyChainFlowAsync(CancellationToken ct)
    {
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Include(l => l.PurchaseOrder)
            .Where(l => l.PurchaseOrder != null)
            .Select(l => new
            {
                VendorId = l.PurchaseOrder!.VendorId,
                l.PartId,
                l.OrderedQuantity,
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
                g.Sum(l => l.OrderedQuantity)))
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
    /// Inventory Location Flow: Storage Location → Part (quantity in bins)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetInventoryLocationFlowAsync(CancellationToken ct)
    {
        var binContents = await db.BinContents.AsNoTracking()
            .Where(b => b.Quantity > 0)
            .Select(b => new { b.LocationId, b.EntityId, b.Quantity })
            .ToListAsync(ct);

        var locationIds = binContents.Select(b => b.LocationId).Distinct().ToList();
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        var partIds = binContents.Select(b => b.EntityId).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber ?? $"Part #{p.Id}", ct);

        return binContents
            .Where(b => locations.ContainsKey(b.LocationId) && parts.ContainsKey(b.EntityId))
            .GroupBy(b => new { b.LocationId, b.EntityId })
            .Select(g => new SankeyFlowItem(
                locations[g.Key.LocationId],
                parts[g.Key.EntityId],
                g.Sum(b => b.Quantity)))
            .Where(f => f.Flow > 0)
            .OrderByDescending(f => f.Flow)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Customer Revenue Breakdown: Customer → Revenue Category (by track type of linked jobs via SO → Invoice)
    /// </summary>
    public async Task<List<SankeyFlowItem>> GetCustomerRevenueFlowAsync(
        DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Paid ||
                        i.Status == InvoiceStatus.Sent ||
                        i.Status == InvoiceStatus.PartiallyPaid);
        if (start.HasValue) query = query.Where(i => i.CreatedAt >= start.Value);
        if (end.HasValue) query = query.Where(i => i.CreatedAt <= end.Value);

        var invoices = await query
            .Select(i => new { i.CustomerId, i.Total })
            .ToListAsync(ct);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name ?? $"Customer #{c.Id}", ct);

        // Group by customer, bucket by revenue size
        return invoices
            .Where(i => customers.ContainsKey(i.CustomerId))
            .GroupBy(i => i.CustomerId)
            .Select(g =>
            {
                var total = g.Sum(i => i.Total);
                var bucket = total >= 100000 ? "Large (>$100K)"
                           : total >= 10000 ? "Medium ($10K-$100K)"
                           : "Small (<$10K)";
                return new SankeyFlowItem(customers[g.Key], bucket, total);
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
