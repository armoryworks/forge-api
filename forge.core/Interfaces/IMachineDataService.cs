using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IMachineDataService
{
    Task ConnectAsync(int connectionId, CancellationToken ct);
    Task DisconnectAsync(int connectionId, CancellationToken ct);
    Task<MachineDataPoint?> GetLatestValueAsync(int tagId, CancellationToken ct);
    Task<IReadOnlyList<MachineDataPoint>> GetHistoryAsync(int tagId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<bool> TestConnectionAsync(int connectionId, CancellationToken ct);
}
