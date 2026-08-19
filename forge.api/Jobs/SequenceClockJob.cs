using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Sequences;
using Forge.Api.Features.Sequences.GateSources;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Jobs;

/// <summary>
/// The engine's only timer (Hangfire, every minute). Three duties, each idempotent:
/// (1) fire due resource clocks once (FiredAt) and publish <see cref="SequenceClockExpiredEvent"/>;
/// (2) fire due step dwell clocks once (DwellFiredAt) likewise;
/// (3) re-evaluate running instances whose ResourceClock / TimeWindow gates may have crossed a boundary since
///     they were last evaluated, so time-based gates flip without anyone clicking.
/// </summary>
public class SequenceClockJob(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock, IPublisher publisher, ILogger<SequenceClockJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var touchedResources = new HashSet<(string, int)>();

        // (1) resource clocks
        var dueClocks = await db.SequenceResourceClocks
            .Where(c => c.DeletedAt == null && c.FiredAt == null && c.ExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var c in dueClocks)
        {
            c.FiredAt = now;
            touchedResources.Add((c.ResourceType, c.ResourceId));
            db.LogActivityAt("sequence-clock-expired", $"Clock expired ({c.ExpiryAction}){(c.EscalateRole is null ? "" : $" → {c.EscalateRole}")}", (c.ResourceType, c.ResourceId));
            await publisher.Publish(new SequenceClockExpiredEvent("resource", null, null, c.ResourceType, c.ResourceId, c.ExpiryAction, c.EscalateRole, c.ExpiresAt), ct);
        }

        // (2) dwell clocks
        var dueSteps = await db.SequenceStepInstances
            .Include(s => s.Instance)
            .Where(s => s.DwellFiredAt == null && s.DwellExpiresAt != null && s.DwellExpiresAt <= now
                        && s.Status == SequenceStepStatus.InProgress && s.Instance!.Status == SequenceInstanceStatus.Running)
            .ToListAsync(ct);
        foreach (var s in dueSteps)
        {
            var def = await db.SequenceStepDefinitions.FirstOrDefaultAsync(d => d.DefinitionId == s.Instance!.DefinitionId && d.Key == s.StepKey, ct);
            var action = def?.DwellExpiryAction ?? SequenceExpiryAction.Flag;
            s.DwellFiredAt = now;
            db.SequenceEvents.Add(SequenceEvaluator.Event(s.Instance!, SequenceEventType.ClockExpired, now, null, s.StepKey,
                payloadJson: $"{{\"kind\":\"dwell\",\"action\":\"{action}\",\"escalateRole\":{(def?.EscalateRole is null ? "null" : $"\"{def.EscalateRole}\"")}}}"));
            if (action == SequenceExpiryAction.Escalate)
                db.SequenceEvents.Add(SequenceEvaluator.Event(s.Instance!, SequenceEventType.Escalated, now, null, s.StepKey,
                    payloadJson: $"{{\"role\":{(def?.EscalateRole is null ? "null" : $"\"{def.EscalateRole}\"")}}}"));
            db.LogActivityAt("sequence-dwell-expired", $"Step '{s.StepKey}' exceeded its dwell time ({action})", SequenceQueries.IndexingPoints(s.Instance!));
            await publisher.Publish(new SequenceClockExpiredEvent("dwell", s.InstanceId, s.StepKey, null, null, action, def?.EscalateRole, s.DwellExpiresAt!.Value), ct);
        }
        if (dueClocks.Count > 0 || dueSteps.Count > 0) await db.SaveChangesAsync(ct);

        // (3) time-sensitive gates
        var candidates = await db.SequenceInstances
            .Where(i => i.Status == SequenceInstanceStatus.Running && i.DeletedAt == null)
            .Where(i => i.Definition!.Gates.Any(g => g.SourceType == SequenceGateSourceType.ResourceClock || g.SourceType == SequenceGateSourceType.TimeWindow))
            .Select(i => new { i.Id, i.SubjectEntityType, i.SubjectEntityId,
                Gates = i.Definition!.Gates.Where(g => g.SourceType == SequenceGateSourceType.ResourceClock || g.SourceType == SequenceGateSourceType.TimeWindow)
                    .Select(g => new { g.StepKey, g.Key, g.SourceType, g.ConfigJson }).ToList(),
                Evaluated = i.Gates.Select(g => new { g.StepKey, g.GateKey, g.LastEvaluatedAt }).ToList() })
            .ToListAsync(ct);

        var toEvaluate = new List<int>();
        foreach (var c in candidates)
        {
            var due = false;
            foreach (var g in c.Gates)
            {
                var last = c.Evaluated.FirstOrDefault(e => e.StepKey == g.StepKey && e.GateKey == g.Key)?.LastEvaluatedAt;
                var cfg = SequenceGateConfig.Parse(g.ConfigJson);
                if (g.SourceType == SequenceGateSourceType.TimeWindow)
                {
                    var nb = cfg.GetDate("notBefore"); var na = cfg.GetDate("notAfter");
                    due |= last is null || (nb.HasValue && Crossed(nb.Value, last.Value, now)) || (na.HasValue && Crossed(na.Value, last.Value, now));
                }
                else
                {
                    var type = cfg.GetBool("fromSubject") ? c.SubjectEntityType : cfg.GetString("resourceType");
                    var id = cfg.GetBool("fromSubject") ? c.SubjectEntityId : cfg.GetInt("resourceId");
                    due |= last is null || (type is not null && id.HasValue && touchedResources.Contains((type, id.Value)));
                }
                if (due) break;
            }
            if (due) toEvaluate.Add(c.Id);
        }

        foreach (var id in toEvaluate)
        {
            try
            {
                await evaluation.EvaluateAsync(id, null, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "SequenceClockJob: re-evaluation of instance {InstanceId} failed", id);
            }
        }
    }

    private static bool Crossed(DateTimeOffset boundary, DateTimeOffset last, DateTimeOffset now) => last < boundary && boundary <= now;
}
