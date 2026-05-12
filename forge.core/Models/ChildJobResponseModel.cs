namespace Forge.Core.Models;

public record ChildJobResponseModel(
    int Id,
    string JobNumber,
    string Title,
    string Stage,
    string? PartNumber,
    decimal? Quantity,
    DateTimeOffset CreatedAt);
