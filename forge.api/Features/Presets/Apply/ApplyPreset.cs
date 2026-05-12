using System.Text.Json;

using FluentValidation;
using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Capabilities.BulkToggle;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Api.Features.Presets.Models;
using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply;

/// <summary>
/// Phase 4 Phase-G — Apply a preset directly (without going through discovery).
/// Reuses the bulk-toggle substrate for atomicity / validation / audit /
/// SignalR broadcast, then writes a single <c>PresetApplied</c> system audit
/// row referencing the run.
///
/// Per 4F implementation decision #3 / open-question #3, re-applying a preset
/// when install state already matches is a NO-OP: zero deltas, zero
/// capability rows mutated, audit row still written but with deltaCount = 0
/// and an outcome of "no-op".
/// </summary>
public record ApplyPresetCommand(
    string PresetId,
    string? Reason,
    bool IsCustomOverride = false,
    IReadOnlyList<PresetCustomOverrideRequestItem>? CustomOverrides = null)
    : IRequest<PresetApplyResultResponseModel>;

public class ApplyPresetValidator : AbstractValidator<ApplyPresetCommand>
{
    public ApplyPresetValidator()
    {
        RuleFor(x => x.PresetId)
            .NotEmpty()
            .Matches("^PRESET-[A-Z0-9-]+$");
        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
        RuleForEach(x => x.CustomOverrides!).ChildRules(item =>
        {
            item.RuleFor(i => i.Code).NotEmpty().Matches("^CAP-[A-Z0-9-]+$");
        }).When(x => x.CustomOverrides is not null);
    }
}

public class ApplyPresetHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    IMediator mediator,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<ApplyPresetCommand, PresetApplyResultResponseModel>
{
    public async Task<PresetApplyResultResponseModel> Handle(
        ApplyPresetCommand request,
        CancellationToken cancellationToken)
    {
        var preset = PresetCatalog.FindById(request.PresetId)
            ?? throw new KeyNotFoundException($"Unknown preset id: {request.PresetId}");

        // Resolve the effective target set:
        //   • Standard preset → preset.EnabledCapabilities.
        //   • Custom WITHOUT overrides → catalog defaults.
        //   • Custom WITH overrides → catalog defaults + overrides applied
        //     (per 4B decision #5: Custom = catalog defaults, then user
        //     hand-picks).
        var targetSet = ResolveTargetSet(preset, request.IsCustomOverride, request.CustomOverrides);

        // Compute deltas vs current state.
        var snapshot = snapshots.Current;
        var current = snapshot.EnabledByCode;
        var deltas = new List<PresetCapabilityDeltaResponseModel>();
        foreach (var def in CapabilityCatalog.All)
        {
            var currentlyEnabled = current.TryGetValue(def.Code, out var v) && v;
            var willBeEnabled = targetSet.Contains(def.Code);
            if (currentlyEnabled != willBeEnabled)
            {
                deltas.Add(new PresetCapabilityDeltaResponseModel(
                    Code: def.Code,
                    Name: def.Name,
                    Area: def.Area,
                    CurrentlyEnabled: currentlyEnabled,
                    WillBeEnabled: willBeEnabled));
            }
        }
        deltas = [.. deltas.OrderBy(d => d.Code, StringComparer.Ordinal)];

        // No-op path (per 4F open-question #3): write the audit row anyway so
        // the install has a record of "the admin re-asserted preset X" even
        // when nothing changed.
        var noOp = deltas.Count == 0;

        if (!noOp)
        {
            var bulkItems = deltas
                .Select(d => new BulkToggleItem(d.Code, d.WillBeEnabled, IfMatch: null))
                .ToList();

            await mediator.Send(
                new BulkToggleCapabilitiesCommand(
                    Items: bulkItems,
                    Reason: BuildBulkReason(preset, request)),
                cancellationToken);
        }

        // Pro Services rollout (Artifact 5 §4) — bundle apply pipeline.
        // Each non-null bundle is staged on the same AppDbContext; one
        // SaveChangesAsync at the end commits all bundle changes in a
        // single transaction. Capability state was already committed by
        // BulkToggleCapabilitiesCommand above — bundles are idempotent so
        // a re-apply recovers from any partial-bundle failure.
        // Bundle apply is skipped on Custom (no bundles on PRESET-CUSTOM).
        var layerResults = new List<LayerApplyResult>();
        if (!preset.IsCustom)
        {
            if (preset.TerminologyBundle is { } tb)
            {
                layerResults.Add(await TerminologyBundleApplier.ApplyAsync(tb, db, preset.Id, cancellationToken));
            }
            if (preset.ReferenceDataBundle is { } rdb)
            {
                layerResults.Add(await ReferenceDataBundleApplier.ApplyAsync(rdb, db, preset.Id, cancellationToken));
            }
            if (preset.TrackTypeBundle is { } ttb)
            {
                layerResults.Add(await TrackTypeBundleApplier.ApplyAsync(ttb, db, preset.Id, cancellationToken));
            }
            if (preset.RoleBundle is { } rb)
            {
                layerResults.Add(await RoleBundleApplier.ApplyAsync(rb, db, preset.Id, cancellationToken));
            }
            if (preset.WorkflowDefinitionBundle is { } wdb)
            {
                layerResults.Add(await WorkflowDefinitionBundleApplier.ApplyAsync(wdb, db, preset.Id, cancellationToken));
            }
            if (preset.FolderMapBundle is { } fmb)
            {
                layerResults.Add(await FolderMapBundleApplier.ApplyAsync(fmb, db, preset.Id, cancellationToken));
            }
            // ReportVisibilityBundle / DashboardBundle appliers are
            // deferred until their backing storage lands (Phase 3a UI work).
            // When those land, append the same way here.

            if (layerResults.Count > 0 && layerResults.Any(r => r.TouchedCount > 0))
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        // Write the PresetApplied audit row (always, no-op or not).
        var actorId = db.CurrentUserId ?? 0;
        var auditDetails = JsonSerializer.Serialize(new
        {
            presetId = preset.Id,
            presetName = preset.Name,
            isCustom = preset.IsCustom,
            isCustomOverride = request.IsCustomOverride,
            customOverrideCount = request.CustomOverrides?.Count ?? 0,
            outcome = noOp && layerResults.All(r => r.TouchedCount == 0) ? "no-op" : "applied",
            deltaCount = deltas.Count,
            reason = request.Reason,
            applyPath = "preset-browser-direct",
            actorUserId = actorId,
            // Per-layer counts — empty list if preset carries no bundles.
            layerResults = layerResults.Select(r => new
            {
                layer = r.Layer,
                addedCount = r.AddedCount,
                updatedCount = r.UpdatedCount,
                skippedCount = r.SkippedCount,
                conflictedCount = r.ConflictedCount,
            }).ToList(),
        });
        await auditWriter.WriteAsync(
            action: CapabilityAuditEvents.PresetApplied,
            userId: actorId,
            entityType: CapabilityAuditEvents.EntityType,
            entityId: null,
            details: auditDetails,
            ct: cancellationToken);

        return new PresetApplyResultResponseModel(
            PresetId: preset.Id,
            PresetName: preset.Name,
            IsCustom: preset.IsCustom,
            NoOp: noOp,
            DeltaCount: deltas.Count,
            Applied: deltas,
            LayerResults: layerResults.Count == 0 ? null : layerResults
                .Select(r => new PresetBundleApplyResultResponseModel(
                    Layer: r.Layer,
                    AddedCount: r.AddedCount,
                    UpdatedCount: r.UpdatedCount,
                    SkippedCount: r.SkippedCount,
                    ConflictedCount: r.ConflictedCount))
                .ToList());
    }

    private static HashSet<string> ResolveTargetSet(
        PresetDefinition preset,
        bool isCustomOverride,
        IReadOnlyList<PresetCustomOverrideRequestItem>? overrides)
    {
        // Standard preset (non-custom): preset's enabled set.
        if (!preset.IsCustom && !isCustomOverride)
        {
            return new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);
        }

        // Custom (or custom-style apply against an override list): catalog
        // defaults baseline, overlaid with overrides.
        var set = new HashSet<string>(
            CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
            StringComparer.Ordinal);

        if (overrides is not null)
        {
            foreach (var ov in overrides)
            {
                if (ov.Enabled) set.Add(ov.Code);
                else set.Remove(ov.Code);
            }
        }

        return set;
    }

    private static string BuildBulkReason(PresetDefinition preset, ApplyPresetCommand request)
    {
        var prefix = preset.IsCustom || request.IsCustomOverride
            ? $"Custom apply"
            : $"Apply preset {preset.Id}";
        return string.IsNullOrWhiteSpace(request.Reason)
            ? prefix
            : $"{prefix}: {request.Reason}";
    }
}
