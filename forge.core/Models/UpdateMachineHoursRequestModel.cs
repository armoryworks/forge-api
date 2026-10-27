namespace Forge.Core.Models;

public record UpdateMachineHoursRequestModel(decimal CurrentHours, DateTimeOffset? ReadingAt = null);
