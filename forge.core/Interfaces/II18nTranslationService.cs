using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Machine-translates short UI label strings between configured languages.
/// Backed by the self-hosted AI module (<see cref="IAiService"/>); implementations
/// must degrade gracefully — report <see cref="Enums.I18nTranslationOutcome.Unavailable"/>
/// when no reply could be obtained (service down or unavailable) so callers can flag
/// the row as pending, and <see cref="Enums.I18nTranslationOutcome.Rejected"/> when the
/// reply is unusable as a label so callers stop asking for it again.
/// </summary>
public interface II18nTranslationService
{
    Task<I18nTranslationResult> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct);
}
