namespace Forge.Core.Models;

public record AutoPoSettingsResponseModel(
    bool Enabled,
    string DefaultMode,
    int BufferDays,
    bool NotifyChat,
    string Schedule);
