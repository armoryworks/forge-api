using Forge.Core.Enums;

namespace Forge.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.3) — per-preset track-type bundle.
/// Apply-preset upserts the contained track types + their stages into
/// <c>track_types</c> and <c>job_stages</c>, honoring the conflict policy.
///
/// PRESET-08 (Pro Services) seeds an Engagement track with service-flavored
/// stages (Proposal → Won → Discovery → Active → Review → Delivered →
/// Invoiced → Paid). PRESET-04 (Production Manufacturer) seeds the
/// existing manufacturing track types (Production / R&amp;D / Maintenance)
/// — refactored out of <c>SeedData.Essential</c> in a later task.
/// </summary>
public sealed record TrackTypeBundle(
    IReadOnlyList<TrackTypeSeed> TrackTypes,
    TrackTypeConflictPolicy ConflictPolicy = TrackTypeConflictPolicy.UpsertByCode);

/// <summary>One track type with its ordered stages.</summary>
/// <param name="Code">Stable code, e.g. <c>"engagement"</c>.</param>
/// <param name="Name">Human display name.</param>
/// <param name="SortOrder">Display ordering across track types.</param>
/// <param name="IsDefault">True if this track is the default when creating a Job.</param>
/// <param name="IsShopFloor">Track-level shop-floor flag (per-stage override below).</param>
/// <param name="Stages">Ordered stages for the track type.</param>
public sealed record TrackTypeSeed(
    string Code,
    string Name,
    int SortOrder,
    bool IsDefault,
    bool IsShopFloor,
    IReadOnlyList<JobStageSeed> Stages);

/// <summary>One kanban stage on a track type.</summary>
/// <param name="Code">Stable code within the track type, e.g. <c>"proposal"</c>.</param>
/// <param name="Name">Human display name.</param>
/// <param name="SortOrder">Display ordering across stages.</param>
/// <param name="Color">Hex color for the stage indicator.</param>
/// <param name="IsShopFloor">If true, this stage shows on the shop-floor display.</param>
/// <param name="IsIrreversible">If true, cards in this stage can't move backward.</param>
/// <param name="AccountingDocumentType">Optional pairing with an accounting document (Estimate / SalesOrder / Invoice / Payment).</param>
/// <param name="WipLimit">Optional WIP limit for the column.</param>
public sealed record JobStageSeed(
    string Code,
    string Name,
    int SortOrder,
    string Color = "#94a3b8",
    bool IsShopFloor = false,
    bool IsIrreversible = false,
    AccountingDocumentType? AccountingDocumentType = null,
    int? WipLimit = null);

/// <summary>How apply-preset handles track types that already exist.</summary>
public enum TrackTypeConflictPolicy
{
    /// <summary>Add if track-type's code is missing; leave existing track types untouched (default).</summary>
    UpsertByCode,
    /// <summary>Only add new track types; never modify or delete existing ones.</summary>
    AddOnly,
    /// <summary>Full replacement — dangerous; only valid for first-time apply on an empty install.</summary>
    Replace
}
