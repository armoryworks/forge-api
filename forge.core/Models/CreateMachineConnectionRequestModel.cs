namespace Forge.Core.Models;

public record CreateMachineConnectionRequestModel
{
    public int WorkCenterId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string OpcUaEndpoint { get; init; } = string.Empty;
    public string? SecurityPolicy { get; init; }
    public string? AuthType { get; init; }
    public string? Credentials { get; init; }
    public int PollIntervalMs { get; init; } = 1000;
    public List<CreateMachineTagRequestModel> Tags { get; init; } = [];
}
