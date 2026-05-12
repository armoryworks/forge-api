using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

/// <summary>
/// Pro Services rollout (Artifact 4 / D2 / D9) — auto-create the
/// cloud-storage folder for an entity at creation time.
///
/// <para>Behavior:</para>
/// <list type="number">
///   <item>Look up the FolderMapBundle (from <c>system_settings[cloud_storage.folder_map]</c>).
///         Find the suggestion matching <paramref name="entityType"/>.
///         If none, return null (no folder configured for this type).</item>
///   <item>Resolve the suggestion's PathTemplate via <see cref="IFolderPathResolver"/>.</item>
///   <item>Resolve the default cloud provider via <see cref="ICloudStorageResolver"/>.</item>
///   <item><b>Sync best-effort:</b> call <c>CreateFolderAsync</c> with
///         <c>EnsureExists=true</c>. Persist an <see cref="EntityCloudLink"/>
///         row with <c>CreatedVia="auto_create"</c> and return it.</item>
///   <item><b>Outbox fallback (per D2):</b> if the sync call throws, log
///         the failure and skip persistence. (Full outbox-retry wiring is
///         a Phase 3a follow-up; today we degrade gracefully and the
///         user can manually link a folder later via admin UI.)</item>
/// </list>
///
/// <para>Callers (entity create handlers) invoke this AFTER the entity
/// is persisted (so EntityCloudLink.EntityId is real). When
/// <c>CAP-EXT-CLOUD-STORAGE</c> is disabled, the implementation returns
/// null immediately without doing any work.</para>
/// </summary>
public interface ICloudFolderAutoCreator
{
    /// <summary>
    /// Auto-create a folder for the given entity. Returns the persisted
    /// <see cref="EntityCloudLink"/> on success; null when no folder map
    /// suggestion matches the entity type, no provider is configured,
    /// or the operation fails (the failure is logged).
    /// </summary>
    /// <param name="entityType">e.g. <c>"Customer"</c>, <c>"Job"</c>, <c>"Deliverable"</c>.</param>
    /// <param name="entityId">Primary key of the just-created entity row.</param>
    /// <param name="tokenContext">Values for path-template tokens (<c>{Customer}</c>, <c>{Job}</c>, etc.).</param>
    Task<EntityCloudLink?> AutoCreateAsync(
        string entityType,
        int entityId,
        IReadOnlyDictionary<string, string> tokenContext,
        CancellationToken ct);
}
