namespace Forge.Core.Models;

public record AccountingProviderInfo(
    string Id,
    string Name,
    string Description,
    string Icon,
    bool RequiresOAuth,
    bool IsConfigured);
