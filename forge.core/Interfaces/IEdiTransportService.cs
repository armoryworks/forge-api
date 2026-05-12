using Forge.Core.Enums;

namespace Forge.Core.Interfaces;

public interface IEdiTransportService
{
    EdiTransportMethod Method { get; }
    Task SendAsync(string payload, string connectionConfig, CancellationToken ct);
    Task<IReadOnlyList<string>> PollAsync(string connectionConfig, CancellationToken ct);
    Task<bool> TestConnectionAsync(string connectionConfig, CancellationToken ct);
}
