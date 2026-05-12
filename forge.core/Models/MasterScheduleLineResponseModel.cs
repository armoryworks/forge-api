namespace Forge.Core.Models;

public record MasterScheduleLineResponseModel(
    int Id,
    int MasterScheduleId,
    int PartId,
    string PartNumber,
    string? PartDescription,
    decimal Quantity,
    DateTimeOffset DueDate,
    string? Notes
);
