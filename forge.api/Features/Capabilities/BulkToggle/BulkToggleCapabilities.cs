using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.BulkToggle;

/// <summary>
/// Phase 4 Phase-C — Atomic bulk toggle: all rows succeed or none. Validates
/// the WHOLE candidate state set against dependency / mutex rules BEFORE
/// applying any change (Phase C decision: whole-set semantics — enabling A
/// and disabling A's dependent B in the same bulk should succeed; per-row
/// validation against the live snapshot would block this case incorrectly).
///
/// Used as the substrate for Phase G's preset-apply (which will additionally
/// emit a single <c>PresetApplied</c> audit row in place of the per-row
/// <c>CapabilityEnabled</c>/<c>CapabilityDisabled</c> rows this handler
/// writes today).
///
/// Optimistic concurrency: each row's optional <c>IfMatch</c> is checked
/// against its current Version; mismatch fails the whole batch with 412.
/// </summary>
public record BulkToggleCapabilitiesCommand(
    IReadOnlyList<BulkToggleItem> Items,
    string? Reason)
    : IRequest<IReadOnlyList<CapabilityDescriptorEntry>>;

public class BulkToggleCapabilitiesValidator : AbstractValidator<BulkToggleCapabilitiesCommand>
{
    public BulkToggleCapabilitiesValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count)
            .WithMessage("Duplicate capability IDs in bulk toggle payload.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty().Matches("^CAP-[A-Z0-9-]+$");
        });

        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}

public class BulkToggleCapabilitiesHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    ISystemAuditWriter auditWriter,
    IHubContext<NotificationHub> notificationHub)
    : IRequestHandler<BulkToggleCapabilitiesCommand, IReadOnlyList<CapabilityDescriptorEntry>>
{
    public async Task<IReadOnlyList<CapabilityDescriptorEntry>> Handle(
        BulkToggleCapabilitiesCommand request,
        CancellationToken cancellationToken)
    {
        var ids = request.Items.Select(i => i.Id).ToList();
        var rows = await db.Capabilities
            .Where(c => ids.Contains(c.Code))
            .ToListAsync(cancellationToken);

        if (rows.Count != ids.Count)
        {
            var missing = ids.Where(id => !rows.Any(r => r.Code == id)).ToList();
            throw new CapabilityMutationException(
                StatusCodes.Status404NotFound,
                "capability-not-found",
                $"Unknown capability codes in bulk toggle: {string.Join(", ", missing)}.",
                new Dictionary<string, object?>
                {
                    ["missing"] = missing,
                });
        }

        var rowByCode = rows.ToDictionary(r => r.Code);

        // 1. Per-row optimistic-concurrency check (any mismatch = whole-batch
        //    failure, per Phase C atomic semantic).
        foreach (var item in request.Items)
        {
            var row = rowByCode[item.Id];
            if (string.IsNullOrWhiteSpace(item.IfMatch)) continue;
            var trimmed = item.IfMatch.Trim();
            if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
            trimmed = trimmed.Trim('"').Trim();
            if (!uint.TryParse(trimmed, out var v) || v != row.Version)
            {
                throw new CapabilityMutationException(
                    StatusCodes.Status412PreconditionFailed,
                    "version-mismatch",
                    $"Stale ETag for capability '{item.Id}' — refresh and try again.",
                    new Dictionary<string, object?>
                    {
                        ["capability"] = item.Id,
                        ["expected"] = row.Version,
                        ["received"] = item.IfMatch,
                    });
            }
        }

        // 2. Build the candidate state map: start from the current snapshot,
        //    then overlay the bulk delta so dependency/mutex evaluation sees
        //    the post-apply world. This is the whole-set semantic: enabling
        //    A and disabling A's dependent B in the same bulk is OK because
        //    the post-apply world has neither A's missing dep nor B's
        //    enabled dependent.
        var candidate = new Dictionary<string, bool>(snapshots.Current.EnabledByCode, StringComparer.Ordinal);
        foreach (var item in request.Items) candidate[item.Id] = item.Enabled;

        // 3. Validate dependency / mutex rules against the candidate state.
        //    A violation is a violation no matter who caused it; we surface
        //    the full list so the admin can fix the request.
        var violations = new List<object>();
        foreach (var item in request.Items)
        {
            // Skip idempotent rows.
            if (rowByCode[item.Id].Enabled == item.Enabled) continue;

            if (item.Enabled)
            {
                var missing = CapabilityDependencyResolver.FindMissingDependencies(item.Id, candidate);
                if (missing.Count > 0)
                {
                    violations.Add(new
                    {
                        code = "capability-missing-dependencies",
                        capability = item.Id,
                        missing,
                        message = $"'{item.Id}' requires: {string.Join(", ", missing)}",
                    });
                }
                var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(item.Id, candidate);
                if (conflicts.Count > 0)
                {
                    violations.Add(new
                    {
                        code = "capability-mutex-violation",
                        capability = item.Id,
                        conflicts,
                        message = $"'{item.Id}' conflicts with enabled: {string.Join(", ", conflicts)}",
                    });
                }
            }
            else
            {
                var dependents = CapabilityDependencyResolver.FindEnabledDependents(item.Id, candidate);
                if (dependents.Count > 0)
                {
                    violations.Add(new
                    {
                        code = "capability-has-dependents",
                        capability = item.Id,
                        dependents,
                        message = $"'{item.Id}' is required by: {string.Join(", ", dependents)}",
                    });
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new CapabilityMutationException(
                StatusCodes.Status409Conflict,
                "bulk-validation-failed",
                $"{violations.Count} constraint violation(s) in bulk toggle — none applied.",
                new Dictionary<string, object?>
                {
                    ["violations"] = violations,
                });
        }

        // 4. Apply atomically. EF Core in-memory + Postgres both support
        //    SaveChangesAsync as a single transactional unit for this scope.
        var changedItems = new List<(Capability Row, bool From, bool To, string? Reason)>();
        foreach (var item in request.Items)
        {
            var row = rowByCode[item.Id];
            var fromState = row.Enabled;
            var toState = item.Enabled;
            if (fromState == toState) continue;
            row.Enabled = toState;
            changedItems.Add((row, fromState, toState, request.Reason));
        }
        await db.SaveChangesAsync(cancellationToken);

        await snapshots.RefreshAsync(cancellationToken);

        // 5. One audit row per changed capability (Phase G's preset-apply will
        //    use a single PresetApplied row instead).
        var actorId = db.CurrentUserId ?? 0;
        foreach (var (row, fromState, toState, reason) in changedItems)
        {
            var details = JsonSerializer.Serialize(new
            {
                code = row.Code,
                from = fromState,
                to = toState,
                before = new { enabled = fromState },
                after = new { enabled = toState },
                reason,
                actorUserId = actorId,
                bulk = true,
            });
            var action = toState ? CapabilityAuditEvents.Enabled : CapabilityAuditEvents.Disabled;
            await auditWriter.WriteAsync(
                action: action,
                userId: actorId,
                entityType: CapabilityAuditEvents.EntityType,
                entityId: row.Id,
                details: details,
                ct: cancellationToken);
        }

        // 6. Broadcast each change individually so the UI can incrementally
        //    update its descriptor signal.
        foreach (var (row, _, toState, _) in changedItems)
        {
            await notificationHub.Clients.All.SendAsync(
                "capabilityChanged",
                new { capabilityId = row.Code, enabled = toState },
                cancellationToken);
        }

        // 7. Re-read the affected rows so the response carries the bumped
        //    Version values.
        var refreshed = await db.Capabilities
            .AsNoTracking()
            .Where(c => ids.Contains(c.Code))
            .ToListAsync(cancellationToken);

        return refreshed
            .OrderBy(r => r.Area).ThenBy(r => r.Code)
            .Select(r => new CapabilityDescriptorEntry(
                Id: r.Code,
                Code: r.Code,
                Area: r.Area,
                Name: r.Name,
                Description: r.Description,
                Enabled: r.Enabled,
                IsDefaultOn: r.IsDefaultOn,
                RequiresRoles: r.RequiresRoles,
                Version: r.Version,
                ETag: $"W/\"{r.Version}\"",
                ConfigVersion: null,
                ConfigETag: null,
                ConfigId: null,
                Dependencies: Array.Empty<string>(),
                Mutexes: Array.Empty<string>()))
            .ToList();
    }
}
