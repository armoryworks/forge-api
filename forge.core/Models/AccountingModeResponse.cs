namespace Forge.Core.Models;

public record AccountingModeResponse(
    bool IsConfigured,
    string? ProviderName,
    string? ProviderId);
