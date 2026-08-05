namespace Forge.Core.Interfaces;

/// <summary>
/// Machine-translates short UI label strings between configured languages.
/// Backed by the self-hosted AI module (<see cref="IAiService"/>); implementations
/// must degrade gracefully — return null when no translation could be produced
/// (service down, unavailable, or unusable output) so callers can flag the row
/// as pending instead of failing the save.
/// </summary>
public interface II18nTranslationService
{
    Task<string?> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct);
}
