using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ReportRepository(AppDbContext db) : IReportRepository
{
    public async Task<List<JobsByStageReportItem>> GetJobsByStageAsync(int? trackTypeId, CancellationToken ct)
    {
        var jobQuery = db.Jobs.Where(j => !j.IsArchived);

        if (trackTypeId.HasValue)
            jobQuery = jobQuery.Where(j => j.TrackTypeId == trackTypeId.Value);

        var rows = await jobQuery
            .Join(db.JobStages, j => j.CurrentStageId, s => s.Id,
                (j, s) => new { s.Name, s.Color, s.SortOrder })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.Name, x.Color, x.SortOrder })
            .Select(g => new JobsByStageReportItem(g.Key.Name, g.Key.Color, g.Count()))
            .OrderBy(r => r.StageName)
            .ToList();
    }

    public async Task<List<OverdueJobReportItem>> GetOverdueJobsAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;

        var overdueJobs = await db.Jobs
            .Where(j => !j.IsArchived && j.DueDate.HasValue && j.DueDate.Value < today)
            .OrderBy(j => j.DueDate)
            .Select(j => new { j.Id, j.JobNumber, j.Title, j.DueDate, j.AssigneeId })
            .ToListAsync(ct);

        var assigneeIds = overdueJobs.Where(j => j.AssigneeId.HasValue).Select(j => j.AssigneeId!.Value).Distinct().ToList();
        var users = assigneeIds.Count > 0
            ? await db.Users.Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct)
            : new Dictionary<int, string>();

        return overdueJobs.Select(j => new OverdueJobReportItem(
            j.Id,
            j.JobNumber,
            j.Title,
            j.DueDate!.Value,
            (int)(today - j.DueDate.Value).TotalDays,
            j.AssigneeId.HasValue ? users.GetValueOrDefault(j.AssigneeId.Value) : null)).ToList();
    }

    public async Task<List<TimeByUserReportItem>> GetTimeByUserAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startDate = DateOnly.FromDateTime(start.UtcDateTime.Date);
        var endDate = DateOnly.FromDateTime(end.UtcDateTime.Date);

        var entries = await db.TimeEntries
            .Where(t => t.Date >= startDate && t.Date <= endDate)
            .Select(t => new { t.UserId, t.DurationMinutes })
            .ToListAsync(ct);

        return entries
            .GroupBy(t => t.UserId)
            .Select(g => new TimeByUserReportItem(
                g.Key,
                "", // populated in handler with user lookup
                Math.Round((decimal)g.Sum(t => t.DurationMinutes) / 60, 1)))
            .OrderByDescending(r => r.TotalHours)
            .ToList();
    }

    public async Task<List<ExpenseSummaryReportItem>> GetExpenseSummaryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var expenses = await db.Expenses
            .Where(e => e.ExpenseDate >= startUtc && e.ExpenseDate <= endUtc)
            .Select(e => new { e.Category, e.Amount })
            .ToListAsync(ct);

        return expenses
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseSummaryReportItem(
                g.Key,
                g.Sum(e => e.Amount),
                g.Count()))
            .OrderByDescending(r => r.TotalAmount)
            .ToList();
    }

    public async Task<List<LeadPipelineReportItem>> GetLeadPipelineAsync(CancellationToken ct)
    {
        var leads = await db.Leads
            .Select(l => l.Status)
            .ToListAsync(ct);

        return leads
            .GroupBy(s => s)
            .Select(g => new LeadPipelineReportItem(g.Key.ToString(), g.Count()))
            .ToList();
    }

    public async Task<List<JobCompletionTrendItem>> GetJobCompletionTrendAsync(int months, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-months);

        var createdDates = await db.Jobs
            .Where(j => j.CreatedAt >= cutoff)
            .Select(j => j.CreatedAt)
            .ToListAsync(ct);

        var created = createdDates
            .GroupBy(d => new { d.Year, d.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToList();

        var completedDates = await db.Jobs
            .Where(j => j.CompletedDate.HasValue && j.CompletedDate.Value >= cutoff)
            .Select(j => j.CompletedDate!.Value)
            .ToListAsync(ct);

        var completed = completedDates
            .GroupBy(d => new { d.Year, d.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToList();

        var result = new List<JobCompletionTrendItem>();
        for (var i = months - 1; i >= 0; i--)
        {
            var d = DateTimeOffset.UtcNow.AddMonths(-i);
            var monthLabel = d.ToString("MMM yyyy");
            var createdCount = created.FirstOrDefault(c => c.Year == d.Year && c.Month == d.Month)?.Count ?? 0;
            var completedCount = completed.FirstOrDefault(c => c.Year == d.Year && c.Month == d.Month)?.Count ?? 0;
            result.Add(new JobCompletionTrendItem(monthLabel, createdCount, completedCount));
        }

        return result;
    }

    public async Task<OnTimeDeliveryReportItem> GetOnTimeDeliveryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var completedJobs = await db.Jobs
            .Where(j => j.CompletedDate.HasValue
                && j.CompletedDate.Value >= startUtc
                && j.CompletedDate.Value <= endUtc
                && j.DueDate.HasValue)
            .Select(j => new { j.DueDate, j.CompletedDate })
            .ToListAsync(ct);

        var total = completedJobs.Count;
        var onTime = completedJobs.Count(j => j.CompletedDate!.Value.Date <= j.DueDate!.Value.Date);
        var late = total - onTime;
        var percent = total > 0 ? Math.Round((decimal)onTime / total * 100, 1) : 0;

        return new OnTimeDeliveryReportItem(total, onTime, late, percent);
    }

    public async Task<List<AverageLeadTimeReportItem>> GetAverageLeadTimeAsync(CancellationToken ct)
    {
        // Calculate average time jobs spend at each stage based on activity log stage moves
        var stageMoves = await db.Set<Forge.Core.Entities.JobActivityLog>()
            .Where(a => a.Action == Forge.Platform.Enums.ActivityAction.StageMoved)
            .OrderBy(a => a.JobId).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var stageTimesRaw = new Dictionary<string, List<double>>();
        var byJob = stageMoves.GroupBy(a => a.JobId);
        foreach (var group in byJob)
        {
            var moves = group.OrderBy(m => m.CreatedAt).ToList();
            for (var i = 0; i < moves.Count - 1; i++)
            {
                var stageName = ExtractStageName(moves[i].Description);
                if (stageName is null) continue;
                var days = (moves[i + 1].CreatedAt - moves[i].CreatedAt).TotalDays;
                if (!stageTimesRaw.ContainsKey(stageName))
                    stageTimesRaw[stageName] = [];
                stageTimesRaw[stageName].Add(days);
            }
        }

        var stages = await db.Set<Forge.Core.Entities.JobStage>()
            .Select(s => new { s.Name, s.Color })
            .Distinct()
            .ToListAsync(ct);
        var colorMap = stages.ToDictionary(s => s.Name, s => s.Color ?? "#94a3b8");

        return stageTimesRaw
            .Select(kv => new AverageLeadTimeReportItem(
                kv.Key,
                colorMap.GetValueOrDefault(kv.Key, "#94a3b8"),
                Math.Round((decimal)kv.Value.Average(), 1)))
            .OrderBy(r => r.StageName)
            .ToList();
    }

    public async Task<List<TeamWorkloadReportItem>> GetTeamWorkloadAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday) startOfWeek = startOfWeek.AddDays(-7);
        var weekStart = DateOnly.FromDateTime(startOfWeek);
        var weekEnd = DateOnly.FromDateTime(today);

        var rawJobs = await db.Jobs
            .Where(j => !j.IsArchived && j.AssigneeId.HasValue)
            .Select(j => new { UserId = j.AssigneeId!.Value, j.CompletedDate, j.DueDate })
            .ToListAsync(ct);

        var assignedJobs = rawJobs
            .GroupBy(j => j.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                ActiveJobs = g.Count(j => j.CompletedDate == null),
                OverdueJobs = g.Count(j => j.CompletedDate == null && j.DueDate.HasValue && j.DueDate.Value < today),
            })
            .ToList();

        var userIds = assignedJobs.Select(j => j.UserId).ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Initials, u.AvatarColor })
            .ToDictionaryAsync(u => u.Id, ct);

        var weeklyEntries = await db.TimeEntries
            .Where(t => userIds.Contains(t.UserId) && t.Date >= weekStart && t.Date <= weekEnd)
            .Select(t => new { t.UserId, t.DurationMinutes })
            .ToListAsync(ct);

        var weeklyHours = weeklyEntries
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationMinutes));

        return assignedJobs
            .Where(j => users.ContainsKey(j.UserId))
            .Select(j =>
            {
                var user = users[j.UserId];
                var hours = Math.Round((decimal)weeklyHours.GetValueOrDefault(j.UserId, 0) / 60, 1);
                return new TeamWorkloadReportItem(
                    j.UserId, user.Name, user.Initials ?? "??", user.AvatarColor ?? "#94a3b8",
                    j.ActiveJobs, j.OverdueJobs, hours);
            })
            .OrderByDescending(r => r.ActiveJobs)
            .ToList();
    }

    public async Task<List<CustomerActivityReportItem>> GetCustomerActivityAsync(CancellationToken ct)
    {
        var rows = await db.Jobs
            .Where(j => j.CustomerId.HasValue)
            .Join(db.Customers, j => j.CustomerId, c => c.Id,
                (j, c) => new { j.CustomerId, CustomerName = c.Name, j.IsArchived, j.CompletedDate, j.CreatedAt })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.CustomerId, x.CustomerName })
            .Select(g => new CustomerActivityReportItem(
                g.Key.CustomerId!.Value,
                g.Key.CustomerName,
                g.Count(j => !j.IsArchived && j.CompletedDate == null),
                g.Count(j => j.CompletedDate != null),
                g.Count(),
                g.Max(j => j.CreatedAt)))
            .OrderByDescending(r => r.TotalJobs)
            .ToList();
    }

    public async Task<List<MyWorkHistoryReportItem>> GetMyWorkHistoryAsync(int userId, CancellationToken ct)
    {
        return await db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.Customer)
            .Where(j => j.AssigneeId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new MyWorkHistoryReportItem(
                j.Id,
                j.JobNumber,
                j.Title,
                j.CurrentStage.Name,
                j.CurrentStage.Color,
                j.Customer != null ? j.Customer.Name : null,
                j.DueDate,
                j.CreatedAt,
                j.CompletedDate))
            .ToListAsync(ct);
    }

    public async Task<List<MyTimeLogReportItem>> GetMyTimeLogAsync(int userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startDate = DateOnly.FromDateTime(start.UtcDateTime);
        var endDate = DateOnly.FromDateTime(end.UtcDateTime);

        return await db.TimeEntries
            .Include(t => t.Job)
            .Where(t => t.UserId == userId && t.Date >= startDate && t.Date <= endDate)
            .OrderByDescending(t => t.Date)
            .Select(t => new MyTimeLogReportItem(
                t.Id,
                t.Job != null ? t.Job.JobNumber : null,
                t.Job != null ? t.Job.Title : null,
                t.Notes,
                t.DurationMinutes,
                t.Category,
                t.Date))
            .ToListAsync(ct);
    }

    // ─── Financial Reports ───

    public async Task<List<ArAgingReportItem>> GetArAgingAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;

        var invoices = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Where(i => i.Status != Forge.Platform.Enums.InvoiceStatus.Voided
                && i.Status != Forge.Platform.Enums.InvoiceStatus.Draft)
            .ToListAsync(ct);

        return invoices
            .Where(i => i.BalanceDue > 0)
            .Select(i =>
            {
                var daysOverdue = Math.Max(0, (int)(today - i.DueDate.Date).TotalDays);
                var bucket = daysOverdue switch
                {
                    0 => "Current",
                    <= 30 => "1-30 Days",
                    <= 60 => "31-60 Days",
                    <= 90 => "61-90 Days",
                    _ => "90+ Days",
                };

                return new ArAgingReportItem(
                    i.Id, i.InvoiceNumber, i.Customer.Name,
                    i.InvoiceDate, i.DueDate,
                    i.Total, i.AmountPaid, i.BalanceDue,
                    daysOverdue, bucket);
            })
            .OrderByDescending(r => r.DaysOverdue)
            .ToList();
    }

    public async Task<List<RevenueReportItem>> GetRevenueAsync(
        DateTimeOffset start, DateTimeOffset end, string groupBy, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var invoices = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Where(i => i.InvoiceDate >= startUtc && i.InvoiceDate <= endUtc
                && i.Status != Forge.Platform.Enums.InvoiceStatus.Voided
                && i.Status != Forge.Platform.Enums.InvoiceStatus.Draft)
            .ToListAsync(ct);

        if (groupBy == "customer")
        {
            return invoices
                .GroupBy(i => i.Customer.Name)
                .Select(g => new RevenueReportItem(
                    g.Key, g.Key, g.Count(),
                    g.Sum(i => i.Subtotal), g.Sum(i => i.TaxAmount),
                    g.Sum(i => i.Total), g.Sum(i => i.AmountPaid)))
                .OrderByDescending(r => r.Total)
                .ToList();
        }

        // Default: group by month
        return invoices
            .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month })
            .Select(g => new RevenueReportItem(
                new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                null, g.Count(),
                g.Sum(i => i.Subtotal), g.Sum(i => i.TaxAmount),
                g.Sum(i => i.Total), g.Sum(i => i.AmountPaid)))
            .OrderBy(r => r.Period)
            .ToList();
    }

    public async Task<List<SimplePnlReportItem>> GetSimplePnlAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var result = new List<SimplePnlReportItem>();

        // Revenue from invoices
        var invoices = await db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.InvoiceDate >= startUtc && i.InvoiceDate <= endUtc
                && i.Status != Forge.Platform.Enums.InvoiceStatus.Voided
                && i.Status != Forge.Platform.Enums.InvoiceStatus.Draft)
            .ToListAsync(ct);

        var totalRevenue = invoices.Sum(i => i.Subtotal);
        var totalTax = invoices.Sum(i => i.TaxAmount);
        result.Add(new SimplePnlReportItem("Sales Revenue", "Revenue", totalRevenue));
        result.Add(new SimplePnlReportItem("Sales Tax Collected", "Revenue", totalTax));

        // Expenses by category
        var rawExpenses = await db.Expenses
            .Where(e => e.ExpenseDate >= startUtc && e.ExpenseDate <= endUtc
                && e.Status == Forge.Platform.Enums.ExpenseStatus.Approved)
            .Select(e => new { e.Category, e.Amount })
            .ToListAsync(ct);

        var pnlExpenses = rawExpenses
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount) })
            .ToList();

        foreach (var expense in pnlExpenses.OrderByDescending(e => e.Total))
        {
            result.Add(new SimplePnlReportItem(expense.Category, "Expense", expense.Total));
        }

        return result;
    }

    public async Task<List<MyExpenseHistoryReportItem>> GetMyExpenseHistoryAsync(
        int userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var rawExpenseHistory = await db.Expenses
            .Where(e => e.UserId == userId && e.ExpenseDate >= startUtc && e.ExpenseDate <= endUtc)
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => new { e.Id, e.Category, e.Description, e.Amount, e.Status, e.ExpenseDate })
            .ToListAsync(ct);

        return rawExpenseHistory
            .Select(e => new MyExpenseHistoryReportItem(
                e.Id, e.Category, e.Description, e.Amount,
                e.Status.ToString(), e.ExpenseDate, null))
            .ToList();
    }

    public async Task<List<QuoteToCloseReportItem>> GetQuoteToCloseAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var quotes = await db.Quotes
            .Include(q => q.Lines)
            .Where(q => q.CreatedAt >= startUtc && q.CreatedAt <= endUtc)
            .ToListAsync(ct);

        return quotes
            .GroupBy(q => q.Status)
            .Select(g => new QuoteToCloseReportItem(
                g.Key.ToString(),
                g.Count(),
                g.Sum(q => q.Lines.Sum(l => l.LineTotal))))
            .ToList();
    }

    public async Task<List<ShippingSummaryReportItem>> GetShippingSummaryAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var shipments = await db.Shipments
            .Include(s => s.Lines)
            .Include(s => s.SalesOrder)
            .Where(s => s.CreatedAt >= startUtc && s.CreatedAt <= endUtc)
            .ToListAsync(ct);

        return shipments
            .GroupBy(s => s.Status.ToString())
            .Select(g => new ShippingSummaryReportItem(
                g.Key,
                g.Count(),
                g.Sum(s => s.Lines.Sum(l => l.Quantity)),
                g.Count(s => s.ShippedDate.HasValue && s.SalesOrder != null && s.SalesOrder.RequestedDeliveryDate.HasValue
                    && s.ShippedDate.Value.Date <= s.SalesOrder.RequestedDeliveryDate.Value.Date),
                g.Count(s => s.ShippedDate.HasValue && s.SalesOrder != null && s.SalesOrder.RequestedDeliveryDate.HasValue
                    && s.ShippedDate.Value.Date > s.SalesOrder.RequestedDeliveryDate.Value.Date)))
            .ToList();
    }

    public async Task<List<TimeInStageReportItem>> GetTimeInStageAsync(int? trackTypeId, CancellationToken ct)
    {
        var stageMoves = await db.Set<Forge.Core.Entities.JobActivityLog>()
            .Where(a => a.Action == Forge.Platform.Enums.ActivityAction.StageMoved)
            .OrderBy(a => a.JobId).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

        // Optionally filter by track type
        HashSet<int>? trackJobIds = null;
        if (trackTypeId.HasValue)
        {
            trackJobIds = (await db.Jobs
                .Where(j => j.TrackTypeId == trackTypeId.Value)
                .Select(j => j.Id)
                .ToListAsync(ct)).ToHashSet();
        }

        var stageData = new Dictionary<string, (List<double> Days, int Count)>();
        var byJob = stageMoves.GroupBy(a => a.JobId);
        foreach (var group in byJob)
        {
            if (trackJobIds != null && !trackJobIds.Contains(group.Key))
                continue;

            var moves = group.OrderBy(m => m.CreatedAt).ToList();
            for (var i = 0; i < moves.Count - 1; i++)
            {
                var stageName = ExtractStageName(moves[i].Description);
                if (stageName is null) continue;
                var days = (moves[i + 1].CreatedAt - moves[i].CreatedAt).TotalDays;
                if (!stageData.ContainsKey(stageName))
                    stageData[stageName] = (new List<double>(), 0);
                stageData[stageName].Days.Add(days);
            }
        }

        var stages = await db.Set<Forge.Core.Entities.JobStage>()
            .Select(s => new { s.Name, s.Color })
            .Distinct()
            .ToListAsync(ct);
        var colorMap = stages.ToDictionary(s => s.Name, s => s.Color ?? "#94a3b8");

        var result = stageData
            .Select(kv => new TimeInStageReportItem(
                kv.Key,
                colorMap.GetValueOrDefault(kv.Key, "#94a3b8"),
                Math.Round((decimal)kv.Value.Days.Average(), 1),
                kv.Value.Days.Count,
                false))
            .OrderByDescending(r => r.AverageDays)
            .ToList();

        // Mark the top bottleneck (longest average time)
        if (result.Count > 0)
        {
            var maxDays = result[0].AverageDays;
            result = result.Select(r => r with { IsBottleneck = r.AverageDays == maxDays }).ToList();
        }

        return result;
    }

    // ─── Batch 4 Reports ───

    public async Task<List<EmployeeProductivityReportItem>> GetEmployeeProductivityAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var startDate = DateOnly.FromDateTime(startUtc.Date);
        var endDate = DateOnly.FromDateTime(endUtc.Date);

        // Hours per user from time entries
        var rawTimeEntries = await db.TimeEntries
            .Where(t => t.Date >= startDate && t.Date <= endDate)
            .Select(t => new { t.UserId, t.DurationMinutes })
            .ToListAsync(ct);

        var hoursByUser = rawTimeEntries
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationMinutes));

        // Jobs completed by assignee in date range
        var rawCompletedJobs = await db.Jobs
            .Where(j => j.AssigneeId.HasValue
                && j.CompletedDate.HasValue
                && j.CompletedDate.Value >= startUtc
                && j.CompletedDate.Value <= endUtc)
            .Select(j => new { UserId = j.AssigneeId!.Value, j.CompletedDate, j.DueDate })
            .ToListAsync(ct);

        var completedByUser = rawCompletedJobs
            .GroupBy(j => j.UserId)
            .ToDictionary(g => g.Key, g => new
            {
                UserId = g.Key,
                JobsCompleted = g.Count(),
                OnTime = g.Count(j => !j.DueDate.HasValue || j.CompletedDate!.Value.Date <= j.DueDate.Value.Date),
            });

        var allUserIds = hoursByUser.Keys.Union(completedByUser.Keys).ToList();
        var users = await db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        return allUserIds
            .Where(id => users.ContainsKey(id))
            .Select(id =>
            {
                var totalHours = Math.Round((decimal)hoursByUser.GetValueOrDefault(id, 0) / 60, 1);
                var completed = completedByUser.GetValueOrDefault(id);
                var jobsCompleted = completed?.JobsCompleted ?? 0;
                var onTime = completed?.OnTime ?? 0;
                var avgHours = jobsCompleted > 0 ? Math.Round(totalHours / jobsCompleted, 1) : 0;
                var onTimePct = jobsCompleted > 0 ? Math.Round((decimal)onTime / jobsCompleted * 100, 1) : 0;

                return new EmployeeProductivityReportItem(
                    id, users[id], totalHours, jobsCompleted, avgHours, onTimePct);
            })
            .OrderByDescending(r => r.TotalHours)
            .ToList();
    }

    public async Task<List<InventoryLevelReportItem>> GetInventoryLevelsAsync(CancellationToken ct)
    {
        var parts = await db.Parts
            .Select(p => new { p.Id, p.PartNumber, p.Description, p.MinStockThreshold, p.ReorderPoint })
            .ToListAsync(ct);

        var rawBinContents = await db.BinContents
            .Where(b => b.EntityType == "part" && b.Status == Forge.Platform.Enums.BinContentStatus.Stored)
            .Select(b => new { b.EntityId, b.Quantity })
            .ToListAsync(ct);

        var stockByPart = rawBinContents
            .GroupBy(b => b.EntityId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));

        return parts
            .Select(p =>
            {
                var stock = stockByPart.GetValueOrDefault(p.Id, 0);
                var isLow = p.MinStockThreshold.HasValue && stock < p.MinStockThreshold.Value;
                return new InventoryLevelReportItem(
                    p.Id, p.PartNumber, p.Description ?? "", stock,
                    p.MinStockThreshold, p.ReorderPoint, isLow);
            })
            .OrderBy(r => r.PartNumber)
            .ToList();
    }

    public async Task<List<MaintenanceReportItem>> GetMaintenanceAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var now = DateTimeOffset.UtcNow;

        var assets = await db.Assets
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);

        var schedules = await db.MaintenanceSchedules
            .Where(s => s.IsActive)
            .ToListAsync(ct);

        var logs = await db.MaintenanceLogs
            .Where(l => l.PerformedAt >= startUtc && l.PerformedAt <= endUtc)
            .ToListAsync(ct);

        var assetMap = assets.ToDictionary(a => a.Id, a => a.Name);

        return assets
            .Select(a =>
            {
                var assetSchedules = schedules.Where(s => s.AssetId == a.Id).ToList();
                var assetLogs = logs.Where(l => assetSchedules.Any(s => s.Id == l.MaintenanceScheduleId)).ToList();
                var overdue = assetSchedules.Count(s => s.NextDueAt < now);
                var totalCost = assetLogs.Sum(l => l.Cost ?? 0);

                return new MaintenanceReportItem(
                    a.Id, a.Name,
                    assetSchedules.Count,
                    assetLogs.Count,
                    overdue,
                    totalCost);
            })
            .Where(r => r.ScheduledCount > 0 || r.CompletedCount > 0)
            .OrderByDescending(r => r.OverdueCount)
            .ThenByDescending(r => r.TotalCost)
            .ToList();
    }

    public async Task<List<QualityScrapReportItem>> GetQualityScrapAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;

        var rawRuns = await db.ProductionRuns
            .Where(r => r.CompletedAt.HasValue && r.CompletedAt.Value >= startUtc && r.CompletedAt.Value <= endUtc)
            .Join(db.Parts, r => r.PartId, p => p.Id,
                (r, p) => new { r.PartId, p.PartNumber, r.CompletedQuantity, r.ScrapQuantity })
            .ToListAsync(ct);

        var runs = rawRuns
            .GroupBy(r => new { r.PartId, r.PartNumber })
            .Select(g => new
            {
                g.Key.PartId,
                g.Key.PartNumber,
                TotalProduced = g.Sum(r => r.CompletedQuantity),
                TotalScrapped = g.Sum(r => r.ScrapQuantity),
            })
            .ToList();

        return runs
            .Select(r =>
            {
                var totalOutput = r.TotalProduced + r.TotalScrapped;
                var scrapRate = totalOutput > 0 ? Math.Round((decimal)r.TotalScrapped / totalOutput * 100, 1) : 0;
                var yieldRate = totalOutput > 0 ? Math.Round((decimal)r.TotalProduced / totalOutput * 100, 1) : 0;

                return new QualityScrapReportItem(
                    r.PartId, r.PartNumber, r.TotalProduced, r.TotalScrapped, scrapRate, yieldRate);
            })
            .OrderByDescending(r => r.ScrapRate)
            .ToList();
    }

    public async Task<List<CycleReviewReportItem>> GetCycleReviewAsync(CancellationToken ct)
    {
        var cycles = await db.PlanningCycles
            .Include(c => c.Entries)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync(ct);

        return cycles
            .Select(c =>
            {
                var total = c.Entries.Count;
                var completed = c.Entries.Count(e => e.CompletedAt.HasValue);
                var rolledOver = c.Entries.Count(e => e.IsRolledOver);
                var rate = total > 0 ? Math.Round((decimal)completed / total * 100, 1) : 0;

                return new CycleReviewReportItem(
                    c.Id, c.Name, c.StartDate, c.EndDate,
                    total, completed, rate, rolledOver);
            })
            .ToList();
    }

    private static string? ExtractStageName(string description)
    {
        var prefix = "Moved to ";
        if (description.StartsWith(prefix))
            return description[prefix.Length..].Trim();
        return null;
    }
}
