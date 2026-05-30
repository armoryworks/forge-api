using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Read-only federated registry of every credential / connection an install
/// has issued or accepted (BI keys, system API keys, EDI trading partners,
/// QuickBooks OAuth, communications sync, cloud-storage links).
///
/// Sits parallel to the native admin surfaces — it does NOT mutate. Each
/// returned row carries a <c>manageRoute</c> client-side path so the UI
/// deep-links to the native page for any operation (issue / revoke / etc.).
///
/// Gated <c>Admin</c>-only by design: the listing exposes the presence,
/// owner, and last-used timestamps of every credential in the install,
/// which is sensitive even though no plaintext is returned.
/// </summary>
[ApiController]
[Route("api/v1/admin/connections")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-IDEN-AUTH-API-KEYS")]
public class ConnectionsController(IConnectionsRegistry registry) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<IntegrationRecordResponseModel>>> List(CancellationToken ct)
        => Ok(await registry.ListAsync(ct));
}
