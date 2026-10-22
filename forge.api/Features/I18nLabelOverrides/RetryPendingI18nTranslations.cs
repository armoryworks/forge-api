using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.I18nLabelOverrides;

public record RetryPendingI18nTranslationsCommand : IRequest<RetryPendingI18nTranslationsResponseModel>;

/// <summary>
/// Re-attempts machine translation for every pending fan-out row (created while
/// the translation service was unreachable). Invoked by the admin "retry" action
/// and by the recurring Hangfire job. Pending rows whose source override has been
/// reverted are removed — there is nothing left to translate. A row whose reply is
/// rejected as not reading like a label is discarded too, so the shipped catalog text
/// stays in place and the same paid request is not sent again on the next run.
/// </summary>
public class RetryPendingI18nTranslationsHandler(AppDbContext db, II18nTranslationService translator, IClock clock)
    : IRequestHandler<RetryPendingI18nTranslationsCommand, RetryPendingI18nTranslationsResponseModel>
{
    public async Task<RetryPendingI18nTranslationsResponseModel> Handle(RetryPendingI18nTranslationsCommand request, CancellationToken cancellationToken)
    {
        var pending = await db.I18nLabelOverrides
            .Where(o => o.IsPendingTranslation)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return new RetryPendingI18nTranslationsResponseModel(0, 0, 0);
        }

        var keys = pending.Select(p => p.Key).Distinct().ToList();
        var sources = (await db.I18nLabelOverrides
                .AsNoTracking()
                .Where(o => keys.Contains(o.Key) && !o.IsMachineTranslated)
                .ToListAsync(cancellationToken))
            .ToDictionary(o => (o.Key, o.LanguageCode));

        var translatedRows = new List<(string EntityType, int EntityId)>();
        var discardedRows = new List<(string EntityType, int EntityId)>();
        var stillPending = 0;

        foreach (var row in pending)
        {
            if (row.SourceLanguageCode is null
                || !sources.TryGetValue((row.Key, row.SourceLanguageCode), out var source))
            {
                row.DeletedAt = clock.UtcNow; // Source override reverted — orphaned pending row.
                continue;
            }

            var result = await translator.TranslateLabelAsync(
                source.Value, source.LanguageCode, row.LanguageCode, cancellationToken);
            if (result.Outcome == I18nTranslationOutcome.Rejected)
            {
                row.DeletedAt = clock.UtcNow;
                discardedRows.Add(("I18nLabelOverride", row.Id));
                continue;
            }

            if (result.Value is null)
            {
                stillPending++;
                continue;
            }

            row.Value = result.Value;
            row.IsPendingTranslation = false;
            translatedRows.Add(("I18nLabelOverride", row.Id));
        }

        if (translatedRows.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Completed {translatedRows.Count} pending machine translation(s)",
                translatedRows.ToArray());
        }

        if (discardedRows.Count > 0)
        {
            db.LogActivityAt(
                "deleted",
                $"Discarded {discardedRows.Count} machine translation(s) that did not read as a label",
                discardedRows.ToArray());
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RetryPendingI18nTranslationsResponseModel(translatedRows.Count, stillPending, discardedRows.Count);
    }
}
