using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record ExportTranslationsQuery(string LanguageCode) : IRequest<Dictionary<string, string>>;

public class ExportTranslationsHandler(AppDbContext db) : IRequestHandler<ExportTranslationsQuery, Dictionary<string, string>>
{
    public async Task<Dictionary<string, string>> Handle(ExportTranslationsQuery request, CancellationToken cancellationToken)
    {
        return await db.TranslatedLabels
            .AsNoTracking()
            .Where(l => l.LanguageCode == request.LanguageCode)
            .OrderBy(l => l.Key)
            .ToDictionaryAsync(l => l.Key, l => l.Value, cancellationToken);
    }
}
