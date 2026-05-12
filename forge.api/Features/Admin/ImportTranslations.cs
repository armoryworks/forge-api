using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record ImportTranslationsCommand(string LanguageCode, ImportTranslationsRequestModel Request) : IRequest<int>;

public class ImportTranslationsHandler(AppDbContext db, IClock clock) : IRequestHandler<ImportTranslationsCommand, int>
{
    public async Task<int> Handle(ImportTranslationsCommand command, CancellationToken cancellationToken)
    {
        var existing = await db.TranslatedLabels
            .Where(l => l.LanguageCode == command.LanguageCode)
            .ToDictionaryAsync(l => l.Key, cancellationToken);

        var now = clock.UtcNow;
        var count = 0;

        foreach (var (key, value) in command.Request.Translations)
        {
            if (existing.TryGetValue(key, out var label))
            {
                label.Value = value;
                label.TranslatedAt = now;
            }
            else
            {
                db.TranslatedLabels.Add(new TranslatedLabel
                {
                    Key = key,
                    LanguageCode = command.LanguageCode,
                    Value = value,
                    TranslatedAt = now,
                });
            }

            count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }
}
