namespace Forge.Core.Models;

public record CustomerPoDocumentLineModel(
    int LineNumber,
    string Description,
    string? PartNumber,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);
