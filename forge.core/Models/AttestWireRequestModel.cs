namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — wire attestation payload (optional bank confirmation reference).</summary>
public record AttestWireRequestModel(string? BankReference);
