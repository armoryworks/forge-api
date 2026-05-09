using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Data.Repositories;

public class LeadRepository(AppDbContext db) : ILeadRepository
{
    /// <summary>
    /// Phase 1j — leads in these statuses are "active" for staleness purposes.
    /// Lost / Converted leads are intentionally never marked stale.
    /// </summary>
    private static readonly LeadStatus[] ActiveStatuses =
    [
        LeadStatus.New, LeadStatus.Contacted, LeadStatus.Quoting,
    ];

    /// <summary>
    /// Phase 1j — staleness threshold for an active lead. After this many
    /// days with no activity the UI flags the row. 14 is a sales-cycle-
    /// friendly default; admin override (system setting) is a follow-on.
    /// </summary>
    private const int StaleAfterDays = 14;

    /// <summary>Engagement window for the recent-activity count chip.</summary>
    private const int EngagementWindowDays = 30;

    public async Task<List<LeadResponseModel>> GetLeadsAsync(LeadStatus? status, string? search, CancellationToken ct)
    {
        var query = db.Leads.AsQueryable();

        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                l.CompanyName.ToLower().Contains(term) ||
                (l.ContactName != null && l.ContactName.ToLower().Contains(term)) ||
                (l.Email != null && l.Email.ToLower().Contains(term)));
        }

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        if (leads.Count == 0) return [];

        // Phase 1j — bulk-load engagement stats for all leads in one
        // query rather than N+1. Two aggregates per lead:
        //   1. Last activity timestamp (drives staleness)
        //   2. Count of comm-flavoured activity in the engagement window
        //      (drives the engagement score chip)
        var leadIds = leads.Select(l => l.Id).ToList();
        var stats = await ComputeStatsAsync(leadIds, ct);

        return leads.Select(l =>
        {
            stats.TryGetValue(l.Id, out var stat);
            return ToResponseModel(l, stat);
        }).ToList();
    }

    public async Task<LeadResponseModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return null;
        var stats = await ComputeStatsAsync([id], ct);
        stats.TryGetValue(id, out var stat);
        return ToResponseModel(lead, stat);
    }

    public Task<Lead?> FindAsync(int id, CancellationToken ct)
        => db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task AddAsync(Lead lead, CancellationToken ct)
    {
        await db.Leads.AddAsync(lead, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    /// <summary>
    /// Bulk-compute (LastActivityAt, RecentEngagementCount) for the
    /// given lead ids. One query for the last-activity max, one for the
    /// engagement count — both grouped by EntityId. Returns a dictionary
    /// keyed by lead id; missing entries mean "no activity logged".
    /// </summary>
    private async Task<Dictionary<int, LeadEngagementStat>> ComputeStatsAsync(
        IReadOnlyList<int> leadIds, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-EngagementWindowDays);

        var raw = await db.Set<ActivityLog>().AsNoTracking()
            .Where(a => a.EntityType == "Lead" && leadIds.Contains(a.EntityId))
            .GroupBy(a => a.EntityId)
            .Select(g => new
            {
                LeadId = g.Key,
                LastActivityAt = (DateTimeOffset?)g.Max(a => a.CreatedAt),
                RecentEngagementCount = g.Count(a =>
                    a.CreatedAt >= since &&
                    (a.Action.StartsWith("communication-") ||
                     a.Action.StartsWith("interaction-auto-") ||
                     a.Action == "interaction-logged")),
            })
            .ToListAsync(ct);

        return raw.ToDictionary(
            x => x.LeadId,
            x => new LeadEngagementStat(x.LastActivityAt, x.RecentEngagementCount));
    }

    private static LeadResponseModel ToResponseModel(Lead l, LeadEngagementStat? stat)
    {
        var lastActivity = stat?.LastActivityAt;
        var engagementCount = stat?.RecentEngagementCount ?? 0;
        var isStale = ActiveStatuses.Contains(l.Status)
            && (lastActivity ?? l.CreatedAt) < DateTimeOffset.UtcNow.AddDays(-StaleAfterDays);

        return new LeadResponseModel(
            l.Id, l.CompanyName, l.ContactName, l.Email, l.Phone, l.Source,
            l.Status, l.Notes, l.FollowUpDate, l.LostReason,
            l.ConvertedCustomerId, l.CreatedAt, l.UpdatedAt,
            l.EngagementShape, l.CustomFieldValues,
            lastActivity, engagementCount, isStale);
    }

    private sealed record LeadEngagementStat(DateTimeOffset? LastActivityAt, int RecentEngagementCount);
}
