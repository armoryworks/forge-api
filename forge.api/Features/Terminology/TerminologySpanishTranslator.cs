using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Terminology;

public partial class TerminologySpanishTranslator(AppDbContext db, II18nTranslationService translator)
{
    private const string SourceLanguage = "en";
    private const string TargetLanguage = "es";
    private const int MaxNounLength = 60;
    private static readonly CultureInfo TargetCulture = CultureInfo.GetCultureInfo(TargetLanguage);
    private bool _translatorUnavailable;

    public async Task<Dictionary<string, TerminologyEntry>> LoadRowsAsync(
        IEnumerable<string> keys, CancellationToken ct)
    {
        var all = keys
            .Concat(TerminologyTermCatalog.All.SelectMany(SpanishKeys))
            .Distinct()
            .ToList();
        return await db.TerminologyEntries
            .Where(e => all.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, ct);
    }

    public async Task<IReadOnlyList<string>> TranslateChangedTermsAsync(
        IReadOnlyCollection<string> changedEnglishKeys, CancellationToken ct)
    {
        var terms = TerminologyTermCatalog.All
            .Where(t => changedEnglishKeys.Contains(t.SingularKey) || changedEnglishKeys.Contains(t.PluralKey))
            .ToList();
        if (terms.Count == 0)
            return [];

        var rows = await LoadRowsAsync(terms.SelectMany(t => new[] { t.SingularKey, t.PluralKey }), ct);
        var untranslated = new List<string>();
        foreach (var term in terms)
        {
            if (!await TranslateTermAsync(term, rows, ct))
                untranslated.Add(term.Id);
        }

        await db.SaveChangesAsync(ct);
        return untranslated;
    }

    public async Task<bool> TranslateTermAsync(
        TerminologyTerm term, Dictionary<string, TerminologyEntry> rows, CancellationToken ct)
    {
        var spanish = SpanishKeys(term);
        if (spanish[..2].Any(key => rows.TryGetValue(key, out var row) && IsAdminSpanish(row)))
        {
            ClearMachineRows(rows, spanish);
            return true;
        }
        var adminGender = rows.TryGetValue(spanish[2], out var genderRow) && IsAdminSpanish(genderRow);

        var englishOne = ActiveLabel(rows, term.SingularKey);
        var englishOther = ActiveLabel(rows, term.PluralKey);
        if (englishOne is null && englishOther is null)
        {
            ClearMachineRows(rows, spanish);
            return true;
        }

        var one = englishOne is null ? null : await TranslateNounAsync(englishOne, ct);
        var other = englishOther is null ? null : await TranslateNounAsync(englishOther, ct);
        if ((englishOne is not null && one is null) || (englishOther is not null && other is null))
        {
            ClearMachineRows(rows, spanish);
            return false;
        }

        WriteMachineValue(rows, spanish[0], one?.Text);
        WriteMachineValue(rows, spanish[1], other?.Text);
        if (!adminGender)
            WriteMachineValue(rows, spanish[2], one?.Gender ?? other?.Gender);
        return true;
    }

    private async Task<TranslatedNoun?> TranslateNounAsync(string english, CancellationToken ct)
    {
        if (_translatorUnavailable)
            return null;
        var translated = await translator.TranslateLabelAsync($"the {english}", SourceLanguage, TargetLanguage, ct);
        if (string.IsNullOrWhiteSpace(translated))
        {
            _translatorUnavailable = true;
            return null;
        }

        var text = translated.Normalize(NormalizationForm.FormC).Trim().TrimEnd('.').Trim();
        string? gender = null;
        var article = SpanishArticle().Match(text);
        if (article.Success)
        {
            gender = article.Groups["article"].Value.ToLowerInvariant() is "la" or "las" ? "f" : "m";
            text = text[article.Length..].Trim();
        }

        if (text.Length == 0 || text.Length > MaxNounLength || SentencePunctuation().IsMatch(text))
            return null;

        return new TranslatedNoun(char.ToUpper(text[0], TargetCulture) + text[1..], gender);
    }

    private void WriteMachineValue(Dictionary<string, TerminologyEntry> rows, string key, string? value)
    {
        if (value is null)
        {
            ClearMachineRows(rows, [key]);
            return;
        }

        if (!rows.TryGetValue(key, out var row))
        {
            row = new TerminologyEntry { Key = key };
            db.TerminologyEntries.Add(row);
            rows[key] = row;
        }
        else if (row.Label == value && row.SourcePresetId == TerminologyEntry.MachineTranslationSource)
        {
            return;
        }

        row.Label = value;
        row.IsAdminEdited = true;
        row.SourcePresetId = TerminologyEntry.MachineTranslationSource;
    }

    private static void ClearMachineRows(Dictionary<string, TerminologyEntry> rows, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!rows.TryGetValue(key, out var row) || row.Label.Length == 0)
                continue;
            if (row.IsAdminEdited && row.SourcePresetId != TerminologyEntry.MachineTranslationSource)
                continue;
            row.Label = string.Empty;
            row.IsAdminEdited = true;
            row.SourcePresetId = TerminologyEntry.MachineTranslationSource;
        }
    }

    private static string? ActiveLabel(Dictionary<string, TerminologyEntry> rows, string key) =>
        rows.TryGetValue(key, out var row) && row.Label.Length > 0 ? row.Label : null;

    public static bool HasSpanishText(Dictionary<string, TerminologyEntry> rows, TerminologyTerm term) =>
        SpanishKeys(term)[..2].Any(key => rows.TryGetValue(key, out var row)
            && (row.Label.Length > 0 || row.SourcePresetId != TerminologyEntry.MachineTranslationSource));

    private static bool IsAdminSpanish(TerminologyEntry row) =>
        row.Label.Length > 0 && row.IsAdminEdited && row.SourcePresetId != TerminologyEntry.MachineTranslationSource;

    public static string[] SpanishKeys(TerminologyTerm term) =>
    [
        TerminologyTerm.ForLanguage(term.SingularKey, TargetLanguage),
        TerminologyTerm.ForLanguage(term.PluralKey, TargetLanguage),
        TerminologyTerm.ForLanguage(term.GenderKey, TargetLanguage),
    ];

    [GeneratedRegex(@"^(?<article>el|la|los|las)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpanishArticle();

    [GeneratedRegex(@"[.!?;:\r\n]", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePunctuation();

    private sealed record TranslatedNoun(string Text, string? Gender);
}
