using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.Toggle;

/// <summary>
/// Phase 4 Phase-B — Mutates the <c>Enabled</c> flag of a single capability,
/// refreshes the <see cref="ICapabilitySnapshotProvider"/>, writes a system
/// audit row, and broadcasts a SignalR <c>capabilityChanged</c> event so all
/// connected clients can reload the descriptor.
///
/// Phase 4 Phase-C — Adds optimistic concurrency (If-Match → 412),
/// dependency-cascade-block on disable (4D-decisions-log #7), dependency
/// check on enable, soft-mutex check on enable (4D §8.3), and richer audit
/// content (before/after + actor + reason).
///
/// Per Phase C decision: we do NOT auto-cascade. A 409 is returned with the
/// list of dependents/dependencies/conflicts so the admin can act explicitly.
/// </summary>
public record ToggleCapabilityCommand(
    string Code,
    bool Enabled,
    string? IfMatch = null,
    string? Reason = null) : IRequest<CapabilityDescriptorEntry>;

public class ToggleCapabilityValidator : AbstractValidator<ToggleCapabilityCommand>
{
    public ToggleCapabilityValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^CAP-[A-Z0-9-]+$")
            .WithMessage("Capability code must match the catalog format CAP-{AREA}-{NAME}.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }
}

public class ToggleCapabilityHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    ISystemAuditWriter auditWriter,
    IHubContext<NotificationHub> notificationHub,
    IGlCapabilityGate glGate)
    : IRequestHandler<ToggleCapabilityCommand, CapabilityDescriptorEntry>
{
    /// <summary>The one capability with a domain hard-gate beyond the dependency graph (§5.5/§7A).</summary>
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    public async Task<CapabilityDescriptorEntry> Handle(
        ToggleCapabilityCommand request,
        CancellationToken cancellationToken)
    {
        var row = await db.Capabilities
            .FirstOrDefaultAsync(c => c.Code == request.Code, cancellationToken)
            ?? throw new KeyNotFoundException($"Capability '{request.Code}' not found.");

        // 1. Optimistic concurrency check (Phase C). Permissive when If-Match
        //    is absent, mirroring the IfMatchAttribute policy on transactional
        //    entities (Phase 3 / WU-11).
        if (!string.IsNullOrWhiteSpace(request.IfMatch))
        {
            var trimmed = request.IfMatch.Trim();
            if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
            trimmed = trimmed.Trim('"').Trim();

            if (!uint.TryParse(trimmed, out var providedVersion) || providedVersion != row.Version)
            {
                throw new CapabilityMutationException(
                    StatusCodes.Status412PreconditionFailed,
                    "version-mismatch",
                    $"Stale ETag for capability '{request.Code}' — refresh and try again.",
                    new Dictionary<string, object?>
                    {
                        ["capability"] = request.Code,
                        ["expected"] = row.Version,
                        ["received"] = request.IfMatch,
                    });
            }
        }

        var fromState = row.Enabled;
        var toState = request.Enabled;

        // 2. Dependency / mutex evaluation (Phase C). Idempotent toggles
        //    short-circuit the rule check — flipping enabled=true on an
        //    already-enabled row never introduces new constraint violations.
        if (fromState != toState)
        {
            var currentEnabled = snapshots.Current.EnabledByCode;

            if (toState)
            {
                // Enabling: check missing dependencies and enabled mutex peers.
                var missingDeps = CapabilityDependencyResolver.FindMissingDependencies(request.Code, currentEnabled);
                if (missingDeps.Count > 0)
                {
                    throw new CapabilityMutationException(
                        StatusCodes.Status409Conflict,
                        "capability-missing-dependencies",
                        $"Cannot enable '{request.Code}' — required dependencies are disabled: {string.Join(", ", missingDeps)}.",
                        new Dictionary<string, object?>
                        {
                            ["capability"] = request.Code,
                            ["missing"] = missingDeps,
                        });
                }

                var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(request.Code, currentEnabled);
                if (conflicts.Count > 0)
                {
                    throw new CapabilityMutationException(
                        StatusCodes.Status409Conflict,
                        "capability-mutex-violation",
                        $"Cannot enable '{request.Code}' — disable mutually-exclusive capabilities first: {string.Join(", ", conflicts)}.",
                        new Dictionary<string, object?>
                        {
                            ["capability"] = request.Code,
                            ["conflicts"] = conflicts,
                        });
                }

                // §5.5 / §7A opening-balances hard-gate (wired 2026-07-07; the gate logic itself
                // shipped dark in Phase 0): CAP-ACCT-FULLGL cannot be enabled while any active
                // book lacks its posted Conversion opening journal. Installs with no Book yet
                // pass trivially — there is no ledger to protect.
                if (request.Code == FullGlCapability)
                {
                    var activeBookIds = await db.Books
                        .AsNoTracking()
                        .Where(b => b.IsActive)
                        .Select(b => b.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var bookId in activeBookIds)
                    {
                        var eligibility = await glGate.EvaluateAsync(bookId, cancellationToken);
                        if (!eligibility.CanEnable)
                        {
                            throw new CapabilityMutationException(
                                StatusCodes.Status409Conflict,
                                "capability-gl-opening-balances",
                                eligibility.Reason ?? $"Book {bookId} has not passed the opening-balances gate.",
                                new Dictionary<string, object?>
                                {
                                    ["capability"] = request.Code,
                                    ["bookId"] = bookId,
                                });
                        }
                    }
                }
            }
            else
            {
                // Disabling: block when other enabled capabilities depend on this.
                var dependents = CapabilityDependencyResolver.FindEnabledDependents(request.Code, currentEnabled);
                if (dependents.Count > 0)
                {
                    throw new CapabilityMutationException(
                        StatusCodes.Status409Conflict,
                        "capability-has-dependents",
                        $"Cannot disable '{request.Code}' — these enabled capabilities depend on it: {string.Join(", ", dependents)}.",
                        new Dictionary<string, object?>
                        {
                            ["capability"] = request.Code,
                            ["dependents"] = dependents,
                        });
                }
            }
        }

        // 3. Commit. Idempotent toggles still emit an audit row + broadcast so
        //    cross-tab sessions can confirm the click landed (same posture as
        //    BiApiKeyRevoked in Phase 3 / WU-04).
        row.Enabled = toState;
        await db.SaveChangesAsync(cancellationToken);

        // 4. Refresh the snapshot so the very next request reflects the new
        //    state (the gate middleware reads this dictionary).
        await snapshots.RefreshAsync(cancellationToken);

        // 5. Audit row with richer content (Phase C — actor, before/after, reason).
        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            code = row.Code,
            from = fromState,
            to = toState,
            before = new { enabled = fromState },
            after = new { enabled = toState },
            reason = request.Reason,
            actorUserId = actorId,
        });
        var action = toState ? CapabilityAuditEvents.Enabled : CapabilityAuditEvents.Disabled;
        await auditWriter.WriteAsync(
            action: action,
            userId: actorId,
            entityType: CapabilityAuditEvents.EntityType,
            entityId: row.Id,
            details: details,
            ct: cancellationToken);

        // 6. Broadcast (4D §4.4 / 4D-decisions-log #3).
        await notificationHub.Clients.All.SendAsync(
            "capabilityChanged",
            new { capabilityId = row.Code, enabled = toState },
            cancellationToken);

        // 7. Re-read so the response carries the bumped Version (the descriptor
        //    contract for the next If-Match round-trip).
        var refreshed = await db.Capabilities
            .AsNoTracking()
            .FirstAsync(c => c.Id == row.Id, cancellationToken);

        return new CapabilityDescriptorEntry(
            Id: refreshed.Code,
            Code: refreshed.Code,
            Area: refreshed.Area,
            Name: refreshed.Name,
            Description: refreshed.Description,
            Enabled: refreshed.Enabled,
            IsDefaultOn: refreshed.IsDefaultOn,
            RequiresRoles: refreshed.RequiresRoles,
            Version: refreshed.Version,
            ETag: $"W/\"{refreshed.Version}\"",
            ConfigVersion: null,
            ConfigETag: null,
            ConfigId: null,
            Dependencies: BuildDeps(refreshed.Code),
            Mutexes: BuildMutexes(refreshed.Code));
    }

    private static IReadOnlyList<string> BuildDeps(string code)
    {
        var list = new List<string>();
        foreach (var edge in CapabilityCatalogRelations.Dependencies)
            if (edge.From == code) list.Add(edge.To);
        return list;
    }

    private static IReadOnlyList<string> BuildMutexes(string code)
    {
        var list = new List<string>();
        foreach (var edge in CapabilityCatalogRelations.Mutexes)
        {
            if (edge.From == code) list.Add(edge.To);
            else if (edge.To == code) list.Add(edge.From);
        }
        return list;
    }
}
