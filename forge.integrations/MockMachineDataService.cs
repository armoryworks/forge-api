using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockMachineDataService : IMachineDataService
{
    private readonly ILogger<MockMachineDataService> _logger;

    public MockMachineDataService(ILogger<MockMachineDataService> logger)
    {
        _logger = logger;
    }

    public Task ConnectAsync(int connectionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockMachineData] Connect to connection {ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(int connectionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockMachineData] Disconnect from connection {ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    public Task<MachineDataPoint?> GetLatestValueAsync(int tagId, CancellationToken ct)
    {
        _logger.LogInformation("[MockMachineData] GetLatestValue for tag {TagId}", tagId);

        var random = new Random();
        var point = new MachineDataPoint
        {
            TagId = tagId,
            WorkCenterId = 1,
            Value = random.Next(100, 5000).ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Quality = MachineDataQuality.Good,
        };

        return Task.FromResult<MachineDataPoint?>(point);
    }

    public Task<IReadOnlyList<MachineDataPoint>> GetHistoryAsync(int tagId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        _logger.LogInformation("[MockMachineData] GetHistory for tag {TagId} from {From} to {To}", tagId, from, to);

        var random = new Random();
        var points = new List<MachineDataPoint>();
        var current = from;
        var interval = TimeSpan.FromSeconds(10);

        while (current <= to && points.Count < 1000)
        {
            points.Add(new MachineDataPoint
            {
                TagId = tagId,
                WorkCenterId = 1,
                Value = random.Next(100, 5000).ToString(),
                Timestamp = current,
                Quality = MachineDataQuality.Good,
            });
            current = current.Add(interval);
        }

        return Task.FromResult<IReadOnlyList<MachineDataPoint>>(points);
    }

    public Task<bool> TestConnectionAsync(int connectionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockMachineData] TestConnection for connection {ConnectionId}", connectionId);
        return Task.FromResult(true);
    }
}
