using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Terminology;

public interface ITerminologyLabels
{
    Task<TermLabels> GetAsync(CancellationToken ct);
}

public static class TerminologyLabelsExtensions
{
    public static async Task<TermLabels> GetOrDefaultsAsync(this ITerminologyLabels? labels, CancellationToken ct) =>
        labels is null ? TermLabels.Defaults : await labels.GetAsync(ct);
}

public class TerminologyLabels(AppDbContext db) : ITerminologyLabels
{
    private TermLabels? _labels;

    public async Task<TermLabels> GetAsync(CancellationToken ct)
    {
        if (_labels is not null) return _labels;

        var keys = TerminologyTermCatalog.All
            .SelectMany(t => new[] { t.SingularKey, t.PluralKey })
            .ToList();
        var overrides = await db.TerminologyEntries
            .AsNoTracking()
            .Where(e => keys.Contains(e.Key) && e.Label != "")
            .Select(e => new { e.Key, e.Label })
            .ToListAsync(ct);

        _labels = new TermLabels(overrides.ToDictionary(e => e.Key, e => e.Label.Trim(), StringComparer.Ordinal));
        return _labels;
    }
}

public sealed class TermLabels(IReadOnlyDictionary<string, string> overrides)
{
    public static readonly TermLabels Defaults = new(new Dictionary<string, string>());

    private static readonly IReadOnlyDictionary<string, string[]> Protected = new Dictionary<string, string[]>
    {
        ["job"] = ["job shop", "job shops", "background job", "background jobs", "hangfire job", "hangfire jobs"],
        ["part"] = ["part of", "parts of", "in part"],
        ["lead"] = ["lead time", "lead times", "lead-time", "leads to"],
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private Regex? _pattern;
    private Dictionary<string, (string TermId, bool Plural)>? _forms;

    public string Singular(string termId)
    {
        var term = Find(termId);
        return Override(term.SingularKey) ?? term.DefaultSingular;
    }

    public string Plural(string termId)
    {
        var term = Find(termId);
        return Override(term.PluralKey)
            ?? (Override(term.SingularKey) is { } singular ? Pluralize(singular) : term.DefaultPlural);
    }

    public string Substitute(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var pattern = Pattern();
        if (pattern is null) return text;
        return pattern.Replace(text, match =>
        {
            var key = Whitespace.Replace(match.Value, " ").ToLowerInvariant();
            if (!_forms!.TryGetValue(key, out var form) || form.TermId.Length == 0)
                return match.Value;
            var replacement = form.Plural ? Plural(form.TermId) : Singular(form.TermId);
            if (match.Value.Length > 1 && match.Value == match.Value.ToUpperInvariant())
                return replacement.ToUpperInvariant();
            return char.IsLower(match.Value[0]) ? Lower(replacement) : replacement;
        });
    }

    public string ForCount(string termId, int count) => count == 1 ? Singular(termId) : Plural(termId);

    public static string Lower(string label) =>
        string.Join(' ', label.Split(' ').Select(word => IsCapitalizedWord(word) ? word.ToLowerInvariant() : word));

    public static string Pluralize(string singular)
    {
        var words = singular.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return singular;
        var last = words[^1];
        var lower = last.ToLowerInvariant();
        words[^1] = lower switch
        {
            _ when lower.Length > 1 && lower[^1] == 'y' && !"aeiou".Contains(lower[^2]) =>
                last[..^1] + (last[^1] == 'Y' ? "IES" : "ies"),
            _ when lower.EndsWith('s') || lower.EndsWith('x') || lower.EndsWith('z')
                || lower.EndsWith("ch") || lower.EndsWith("sh") => $"{last}es",
            _ => $"{last}s",
        };
        return string.Join(' ', words);
    }

    private Regex? Pattern()
    {
        if (_forms is not null) return _pattern;
        _forms = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        foreach (var term in TerminologyTermCatalog.All)
        {
            if (Override(term.SingularKey) is null && Override(term.PluralKey) is null) continue;
            _forms[term.DefaultSingular.ToLowerInvariant()] = (term.Id, false);
            _forms[term.DefaultPlural.ToLowerInvariant()] = (term.Id, true);
            foreach (var phrase in Protected.GetValueOrDefault(term.Id, []))
                _forms[phrase] = (string.Empty, false);
        }
        if (_forms.Count == 0) return null;
        var alternatives = _forms.Keys
            .OrderByDescending(f => f.Length)
            .Select(f => string.Join(@"\s+", f.Split(' ').Select(Regex.Escape)));
        _pattern = new Regex($@"(?<![\p{{L}}\p{{N}}_])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return _pattern;
    }

    private string? Override(string key) =>
        overrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static TerminologyTerm Find(string termId) =>
        TerminologyTermCatalog.All.FirstOrDefault(t => t.Id == termId)
        ?? throw new ArgumentOutOfRangeException(nameof(termId), termId, "Unknown terminology term.");

    private static bool IsCapitalizedWord(string word) =>
        word.Length > 0 && char.IsUpper(word[0]) && word.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c));
}
