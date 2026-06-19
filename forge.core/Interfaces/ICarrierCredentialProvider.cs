using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves a carrier's UI-entered, encrypted credentials by its integration service id ("ups", "fedex",
/// "usps", "dhl"). Returns null when none are stored, so the adapter falls back to IOptions (env/appsettings).
/// Implementations resolve synchronously (cheap indexed lookup) so a carrier adapter's sync
/// <c>IsConfigured</c> can account for DB-stored credentials.
/// </summary>
public interface ICarrierCredentialProvider
{
    CarrierCredentials? Resolve(string integrationServiceId);
}
