namespace Forge.Core.Models;

/// <summary>
/// Body for PUT /carriers/{id}/credentials. The secret is write-only — it's encrypted server-side and
/// never returned. Environment is "sandbox" or "production".
/// </summary>
public record UpdateCarrierCredentialsRequestModel(
    string ClientId,
    string Secret,
    string? AccountNumber = null,
    string Environment = "sandbox");
