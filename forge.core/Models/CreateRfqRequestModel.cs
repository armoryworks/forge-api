namespace Forge.Core.Models;

public record CreateRfqRequestModel(
    int PartId,
    decimal Quantity,
    DateTimeOffset RequiredDate,
    string? Description,
    string? SpecialInstructions,
    DateTimeOffset? ResponseDeadline);
