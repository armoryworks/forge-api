using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Federated read-only registry of every credential / connection an install
/// has issued or accepted. The native sources keep their own tables and
/// management surfaces; this layer answers "what's connected to this
/// install" in one query so an admin can see usage at a glance.
///
/// Aggregates: BI keys, system API keys, EDI trading partners, QuickBooks
/// OAuth, communication sync configs, cloud-storage links. The implementation
/// is read-only — mutations are routed back to the native surfaces via the
/// <c>ManageRoute</c> deep-link on each row.
/// </summary>
public interface IConnectionsRegistry
{
    Task<List<IntegrationRecordResponseModel>> ListAsync(CancellationToken ct);
}
