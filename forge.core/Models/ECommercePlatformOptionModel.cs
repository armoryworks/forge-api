using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One selectable platform, and whether a connector for it actually exists.
///
/// <para>Surfaced so the admin screen can offer the full enum while being
/// honest about which entries can be polled today. Letting someone configure
/// credentials for a platform with no connector, and only discovering that when
/// the first import silently returns nothing, is the failure this prevents.</para>
/// </summary>
public record ECommercePlatformOptionModel
{
    public ECommercePlatform Platform { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>True when a connector is registered and the platform can be polled.</summary>
    public bool IsSupported { get; init; }

    /// <summary>True for platforms where the marketplace is the tax facilitator and pays out on a settlement cycle.</summary>
    public bool IsMarketplace { get; init; }

    /// <summary>Why it cannot be used yet, when <see cref="IsSupported"/> is false. Null otherwise.</summary>
    public string? UnavailableReason { get; init; }
}
