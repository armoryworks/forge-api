namespace Forge.Core.Models;

public record UpdateMachineConnectionRequestModel
{
    public string Name { get; init; } = string.Empty;
    public string OpcUaEndpoint { get; init; } = string.Empty;
    public string? SecurityPolicy { get; init; }
    public string? AuthType { get; init; }
    public string? Credentials { get; init; }
    public int PollIntervalMs { get; init; } = 1000;
    public bool IsActive { get; init; } = true;
    public List<CreateMachineTagRequestModel> Tags { get; init; } = [];
}
