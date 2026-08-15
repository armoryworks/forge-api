namespace Forge.Core.Models;

/// <summary>
/// Knobs for a clean-rebuild import. <see cref="ExcludePatterns"/> are shell-style globs over
/// <c>schema.table</c> (a pattern without a dot also matches the bare table name) — excluded
/// tables neither truncate nor load. <see cref="PurgeSoftDeleted"/> deletes rows with a non-null
/// <c>deleted_at</c> from every loaded table after the load — the app-level notion of garbage.
/// </summary>
public record DatabaseImportOptionsModel(
    List<string> ExcludePatterns,
    bool PurgeSoftDeleted,
    bool AllowFkOrphans);
