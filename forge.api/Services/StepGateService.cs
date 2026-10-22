using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Routing;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Enforces step gating on progress updates. Off never checks. Warn stops the update until the caller
/// acknowledges it. Block stops it unless the caller holds an override role, <c>CAP-MFG-GATE-OVERRIDE</c> is on,
/// and a reason is given. Starting a timer is never gated, so nothing here runs on timer start.
/// </summary>
public class StepGateService(
    AppDbContext db, ISettingsService settings, ICapabilitySnapshotProvider? capabilities = null) : IStepGateService
{
    public const string OverrideCapability = "CAP-MFG-GATE-OVERRIDE";

    public async Task<StepGatingMode> ModeAsync(int? partId, CancellationToken ct)
    {
        var partMode = partId is int id
            ? await db.Parts.AsNoTracking().Where(p => p.Id == id).Select(p => p.StepGating).FirstOrDefaultAsync(ct)
            : null;
        return ResolveMode(partMode, await settings.GetStringAsync(ShopFloorSettings.StepGatingKey, ct));
    }

    public async Task<StepGateOutcome> EnforceAsync(StepGateRequest request, CancellationToken ct)
    {
        var mode = await ModeAsync(request.Job.PartId, ct);
        if (mode == StepGatingMode.Off || request.Job.PartId is not int partId)
            return StepGateOutcome.None;

        var reported = request.Completed + request.Scrap;
        var grows = reported > request.PreviousCompleted + request.PreviousScrap;
        if (!grows && !request.MarkingComplete)
            return new StepGateOutcome(mode, false, null);

        var routing = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId)
            .Select(o => new RoutingStep(o.Id, o.StepNumber))
            .ToListAsync(ct);
        var stepById = routing.ToDictionary(s => s.OperationId, s => s.StepNumber);
        var effective = RoutingPredecessors.Effective(routing, await LoadLinksAsync(db, stepById.Keys, ct));
        var progress = await LoadProgressAsync(db, request.Job.Id, ct);
        var operationId = request.Operation.Id;

        StepGateViolationResponseModel? violation = null;
        string? message = null;
        if (grows)
        {
            var availability = StepGate.Available(operationId, effective, progress, request.JobQuantity);
            if (availability.Available is decimal available && reported > available)
            {
                var from = availability.LimitingOperationId is int limit ? stepById.GetValueOrDefault(limit) : (int?)null;
                violation = new StepGateViolationResponseModel(
                    mode, PredecessorGate.Quantity, from, available, reported, [], false);
                message = $"Op {request.Operation.StepNumber} would report {reported:0.##} pieces, but only "
                    + $"{available:0.##} are available from Op {from}.";
            }
        }

        if (violation is null && request.MarkingComplete)
        {
            var waiting = StepGate.WaitingOn(operationId, effective, progress)
                .Select(id => stepById.GetValueOrDefault(id))
                .OrderBy(s => s)
                .ToList();
            if (waiting.Count > 0)
            {
                violation = new StepGateViolationResponseModel(
                    mode, PredecessorGate.Complete, waiting[0], null, reported, waiting, false);
                message = $"Op {request.Operation.StepNumber} can't be marked done until "
                    + string.Join(", ", waiting.Select(s => $"Op {s}")) + " " + (waiting.Count == 1 ? "is" : "are") + " done.";
            }
        }

        if (violation is null)
            return new StepGateOutcome(mode, false, null);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (mode == StepGatingMode.Warn)
        {
            if (request.Acknowledge)
                return new StepGateOutcome(mode, true, reason);
            throw new StepGateException(message + " Confirm to continue.", violation with { CanOverride = true });
        }

        var mayOverride = request.UserMayOverride && (capabilities?.IsEnabled(OverrideCapability) ?? true);
        if (mayOverride && request.Acknowledge && reason is not null)
            return new StepGateOutcome(mode, true, reason);

        throw new StepGateException(
            message + (mayOverride
                ? " Give a reason to override."
                : " A manager or production manager can override it."),
            violation with { CanOverride = mayOverride });
    }

    public static StepGatingMode ResolveMode(StepGatingMode? partMode, string? setting)
        => partMode ?? (Enum.TryParse<StepGatingMode>(setting, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed) ? parsed : StepGatingMode.Off);

    public static async Task<List<RoutingLink>> LoadLinksAsync(
        AppDbContext db, IEnumerable<int> operationIds, CancellationToken ct)
    {
        var ids = operationIds.ToList();
        if (ids.Count == 0)
            return [];
        return await db.OperationPredecessors.AsNoTracking()
            .Where(l => ids.Contains(l.OperationId))
            .Select(l => new RoutingLink(l.OperationId, l.PredecessorOperationId, l.Gate))
            .ToListAsync(ct);
    }

    private static async Task<Dictionary<int, StepProgress>> LoadProgressAsync(AppDbContext db, int jobId, CancellationToken ct)
        => (await db.JobOperations.AsNoTracking()
                .Where(r => r.JobId == jobId && r.OperationId != null)
                .Select(r => new { OperationId = r.OperationId!.Value, r.Status, r.CompletedQuantity })
                .ToListAsync(ct))
            .GroupBy(r => r.OperationId)
            .ToDictionary(g => g.Key, g => new StepProgress(g.First().Status, g.First().CompletedQuantity));
}
