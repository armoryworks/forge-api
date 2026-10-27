using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.I18nLabelOverrides;

public record ImportI18nLabelOverridesCommand(ImportI18nLabelOverridesRequestModel Body)
    : IRequest<ImportI18nLabelOverridesResponseModel>;

public class ImportI18nLabelOverridesValidator : AbstractValidator<ImportI18nLabelOverridesCommand>
{
    public const int MaxEntries = 20000;

    public ImportI18nLabelOverridesValidator()
    {
        RuleFor(x => x.Body.LanguageCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Body.Entries).NotNull();
        RuleFor(x => x.Body.Entries.Count).LessThanOrEqualTo(MaxEntries).When(x => x.Body.Entries is not null);
        RuleForEach(x => x.Body.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Key).NotEmpty().MaximumLength(400);
            entry.RuleFor(e => e.Value).NotNull().MaximumLength(2000);
        });
        RuleFor(x => x.Body.Entries)
            .Must(entries => entries.Select(e => e.Key.Trim()).Distinct().Count() == entries.Count)
            .When(x => x.Body.Entries is not null && x.Body.Entries.All(e => e.Key is not null))
            .WithMessage("Each key can appear once in an import.");
    }
}

/// <summary>
/// Imports a translation file for one language as human label overrides. A value equal to the
/// shipped catalog value is not stored: an existing override for that key is reverted instead, so
/// the shipped text (and later catalog fixes to it) shows. Blank values are skipped. Imports never
/// fan out machine translations to other languages.
/// </summary>
public class ImportI18nLabelOverridesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ImportI18nLabelOverridesCommand, ImportI18nLabelOverridesResponseModel>
{
    public async Task<ImportI18nLabelOverridesResponseModel> Handle(
        ImportI18nLabelOverridesCommand request, CancellationToken cancellationToken)
    {
        var languageCode = request.Body.LanguageCode.Trim().ToLowerInvariant();
        var existing = await db.I18nLabelOverrides
            .Where(o => o.LanguageCode == languageCode)
            .ToDictionaryAsync(o => o.Key, cancellationToken);

        int created = 0, updated = 0, reverted = 0, unchanged = 0;
        var touched = new List<(I18nLabelOverride Row, string Action)>();
        foreach (var entry in request.Body.Entries)
        {
            var key = entry.Key.Trim();
            var value = entry.Value.Trim();
            existing.TryGetValue(key, out var row);

            if (value.Length == 0)
            {
                unchanged++;
                continue;
            }

            if (entry.ShippedValue is not null && value == entry.ShippedValue.Trim())
            {
                if (row is null)
                {
                    unchanged++;
                    continue;
                }

                row.DeletedAt = clock.UtcNow;
                touched.Add((row, "deleted"));
                reverted++;
                continue;
            }

            if (row is null)
            {
                var added = new I18nLabelOverride { Key = key, LanguageCode = languageCode, Value = value };
                db.I18nLabelOverrides.Add(added);
                touched.Add((added, "created"));
                created++;
                continue;
            }

            if (row.Value == value && !row.IsMachineTranslated && !row.IsPendingTranslation)
            {
                unchanged++;
                continue;
            }

            row.Value = value;
            row.IsMachineTranslated = false;
            row.IsPendingTranslation = false;
            row.SourceLanguageCode = null;
            touched.Add((row, "updated"));
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (touched.Count > 0)
        {
            foreach (var (row, action) in touched)
            {
                var verb = action switch
                {
                    "created" => "Imported label",
                    "updated" => "Imported new text for label",
                    _ => "Import reverted label",
                };
                db.LogActivityAt(action, $"{verb} '{row.Key}' ({languageCode})", ("I18nLabelOverride", row.Id));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new ImportI18nLabelOverridesResponseModel(created, updated, reverted, unchanged);
    }
}
