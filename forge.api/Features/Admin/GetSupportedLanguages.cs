using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetSupportedLanguagesQuery : IRequest<List<SupportedLanguageResponseModel>>;

public class GetSupportedLanguagesHandler(AppDbContext db) : IRequestHandler<GetSupportedLanguagesQuery, List<SupportedLanguageResponseModel>>
{
    public async Task<List<SupportedLanguageResponseModel>> Handle(GetSupportedLanguagesQuery request, CancellationToken cancellationToken)
    {
        var languages = await db.SupportedLanguages
            .AsNoTracking()
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);

        return languages.Select(l => new SupportedLanguageResponseModel(
            l.Id, l.Code, l.Name, l.NativeName,
            l.IsDefault, l.IsActive, l.CompletionPercent)).ToList();
    }
}
