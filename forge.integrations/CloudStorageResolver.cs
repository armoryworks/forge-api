using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Pro Services rollout — DI-backed resolver for cloud-storage providers.
/// Wraps the set of registered <see cref="ICloudStorageIntegrationService"/>
/// implementations (mock + real Google Drive / OneDrive / Dropbox) and
/// picks by provider code or returns the default for the install.
///
/// <para>Follows the MultiCarrierShippingService pattern: register each
/// provider as <see cref="ICloudStorageIntegrationService"/> in DI, then
/// inject <c>IEnumerable&lt;ICloudStorageIntegrationService&gt;</c> here
/// to enumerate them.</para>
///
/// <para>The mock provider is always registered. Real providers register
/// only when their options section (GoogleDrive / OneDrive / Dropbox)
/// has populated credentials — see Program.cs.</para>
/// </summary>
public class CloudStorageResolver : ICloudStorageResolver
{
    private readonly Dictionary<string, ICloudStorageIntegrationService> _byCode;
    private readonly ICloudStorageIntegrationService _default;
    private readonly ILogger<CloudStorageResolver> _logger;

    public CloudStorageResolver(
        IEnumerable<ICloudStorageIntegrationService> services,
        ILogger<CloudStorageResolver> logger)
    {
        _logger = logger;
        var list = services.ToList();
        _byCode = list.ToDictionary(s => s.ProviderCode, StringComparer.OrdinalIgnoreCase);

        // Default: the first non-mock service if any real provider is
        // registered, otherwise the mock. Lookup-order in DI registration
        // controls which "first real" wins on a hybrid install — fine for
        // current callers (the admin connection flow asks explicitly by
        // code; the default-fallback only fires before any provider is
        // explicitly selected).
        var realProvider = list.FirstOrDefault(s =>
            !string.Equals(s.ProviderCode, "mock", StringComparison.OrdinalIgnoreCase));
        _default = realProvider ?? _byCode["mock"];

        _logger.LogInformation("CloudStorageResolver registered: {Codes}; default = {Default}",
            string.Join(", ", _byCode.Keys), _default.ProviderCode);
    }

    public ICloudStorageIntegrationService? ResolveByCode(string providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return null;
        return _byCode.TryGetValue(providerCode, out var svc) ? svc : null;
    }

    public ICloudStorageIntegrationService ResolveDefault() => _default;

    public IReadOnlySet<string> RegisteredProviderCodes =>
        _byCode.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
