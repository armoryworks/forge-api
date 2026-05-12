using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockEdiTransportService : IEdiTransportService
{
    private readonly ILogger<MockEdiTransportService> _logger;

    public MockEdiTransportService(ILogger<MockEdiTransportService> logger)
    {
        _logger = logger;
    }

    public EdiTransportMethod Method => EdiTransportMethod.Manual;

    public Task SendAsync(string payload, string connectionConfig, CancellationToken ct)
    {
        _logger.LogInformation("[MockEdiTransport] Sending payload of {Size} bytes", payload.Length);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> PollAsync(string connectionConfig, CancellationToken ct)
    {
        _logger.LogInformation("[MockEdiTransport] Polling for inbound documents");
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task<bool> TestConnectionAsync(string connectionConfig, CancellationToken ct)
    {
        _logger.LogInformation("[MockEdiTransport] Testing connection");
        return Task.FromResult(true);
    }
}
