using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAccountingProviderFactory
{
    /// <summary>
    /// Gets the currently active accounting provider based on system settings.
    /// Returns null when no provider is configured (standalone mode).
    /// </summary>
    Task<IAccountingService?> GetActiveProviderAsync(CancellationToken ct);

    /// <summary>
    /// Gets a specific provider by its ID (e.g., "quickbooks", "xero").
    /// </summary>
    IAccountingService? GetProvider(string providerId);

    /// <summary>
    /// Lists all registered providers with their configuration status.
    /// </summary>
    Task<List<AccountingProviderInfo>> GetAvailableProvidersAsync(CancellationToken ct);

    /// <summary>
    /// Sets the active provider. Pass null or empty string for standalone mode.
    /// </summary>
    Task SetActiveProviderAsync(string? providerId, CancellationToken ct);

    /// <summary>
    /// Gets the current active provider ID from system settings.
    /// Returns null when in standalone mode.
    /// </summary>
    Task<string?> GetActiveProviderIdAsync(CancellationToken ct);
}
