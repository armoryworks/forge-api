using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

/// <summary>
/// Pro Services rollout — resolves the right
/// <see cref="ICloudStorageIntegrationService"/> for a given install,
/// provider row, or entity link. Wraps the DI-registered set (mock +
/// real Drive / OneDrive / Dropbox impls) and picks by
/// <see cref="ICloudStorageIntegrationService.ProviderCode"/>.
///
/// <para>Per the D9 multi-provider design, an install can have multiple
/// active providers (hybrid storage). Callers that already know which
/// provider an entity binds to (e.g. via <see cref="EntityCloudLink.ProviderId"/>)
/// resolve via <see cref="ResolveByCode"/>. Callers that need "any
/// configured provider" use <see cref="ResolveDefault"/>.</para>
/// </summary>
public interface ICloudStorageResolver
{
    /// <summary>
    /// Resolve a service by its provider code (<c>"gdrive"</c>,
    /// <c>"onedrive"</c>, <c>"dropbox"</c>, or <c>"mock"</c>). Returns
    /// null when no matching service is registered.
    /// </summary>
    ICloudStorageIntegrationService? ResolveByCode(string providerCode);

    /// <summary>
    /// Resolve the default provider — the first non-mock service if any
    /// real provider is configured, otherwise the mock. Used when the
    /// caller doesn't yet have a CloudStorageProvider row to anchor on
    /// (e.g. the admin connection wizard before any provider is selected).
    /// </summary>
    ICloudStorageIntegrationService ResolveDefault();

    /// <summary>The set of provider codes currently registered in DI.</summary>
    IReadOnlySet<string> RegisteredProviderCodes { get; }
}
