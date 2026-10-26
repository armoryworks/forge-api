namespace Forge.Core.Models;

/// <summary>
/// Where a carrier collects shipments: the default company location, and the IANA time zone of the plant at
/// that location (null when none is recorded, in which case the client uses its own zone).
/// </summary>
public record PickupOriginResponseModel(string? LocationName, string? TimeZone);
