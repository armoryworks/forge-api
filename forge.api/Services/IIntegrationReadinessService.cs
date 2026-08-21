using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>Computes integration readiness by joining the descriptor catalog with
/// stored settings (configured?), the capability snapshot (needed?), and the
/// environment posture (mock vs real).</summary>
public interface IIntegrationReadinessService
{
    Task<IntegrationReadinessReport> BuildAsync(CancellationToken ct = default);
}
