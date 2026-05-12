namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves clock event type definitions from reference data.
/// Cached in-memory for performance — types rarely change at runtime.
/// </summary>
public interface IClockEventTypeService
{
    Task<List<ClockEventTypeDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<ClockEventTypeDefinition?> GetByCodeAsync(string code, CancellationToken ct = default);
    void InvalidateCache();
}

public record ClockEventTypeDefinition(
    string Code,
    string Label,
    string StatusMapping,
    string? OppositeCode,
    string Category,
    bool CountsAsActive,
    bool IsMismatchable,
    string Icon,
    string Color);
