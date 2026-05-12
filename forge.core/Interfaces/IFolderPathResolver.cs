namespace Forge.Core.Interfaces;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.6 / D9) — resolves folder-path
/// templates by substituting tokens like <c>{Customer}</c>, <c>{Job}</c>,
/// <c>{Year}</c> with caller-supplied values.
///
/// <para>Used by the cloud-folder auto-create flow when an entity is
/// created on an install with <c>CAP-EXT-CLOUD-STORAGE</c> enabled. The
/// FolderMapBundle's PathTemplate gets resolved here before being passed
/// to <see cref="ICloudStorageIntegrationService.CreateFolderAsync"/>.</para>
///
/// <para>Standard tokens (resolved automatically when not provided in
/// the context): <c>{Year}</c>, <c>{Month}</c>, <c>{Quarter}</c>.
/// Entity tokens (caller-supplied): <c>{Customer}</c>, <c>{Job}</c>,
/// <c>{Project}</c>, <c>{Deliverable}</c>, <c>{EngagementType}</c>.</para>
///
/// <para>Substitution is case-insensitive on the token name. Unmatched
/// tokens stay literal in the output (no exceptions). Slashes inside a
/// token value are sanitized to dashes so a customer name like
/// <c>"ACME / Inc"</c> doesn't accidentally split path segments.</para>
/// </summary>
public interface IFolderPathResolver
{
    /// <summary>
    /// Substitute tokens in <paramref name="template"/> using the supplied
    /// context. Standard date tokens are filled from <c>DateTimeOffset.UtcNow</c>
    /// when not present in the context.
    /// </summary>
    string Resolve(string template, IReadOnlyDictionary<string, string>? context = null);
}
