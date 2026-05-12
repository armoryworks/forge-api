using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.AutoPo;

public record GetAutoPoSettingsQuery : IRequest<AutoPoSettingsResponseModel>;

public class GetAutoPoSettingsHandler(ISystemSettingRepository settings) : IRequestHandler<GetAutoPoSettingsQuery, AutoPoSettingsResponseModel>
{
    public async Task<AutoPoSettingsResponseModel> Handle(GetAutoPoSettingsQuery request, CancellationToken ct)
    {
        var enabled = await GetBoolSettingAsync("inventory:auto_po_enabled", false, ct);
        var mode = await GetStringSettingAsync("inventory:auto_po_mode", "Draft", ct);
        var bufferDays = await GetIntSettingAsync("inventory:auto_po_buffer_days", 3, ct);
        var notifyChat = await GetBoolSettingAsync("inventory:auto_po_notify_chat", true, ct);

        return new AutoPoSettingsResponseModel(enabled, mode, bufferDays, notifyChat, "Daily at 6:00 AM UTC");
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(key, ct);
        return setting is not null && bool.TryParse(setting.Value, out var val) ? val : defaultValue;
    }

    private async Task<string> GetStringSettingAsync(string key, string defaultValue, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(key, ct);
        return setting?.Value ?? defaultValue;
    }

    private async Task<int> GetIntSettingAsync(string key, int defaultValue, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(key, ct);
        return setting is not null && int.TryParse(setting.Value, out var val) ? val : defaultValue;
    }
}
