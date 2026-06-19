namespace Forge.Core.Models;

/// <summary>
/// A carrier's confirmation of a scheduled pickup. <see cref="ConfirmationNumber"/> is the carrier's
/// reference (PRP / confirmation code) — persisted on the shipment so the pickup can be displayed and cancelled.
/// </summary>
public record PickupConfirmation(
    string ConfirmationNumber,
    DateTimeOffset ScheduledDate,
    string CarrierName);
