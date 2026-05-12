using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MachineConnection : BaseAuditableEntity
{
    public int WorkCenterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OpcUaEndpoint { get; set; } = string.Empty;
    public string? SecurityPolicy { get; set; }
    public string? AuthType { get; set; }
    public string? EncryptedCredentials { get; set; }
    public MachineConnectionStatus Status { get; set; } = MachineConnectionStatus.Disconnected;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public string? LastError { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public bool IsActive { get; set; } = true;

    public WorkCenter WorkCenter { get; set; } = null!;
    public ICollection<MachineTag> Tags { get; set; } = [];
}
