namespace Forge.Core.Models;

public record ShiftWorkCenterModel(int WorkCenterId, int DaysOfWeek, string? WorkCenterName = null);
