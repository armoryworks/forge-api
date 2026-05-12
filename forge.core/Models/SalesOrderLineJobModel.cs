namespace Forge.Core.Models;

public record SalesOrderLineJobModel(
    int Id,
    string JobNumber,
    string? Title,
    string? StageName,
    string? AssigneeName,
    string? Priority,
    DateTimeOffset? DueDate,
    bool IsArchived);
