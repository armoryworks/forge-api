namespace Forge.Core.Models;

/// <summary>Result of a carrier connection test (a live rate-shop probe against the carrier's API).</summary>
public record CarrierTestResultModel(bool Success, string Message);
