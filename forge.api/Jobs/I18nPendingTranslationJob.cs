using MediatR;

using Forge.Api.Features.I18nLabelOverrides;

namespace Forge.Api.Jobs;

/// <summary>
/// Recurring sweep that re-attempts pending i18n label machine translations —
/// fan-out rows created while the AI container was unreachable (the upsert
/// saves the human override and flags siblings pending instead of failing).
/// Delegates to <see cref="RetryPendingI18nTranslationsHandler"/> so the admin
/// "retry now" endpoint and this job share one code path.
/// </summary>
public class I18nPendingTranslationJob(IMediator mediator, ILogger<I18nPendingTranslationJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var result = await mediator.Send(new RetryPendingI18nTranslationsCommand(), ct);
        if (result.TranslatedCount > 0 || result.StillPendingCount > 0)
        {
            logger.LogInformation(
                "i18n pending-translation sweep: {Translated} translated, {StillPending} still pending.",
                result.TranslatedCount, result.StillPendingCount);
        }
    }
}
