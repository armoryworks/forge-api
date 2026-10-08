namespace Forge.Core.Models;

public record KioskAvailableJobResponseModel(
    int JobId,
    string JobNumber,
    string Title,
    string? PartNumber,
    decimal Quantity,
    DateTimeOffset? DueDate,
    string PriorityName,
    string StageName,
    KioskNextOperationResponseModel? NextOperation);
