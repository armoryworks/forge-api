namespace Forge.Core.Enums;

/// <summary>What one machine translation attempt of a UI label produced.</summary>
public enum I18nTranslationOutcome
{
    /// <summary>No reply was obtained (service down, timed out or empty reply); worth retrying later.</summary>
    Unavailable,
    /// <summary>A usable translation was produced.</summary>
    Translated,
    /// <summary>The service replied, but the reply does not read as a label; retrying would only repeat the paid call.</summary>
    Rejected,
}
