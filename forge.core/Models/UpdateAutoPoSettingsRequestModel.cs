namespace Forge.Core.Models;

public record UpdateAutoPoSettingsRequestModel(
    bool? Enabled,
    string? DefaultMode,
    int? BufferDays,
    bool? NotifyChat);
