using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// Machine-translates UI label strings via the self-hosted AI module
/// (<see cref="IAiService"/> — Ollama in production, mock under MOCK_INTEGRATIONS).
/// Availability is probed before generating so a downed AI container costs one
/// fast check, and every failure path returns null — callers flag the row as
/// pending instead of failing the save (graceful degradation, same contract as
/// the rest of the AI surface).
/// </summary>
public class AiI18nTranslationService(IAiService aiService, ILogger<AiI18nTranslationService> logger)
    : II18nTranslationService
{
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

    public async Task<string?> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct)
    {
        try
        {
            if (!await aiService.IsAvailableAsync(ct))
            {
                logger.LogWarning("i18n translation skipped — AI service unavailable ({Source} → {Target}).",
                    sourceLanguageCode, targetLanguageCode);
                return null;
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

            return Sanitize(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "i18n translation failed ({Source} → {Target}).",
                sourceLanguageCode, targetLanguageCode);
            return null;
        }
    }

    /// <summary>Takes the first non-empty line, strips wrapping quotes; null when unusable.</summary>
    private static string? Sanitize(string? raw)
    {
        var line = raw?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        line = line.Trim().Trim('"', '“', '”').Trim();
        return string.IsNullOrWhiteSpace(line) || line.Length > 2000 ? null : line;
    }
}
