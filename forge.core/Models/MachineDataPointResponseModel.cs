
namespace Forge.Core.Models;

public record MachineDataPointResponseModel
{
    public int TagId { get; init; }
    public string TagName { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string? Unit { get; init; }
    public MachineDataQuality Quality { get; init; }
}
