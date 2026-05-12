namespace Forge.Core.Models;

public record UpdateUserPreferencesRequestModel(
    Dictionary<string, object?> Preferences);
