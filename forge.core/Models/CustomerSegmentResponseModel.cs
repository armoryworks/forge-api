namespace Forge.Core.Models;

/// <summary>C3: a persisted customer segment (saved named filter).</summary>
public record CustomerSegmentResponseModel(
    int Id,
    string Name,
    string? Description,
    string? FilterCriteria,
    bool IsActive,
    DateTimeOffset CreatedAt);
