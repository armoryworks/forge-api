using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record GetQuoteSettingsQuery : IRequest<QuoteSettingsResponseModel>;

/// <summary>
/// S4a — sales-side quote/order settings. Mirrors the AutoPo settings
/// pattern (Features/AutoPo/GetAutoPoSettings.cs): each setting is a
/// SystemSetting row read through <see cref="ISystemSettingRepository"/>
/// with a hard-coded default when absent.
/// </summary>
public class GetQuoteSettingsHandler(ISystemSettingRepository settings)
    : IRequestHandler<GetQuoteSettingsQuery, QuoteSettingsResponseModel>
{
    public async Task<QuoteSettingsResponseModel> Handle(GetQuoteSettingsQuery request, CancellationToken ct)
    {
        var autoCustomerPoEnabled = await GetBoolSettingAsync("sales:auto_customer_po_enabled", false, ct);

        return new QuoteSettingsResponseModel(autoCustomerPoEnabled);
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(key, ct);
        return setting is not null && bool.TryParse(setting.Value, out var val) ? val : defaultValue;
    }
}
