namespace Forge.Core.Models;

public record CreateControlledDocumentRequestModel(
    string Title,
    string? Description,
    string Category,
    int ReviewIntervalDays);
