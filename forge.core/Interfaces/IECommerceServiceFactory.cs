using Forge.Core.Enums;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves the connector for a platform.
///
/// <para>Exists because an install genuinely runs several platforms at once —
/// an eBay store, an Etsy shop and a Shopify site are the normal case, not an
/// edge case. The previous design registered <see cref="IECommerceService"/>
/// once in DI with a <c>Platform</c> property, so the container could only ever
/// hold one connector and whichever registration won silently served every
/// channel.</para>
/// </summary>
public interface IECommerceServiceFactory
{
    /// <summary>
    /// The connector for <paramref name="platform"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// No connector is registered for the platform — either it is
    /// <see cref="ECommercePlatform.Manual"/> (which has no API by definition)
    /// or its connector has not been built yet.
    /// </exception>
    IECommerceService For(ECommercePlatform platform);

    /// <summary>True when <see cref="For"/> would succeed. Lets callers degrade instead of throwing.</summary>
    bool IsSupported(ECommercePlatform platform);

    /// <summary>Every platform with a registered connector, for admin UI that offers a choice.</summary>
    IReadOnlyList<ECommercePlatform> SupportedPlatforms { get; }
}
