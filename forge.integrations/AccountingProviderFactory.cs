using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class AccountingProviderFactory : IAccountingProviderFactory
{
    private readonly Dictionary<string, IAccountingService> _providers;
    private readonly ISystemSettingRepository _settings;
    private readonly ILogger<AccountingProviderFactory> _logger;

    private const string ActiveProviderKey = "accounting_provider";

    public AccountingProviderFactory(
        IEnumerable<IAccountingService> providers,
        ISystemSettingRepository settings,
        ILogger<AccountingProviderFactory> logger)
    {
        _providers = providers.ToDictionary(p => p.ProviderId, p => p);
        _settings = settings;
        _logger = logger;
    }

    public async Task<IAccountingService?> GetActiveProviderAsync(CancellationToken ct)
    {
        var providerId = await GetActiveProviderIdAsync(ct);
        if (string.IsNullOrEmpty(providerId))
            return null;

        if (_providers.TryGetValue(providerId, out var provider))
            return provider;

        _logger.LogWarning("Configured accounting provider '{ProviderId}' not found in registered providers", providerId);
        return null;
    }

    public IAccountingService? GetProvider(string providerId)
    {
        _providers.TryGetValue(providerId, out var provider);
        return provider;
    }

    public async Task<List<AccountingProviderInfo>> GetAvailableProvidersAsync(CancellationToken ct)
    {
        var activeId = await GetActiveProviderIdAsync(ct);

        var infos = new List<AccountingProviderInfo>
        {
            // AccountingProviderInfo(Id, Name, Description, Icon, RequiresOAuth, IsConfigured)
            // IsConfigured here means "is this provider currently active/selected"
            new("local", "Local (Standalone)", "All data stored locally — no external accounting sync required", "storage", false, activeId == "local" || string.IsNullOrEmpty(activeId)),
            new("quickbooks", "QuickBooks Online", "Accounting, invoicing, and payment sync via Intuit QuickBooks", "account_balance", true, activeId == "quickbooks"),
            new("xero", "Xero", "Cloud accounting with multi-currency and project tracking", "account_balance_wallet", true, activeId == "xero"),
            new("freshbooks", "FreshBooks", "Small business invoicing and expense tracking", "receipt_long", true, activeId == "freshbooks"),
            new("sage", "Sage Business Cloud", "Enterprise accounting and financial management", "business", true, activeId == "sage"),
            new("netsuite", "NetSuite", "Enterprise ERP with accounting and inventory management", "corporate_fare", false, activeId == "netsuite"),
            new("wave", "Wave", "Free small business accounting (personal access token)", "waves", false, activeId == "wave"),
            new("zoho", "Zoho Books", "Zoho Books cloud accounting with inventory", "menu_book", true, activeId == "zoho"),
        };

        return infos;
    }

    public async Task SetActiveProviderAsync(string? providerId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(providerId) && !_providers.ContainsKey(providerId))
        {
            throw new InvalidOperationException($"Accounting provider '{providerId}' is not registered. Available: {string.Join(", ", _providers.Keys)}");
        }

        await _settings.UpsertAsync(ActiveProviderKey, providerId ?? string.Empty, "Active accounting provider ID", ct);
        await _settings.SaveChangesAsync(ct);

        _logger.LogInformation("Active accounting provider set to '{ProviderId}'", providerId ?? "(standalone)");
    }

    public async Task<string?> GetActiveProviderIdAsync(CancellationToken ct)
    {
        var setting = await _settings.FindByKeyAsync(ActiveProviderKey, ct);
        var value = setting?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
