using System.Globalization;
using System.Text.RegularExpressions;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Terminology;

public record UpdateTerminologyCommand(List<TerminologyEntryRequestModel> Entries)
    : IRequest<UpdateTerminologyResponseModel>;

public class UpdateTerminologyValidator : AbstractValidator<UpdateTerminologyCommand>
{
    public UpdateTerminologyValidator()
    {
        RuleFor(x => x.Entries).NotEmpty().WithMessage("At least one terminology entry is required");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Key).NotEmpty().MaximumLength(100)
                .Matches("^[a-z][a-z0-9_]*(:[a-z]{2})?$")
                .WithMessage("Terminology keys are lower-case snake_case with an optional ':<language>' suffix");
            entry.RuleFor(e => e.Label).MaximumLength(200);
        });
    }
}

public partial class UpdateTerminologyHandler(
    AppDbContext db,
    ITerminologyRepository repo,
    II18nTranslationService translator,
    IHubContext<NotificationHub> notificationHub)
    : IRequestHandler<UpdateTerminologyCommand, UpdateTerminologyResponseModel>
{
    private const string SourceLanguage = "en";
    private const string TargetLanguage = "es";
    private static readonly CultureInfo TargetCulture = CultureInfo.GetCultureInfo(TargetLanguage);

    public async Task<UpdateTerminologyResponseModel> Handle(
        UpdateTerminologyCommand request, CancellationToken cancellationToken)
    {
        var requested = request.Entries
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.Last().Label?.Trim() ?? string.Empty);
        var spanishKeys = TerminologyTermCatalog.All
            .SelectMany(t => SpanishKeys(t))
            .ToList();
        var keys = requested.Keys.Concat(spanishKeys).Distinct().ToList();
        var rows = await db.TerminologyEntries
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        var changedKeys = new HashSet<string>();
        foreach (var (key, label) in requested)
        {
            if (WriteAdminValue(rows, key, label))
                changedKeys.Add(key);
        }

        var untranslated = new List<string>();
        foreach (var term in TerminologyTermCatalog.All)
        {
            var changed = changedKeys.Contains(term.SingularKey) || changedKeys.Contains(term.PluralKey);
            var retry = (requested.ContainsKey(term.SingularKey) || requested.ContainsKey(term.PluralKey))
                && !HasSpanishText(rows, term);
            if (!changed && !retry)
                continue;
            if (!await TranslateTermAsync(term, rows, cancellationToken))
                untranslated.Add(term.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notificationHub.Clients.All.SendAsync("terminologyChanged", new { }, cancellationToken);

        return new UpdateTerminologyResponseModel(await repo.GetAllAsync(cancellationToken), untranslated);
    }

    private bool WriteAdminValue(Dictionary<string, TerminologyEntry> rows, string key, string label)
    {
        if (rows.TryGetValue(key, out var row))
        {
            if (row.Label == label && row.IsAdminEdited && row.SourcePresetId is null)
                return false;
            if (label.Length == 0 && row.Label.Length == 0 && row.IsAdminEdited)
                return false;
            row.Label = label;
            row.IsAdminEdited = true;
            row.SourcePresetId = null;
            return true;
        }

        if (label.Length == 0)
            return false;

        row = new TerminologyEntry { Key = key, Label = label, IsAdminEdited = true };
        db.TerminologyEntries.Add(row);
        rows[key] = row;
        return true;
    }

    private async Task<bool> TranslateTermAsync(
        TerminologyTerm term, Dictionary<string, TerminologyEntry> rows, CancellationToken ct)
    {
        var spanish = SpanishKeys(term);
        if (spanish[..2].Any(key => rows.TryGetValue(key, out var row) && IsAdminSpanish(row)))
            return true;
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
        var translated = await translator.TranslateLabelAsync($"the {english}", SourceLanguage, TargetLanguage, ct);
        if (string.IsNullOrWhiteSpace(translated))
            return null;

        var text = translated.Trim().TrimEnd('.').Trim();
        string? gender = null;
        var article = SpanishArticle().Match(text);
        if (article.Success)
        {
            gender = article.Groups["article"].Value.ToLowerInvariant() is "la" or "las" ? "f" : "m";
            text = text[article.Length..].Trim();
        }

        if (text.Length == 0 || text.Length > 200)
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

    private static bool HasSpanishText(Dictionary<string, TerminologyEntry> rows, TerminologyTerm term) =>
        SpanishKeys(term)[..2].Any(key => rows.TryGetValue(key, out var row)
            && (row.Label.Length > 0 || row.SourcePresetId != TerminologyEntry.MachineTranslationSource));

    private static bool IsAdminSpanish(TerminologyEntry row) =>
        row.Label.Length > 0 && row.IsAdminEdited && row.SourcePresetId != TerminologyEntry.MachineTranslationSource;

    private static string[] SpanishKeys(TerminologyTerm term) =>
    [
        TerminologyTerm.ForLanguage(term.SingularKey, TargetLanguage),
        TerminologyTerm.ForLanguage(term.PluralKey, TargetLanguage),
        TerminologyTerm.ForLanguage(term.GenderKey, TargetLanguage),
    ];

    [GeneratedRegex(@"^(?<article>el|la|los|las)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpanishArticle();

    private sealed record TranslatedNoun(string Text, string? Gender);
}
