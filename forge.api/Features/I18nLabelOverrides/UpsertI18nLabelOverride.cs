using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.I18nLabelOverrides;

public record UpsertI18nLabelOverrideCommand(UpsertI18nLabelOverrideRequestModel Body)
    : IRequest<UpsertI18nLabelOverrideResponseModel>;

public class UpsertI18nLabelOverrideValidator : AbstractValidator<UpsertI18nLabelOverrideCommand>
{
    public UpsertI18nLabelOverrideValidator()
    {
        RuleFor(x => x.Body.Key).NotEmpty().MaximumLength(400);
        RuleFor(x => x.Body.LanguageCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Body.Value).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>
/// Saves a human label override for one key + language. When requested, fans the
/// edit out to every other configured language as a machine translation
/// (<see cref="I18nLabelOverride.IsMachineTranslated"/>). Existing human overrides
/// in other languages are never clobbered. If the translation service is
/// unreachable the primary override still saves and the fan-out rows are flagged
/// <see cref="I18nLabelOverride.IsPendingTranslation"/> for the retry job.
/// </summary>
public class UpsertI18nLabelOverrideHandler(AppDbContext db, II18nTranslationService translator)
    : IRequestHandler<UpsertI18nLabelOverrideCommand, UpsertI18nLabelOverrideResponseModel>
{
    public async Task<UpsertI18nLabelOverrideResponseModel> Handle(UpsertI18nLabelOverrideCommand request, CancellationToken cancellationToken)
    {
        var key = request.Body.Key.Trim();
        var languageCode = request.Body.LanguageCode.Trim().ToLowerInvariant();
        var value = request.Body.Value.Trim();

        var existingForKey = await db.I18nLabelOverrides
            .Where(o => o.Key == key)
            .ToListAsync(cancellationToken);

        var primary = existingForKey.FirstOrDefault(o => o.LanguageCode == languageCode);
        var created = primary is null;
        if (primary is null)
        {
            primary = new I18nLabelOverride { Key = key, LanguageCode = languageCode };
            db.I18nLabelOverrides.Add(primary);
        }

        primary.Value = value;
        primary.IsMachineTranslated = false;
        primary.IsPendingTranslation = false;
        primary.SourceLanguageCode = null;

        var affected = new List<I18nLabelOverride> { primary };
        var pendingCount = 0;
        var translatedCount = 0;

        if (request.Body.TranslateToOtherLanguages)
        {
            var targets = (await I18nLanguages.GetConfiguredCodesAsync(db, cancellationToken))
                .Where(code => code != languageCode);

            foreach (var target in targets)
            {
                var sibling = existingForKey.FirstOrDefault(o => o.LanguageCode == target);
                if (sibling is not null && !sibling.IsMachineTranslated)
                {
                    continue; // A human override in that language always wins — never clobber it.
                }

                var translated = await translator.TranslateLabelAsync(value, languageCode, target, cancellationToken);
                if (sibling is null)
                {
                    sibling = new I18nLabelOverride { Key = key, LanguageCode = target };
                    db.I18nLabelOverrides.Add(sibling);
                }

                // Pending rows keep the source text as a placeholder; they are
                // excluded from the active merge map until the retry translates them.
                sibling.Value = translated ?? value;
                sibling.IsMachineTranslated = true;
                sibling.IsPendingTranslation = translated is null;
                sibling.SourceLanguageCode = languageCode;

                if (translated is null) { pendingCount++; } else { translatedCount++; }
                affected.Add(sibling);
            }
        }

        // First save persists new rows (assigns ids) so the activity row can reference them.
        await db.SaveChangesAsync(cancellationToken);

        var fanOutSummary = (translatedCount, pendingCount) switch
        {
            (0, 0) => string.Empty,
            (_, 0) => $"; auto-translated to {translatedCount} language(s)",
            (0, _) => $"; {pendingCount} translation(s) pending",
            _ => $"; auto-translated to {translatedCount} language(s), {pendingCount} pending",
        };
        db.LogActivityAt(
            created ? "created" : "updated",
            $"{(created ? "Overrode" : "Updated override for")} label '{key}' ({languageCode}){fanOutSummary}",
            ("I18nLabelOverride", primary.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new UpsertI18nLabelOverrideResponseModel(
            affected.Select(o => new I18nLabelOverrideResponseModel(
                o.Id, o.Key, o.LanguageCode, o.Value,
                o.IsMachineTranslated, o.IsPendingTranslation, o.SourceLanguageCode,
                o.CreatedAt, o.UpdatedAt)).ToList(),
            pendingCount > 0);
    }
}
