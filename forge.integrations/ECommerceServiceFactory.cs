using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <inheritdoc cref="IECommerceServiceFactory"/>
public class ECommerceServiceFactory : IECommerceServiceFactory
{
    private readonly Dictionary<ECommercePlatform, IECommerceService> _byPlatform;

    /// <summary>
    /// Built from every <see cref="IECommerceService"/> registered in DI. A
    /// platform registered twice is a configuration error rather than a
    /// last-one-wins silent override, so it throws at construction — which
    /// happens at startup, not mid-import.
    /// </summary>
    public ECommerceServiceFactory(IEnumerable<IECommerceService> services)
    {
        _byPlatform = [];
        foreach (var service in services)
        {
            if (!_byPlatform.TryAdd(service.Platform, service))
            {
                throw new InvalidOperationException(
                    $"Two IECommerceService implementations are registered for {service.Platform}: " +
                    $"{_byPlatform[service.Platform].GetType().Name} and {service.GetType().Name}. " +
                    "Exactly one connector per platform.");
            }
        }
    }

    public IReadOnlyList<ECommercePlatform> SupportedPlatforms =>
        _byPlatform.Keys.OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();

    public bool IsSupported(ECommercePlatform platform) => _byPlatform.ContainsKey(platform);

    public IECommerceService For(ECommercePlatform platform)
    {
        if (_byPlatform.TryGetValue(platform, out var service))
            return service;

        if (platform == ECommercePlatform.Manual)
        {
            throw new NotSupportedException(
                "The Manual platform has no connector by design — its orders are keyed in or " +
                "loaded from a file rather than polled.");
        }

        throw new NotSupportedException(
            $"No connector is registered for {platform}. Registered platforms: " +
            $"{string.Join(", ", SupportedPlatforms)}.");
    }
}
