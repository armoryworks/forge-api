using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetTranslationsQuery(string LanguageCode) : IRequest<List<TranslationEntryResponseModel>>;

public class GetTranslationsHandler(AppDbContext db) : IRequestHandler<GetTranslationsQuery, List<TranslationEntryResponseModel>>
{
    public async Task<List<TranslationEntryResponseModel>> Handle(GetTranslationsQuery request, CancellationToken cancellationToken)
    {
        var labels = await db.TranslatedLabels
            .AsNoTracking()
            .Where(l => l.LanguageCode == request.LanguageCode)
            .OrderBy(l => l.Key)
            .ToListAsync(cancellationToken);

        return labels.Select(l => new TranslationEntryResponseModel(
            l.Key, l.Value, l.Context, l.IsApproved)).ToList();
    }
}
