namespace Forge.Core.Enums;

/// <summary>
/// How a shipment on this carrier reaches Delivered. Configuration-driven per carrier and not
/// mutually exclusive across the fleet: a manual courier stays <see cref="Manual"/> while an
/// integrated carrier can <see cref="Poll"/> tracking or accept a <see cref="Webhook"/>. Poll and
/// Webhook are wired in later carrier-epic slices; today the value is stored and Manual is honored.
/// </summary>
public enum CarrierDeliveryUpdateMode
{
    Manual,
    Poll,
    Webhook,
}
