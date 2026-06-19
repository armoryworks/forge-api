namespace Forge.Core.Models;

/// <summary>
/// Body for POST /shipments/{id}/pickup. All fields optional: ready/close default to a same-day business
/// window when omitted. The pickup address, carrier, and package details are derived from the shipment.
/// </summary>
public record SchedulePickupRequestModel(
    DateTimeOffset? ReadyTime = null,
    DateTimeOffset? CloseTime = null,
    string? Instructions = null);
