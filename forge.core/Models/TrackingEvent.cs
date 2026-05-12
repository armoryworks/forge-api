namespace Forge.Core.Models;

public record TrackingEvent(
    DateTimeOffset Timestamp,
    string Location,
    string Description);
