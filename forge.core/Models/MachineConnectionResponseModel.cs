using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MachineConnectionResponseModel
{
    public int Id { get; init; }
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string OpcUaEndpoint { get; init; } = string.Empty;
    public string? SecurityPolicy { get; init; }
    public string? AuthType { get; init; }
    public MachineConnectionStatus Status { get; init; }
    public DateTimeOffset? LastConnectedAt { get; init; }
    public string? LastError { get; init; }
    public int PollIntervalMs { get; init; }
    public bool IsActive { get; init; }
    public int TagCount { get; init; }
    public List<MachineTagResponseModel> Tags { get; init; } = [];
}
