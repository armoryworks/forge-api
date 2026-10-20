using System.Text.RegularExpressions;

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
public partial class AiI18nTranslationService(IAiService aiService, ILogger<AiI18nTranslationService> logger)
    : II18nTranslationService
{
    private const string SentencePunctuation = ".!?;:";

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

            var translated = Sanitize(raw);
            if (translated is not null && !text.EndsWith('.') && translated.EndsWith('.') && !translated.EndsWith(".."))
            {
                translated = translated[..^1].TrimEnd();
            }

            if (translated is not null && !IsPlausibleTranslation(text, translated))
            {
                logger.LogWarning("i18n translation discarded — reply does not read as a label ({Source} → {Target}).",
                    sourceLanguageCode, targetLanguageCode);
                return null;
            }

            return translated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

    /// <summary>
    /// True when <paramref name="reply"/> can stand in for <paramref name="source"/> as a UI label:
    /// not far longer than the source, no sentence punctuation the source lacks, every
    /// {{placeholder}} token kept, and no mock or prompt echo.
    /// </summary>
    public static bool IsPlausibleTranslation(string source, string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        if (reply.Length > Math.Max(source.Length * 4, source.Length + 40))
        {
            return false;
        }

        if (reply.StartsWith("[Mock AI]", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("software UI label", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (reply.Any(c => SentencePunctuation.Contains(c) && !source.Contains(c)))
        {
            return false;
        }

        return PlaceholderToken().Matches(source).All(m => reply.Contains(m.Value, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex PlaceholderToken();
}
