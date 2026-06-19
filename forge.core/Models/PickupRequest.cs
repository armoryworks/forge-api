namespace Forge.Core.Models;

/// <summary>
/// A request to schedule a carrier courier pickup. Provider-agnostic — each carrier adapter maps it to
/// that carrier's pickup API. <see cref="PickupAddress"/> is where the courier collects (the shipping
/// origin); the ready/close times bound the pickup window for that day.
/// </summary>
public record PickupRequest(
    ShippingAddress PickupAddress,
    DateTimeOffset ReadyTime,
    DateTimeOffset CloseTime,
    int PackageCount,
    decimal TotalWeightLbs,
    string? Instructions);
