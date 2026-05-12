namespace Forge.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.6) — per-preset default folder
/// layout for cloud-storage integration (per D9). Used by the dual-path
/// folder auto-create flow (per D2) when an entity is created with
/// <c>CAP-EXT-CLOUD-STORAGE</c> enabled.
///
/// Apply-preset writes the suggestions into <c>folder_map_suggestions</c>.
/// The actual provider-side folder creation happens lazily when the
/// triggering entity is created — the bundle is configuration, not data.
/// </summary>
public sealed record FolderMapBundle(
    IReadOnlyList<FolderMapSuggestion> Suggestions);

/// <summary>One folder-layout suggestion for an entity type.</summary>
/// <param name="EntityType">Target entity type, e.g. <c>"Job"</c>, <c>"Customer"</c>.</param>
/// <param name="PathTemplate">
/// Folder path template with substitution tokens. Supported tokens:
/// <c>{Customer}</c>, <c>{Project}</c>, <c>{Job}</c>, <c>{Year}</c>,
/// <c>{Month}</c>, <c>{Quarter}</c>, <c>{EngagementType}</c>.
/// </param>
/// <param name="SubfolderNames">Subfolders to create under the parent path.</param>
/// <param name="AutoCreateOnEntityCreate">
/// If true, the folder is created (dual-path: sync best-effort + outbox
/// fallback per D2) when the entity is created. Default true.
/// </param>
public sealed record FolderMapSuggestion(
    string EntityType,
    string PathTemplate,
    IReadOnlyList<string> SubfolderNames,
    bool AutoCreateOnEntityCreate = true);
