using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Resolves UI-entered carrier credentials from the active Carrier row, decrypting the secret. Registered
/// as a singleton (the carrier adapters are singletons); each call opens a short-lived scope for the
/// scoped AppDbContext + decryption service, so the lookup is always fresh — no cache to invalidate. The
/// lookup is a single indexed read keyed on integration_service_id, so the sync query is cheap.
/// </summary>
public class CarrierCredentialProvider(IServiceScopeFactory scopeFactory) : ICarrierCredentialProvider
{
    public CarrierCredentials? Resolve(string integrationServiceId)
    {
        if (string.IsNullOrWhiteSpace(integrationServiceId)) return null;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<ITokenEncryptionService>();

        var carrier = db.Carriers.AsNoTracking().FirstOrDefault(c =>
            c.IsActive
            && c.IntegrationServiceId == integrationServiceId
            && c.CredentialClientId != null
            && c.CredentialSecret != null);

        if (carrier is null) return null;

        string secret;
        try
        {
            secret = encryption.Decrypt(carrier.CredentialSecret!);
        }
        catch
        {
            // Stored ciphertext can't be decrypted (rotated key / corrupt) — fall back to IOptions rather
            // than throwing on every shipping call.
            return null;
        }

        return new CarrierCredentials(
            carrier.CredentialClientId!, secret, carrier.CredentialAccountNumber,
            string.IsNullOrWhiteSpace(carrier.CredentialEnvironment) ? "sandbox" : carrier.CredentialEnvironment);
    }
}
