using System.Text.RegularExpressions;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Machine-translates UI label strings via the self-hosted AI module
/// (<see cref="IAiService"/> — Ollama in production, mock under MOCK_INTEGRATIONS).
/// Availability is probed before generating so a downed AI container costs one
/// fast check, and every failure path reports the translation unavailable — callers
/// flag the row as pending instead of failing the save (graceful degradation, same
/// contract as the rest of the AI surface). A reply that does not read as a label is
/// reported rejected, so it is never stored and never paid for again.
/// </summary>
public partial class AiI18nTranslationService(IAiService aiService, ILogger<AiI18nTranslationService> logger)
    : II18nTranslationService
{
    private const string PromptPhrase = "software UI label";

    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["es"] = "Spanish",
        ["pt"] = "Portuguese",
        ["zh"] = "Chinese (Simplified)",
        ["ar"] = "Arabic",
        ["fr"] = "French",
        ["de"] = "German",
    };

    public async Task<I18nTranslationResult> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct)
    {
        try
        {
            if (!await aiService.IsAvailableAsync(ct))
            {
                logger.LogWarning("i18n translation skipped — AI service unavailable ({Source} → {Target}).",
                    sourceLanguageCode, targetLanguageCode);
                return I18nTranslationResult.Unavailable;
            }

            var sourceName = LanguageNames.GetValueOrDefault(sourceLanguageCode, sourceLanguageCode);
            var targetName = LanguageNames.GetValueOrDefault(targetLanguageCode, targetLanguageCode);
            var prompt =
                $"Translate this software UI label from {sourceName} to {targetName}.\n" +
                $"Label: {text}\n" +
                "Reply with ONLY the translated label — no quotes, no explanation.";
            var raw = await aiService.GenerateTextAsync(
                prompt,
                "You are a professional translator for manufacturing/ERP software user interfaces. " +
                "Keep translations short and idiomatic for UI labels, and preserve any {{placeholder}} tokens exactly.",
                0.1,
                ct);

            if (string.IsNullOrWhiteSpace(raw))
            {
                logger.LogWarning("i18n translation failed — empty reply ({Source} → {Target}).",
                    sourceLanguageCode, targetLanguageCode);
                return I18nTranslationResult.Unavailable;
            }

            var translated = Sanitize(raw);
            if (!text.EndsWith('.') && translated.EndsWith('.') && !translated.EndsWith(".."))
            {
                translated = translated[..^1].TrimEnd();
            }

            if (!IsPlausibleTranslation(text, translated))
            {
                logger.LogWarning("i18n translation rejected — reply does not read as a label ({Source} → {Target}).",
                    sourceLanguageCode, targetLanguageCode);
                return I18nTranslationResult.Rejected;
            }

            return I18nTranslationResult.Translated(translated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "i18n translation failed ({Source} → {Target}).",
                sourceLanguageCode, targetLanguageCode);
            return I18nTranslationResult.Unavailable;
        }
    }

    /// <summary>Trims the reply and strips wrapping quotes; inner line breaks are kept so a multi-line reply is rejected.</summary>
    private static string Sanitize(string raw) =>
        raw.Trim().Trim('"', '“', '”').Trim();

    /// <summary>
    /// True when <paramref name="reply"/> can stand in for <paramref name="source"/> as a UI label:
    /// a single line, not far longer than the source, not sentence-like unless the source is,
    /// every {{placeholder}} token kept, and no mock or prompt echo.
    /// </summary>
    public static bool IsPlausibleTranslation(string source, string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || reply.Length > 2000 || reply.Contains('\n') || reply.Contains('\r'))
        {
            return false;
        }

        if (reply.Length > Math.Max(source.Length * 4, source.Length + 40))
        {
            return false;
        }

        if (reply.StartsWith("[Mock AI]", StringComparison.OrdinalIgnoreCase)
            || (reply.Contains(PromptPhrase, StringComparison.OrdinalIgnoreCase)
                && !source.Contains(PromptPhrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (SentenceBreak().IsMatch(reply) && !SentenceBreak().IsMatch(source))
        {
            return false;
        }

        return PlaceholderToken().Matches(source).All(m => reply.Contains(m.Value, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?:[!?;:…]|(?<!(?:^|[\s(./-])\p{L}{1,4})\.)\s+[¿¡]?\p{Lu}")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex PlaceholderToken();
}
