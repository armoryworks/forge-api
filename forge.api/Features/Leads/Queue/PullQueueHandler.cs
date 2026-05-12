using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leads.Queue;

/// <summary>
/// Phase 1r / Batch 6 — pull next N leads off the worker queue.
/// Serves leads where:
///   • OutreachState = Queued (eligible for first-touch)
///   • Status NOT IN (Lost, Converted)
///   • No active cooldown (CooldownUntil null OR &lt;= now)
///   • Optional CampaignId filter so reps can choose a specific batch
///
/// Concurrency: uses Postgres FOR UPDATE SKIP LOCKED inside a
/// transaction so two reps pulling at the same time get disjoint
/// slices — neither blocks, neither dials the same lead. The handler
/// flips the pulled leads to OutreachState=InProgress before
/// committing the transaction so the next pull never re-serves them.
/// </summary>
public record PullQueueCommand(int UserId, PullQueueRequest Request) : IRequest<List<QueueLeadResponseModel>>;

public class PullQueueHandler(AppDbContext db, IClock clock)
    : IRequestHandler<PullQueueCommand, List<QueueLeadResponseModel>>
{
    public async Task<List<QueueLeadResponseModel>> Handle(PullQueueCommand request, CancellationToken ct)
    {
        var count = Math.Clamp(request.Request.Count, 1, 50);
        var now = clock.UtcNow;

        // Postgres-specific FOR UPDATE SKIP LOCKED via raw SQL on the
        // candidate id list. EF Core can't model SKIP LOCKED in LINQ.
        // We pull ids only inside the transaction, then load the full
        // entities by those ids and mutate them — keeps the lock window
        // narrow and the projection straightforward.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sql = @"
            SELECT id FROM leads
            WHERE outreach_state = 'Queued'
              AND status NOT IN ('Lost', 'Converted')
              AND deleted_at IS NULL
              AND ({0}::int IS NULL OR campaign_id = {0})
              AND NOT EXISTS (
                SELECT 1 FROM lead_outreach_preferences p
                WHERE p.lead_id = leads.id
                  AND p.deleted_at IS NULL
                  AND p.cooldown_until IS NOT NULL
                  AND p.cooldown_until > {1}
              )
            ORDER BY created_at ASC
            LIMIT {2}
            FOR UPDATE SKIP LOCKED";

        var idResults = await db.Database
            .SqlQueryRaw<int>(sql, request.Request.CampaignId ?? (object)DBNull.Value, now, count)
            .ToListAsync(ct);

        if (idResults.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var leads = await db.Leads
            .Include(l => l.Campaign)
            .Where(l => idResults.Contains(l.Id))
            .ToListAsync(ct);

        foreach (var lead in leads)
        {
            lead.OutreachState = OutreachState.InProgress;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Pre-load matching prefs for the cooldown / opt-out badges
        var leadIds = leads.Select(l => l.Id).ToList();
        var prefs = await db.LeadOutreachPreferences.AsNoTracking()
            .Where(p => leadIds.Contains(p.LeadId))
            .ToDictionaryAsync(p => p.LeadId, p => p, ct);

        var lastActivities = await db.ActivityLogs.AsNoTracking()
            .Where(a => a.EntityType == "Lead" && leadIds.Contains(a.EntityId))
            .GroupBy(a => a.EntityId)
            .Select(g => new { LeadId = g.Key, Last = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.LeadId, x => x.Last, ct);

        return leads.Select(l => {
            prefs.TryGetValue(l.Id, out var p);
            lastActivities.TryGetValue(l.Id, out var last);
            return new QueueLeadResponseModel(
                l.Id, l.CompanyName, l.ContactName, l.Email, l.Phone, l.Source, l.Notes,
                l.Status, l.OutreachState, l.CampaignId, l.Campaign?.Name,
                last == default ? null : last,
                p?.CooldownUntil, p?.EmailOptOut ?? false, p?.CallOptOut ?? false);
        }).ToList();
    }
}
