using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.I18nLabelOverrides;

public record GetI18nLabelOverridesQuery : IRequest<List<I18nLabelOverrideResponseModel>>;

public class GetI18nLabelOverridesHandler(AppDbContext db)
    : IRequestHandler<GetI18nLabelOverridesQuery, List<I18nLabelOverrideResponseModel>>
{
    public async Task<List<I18nLabelOverrideResponseModel>> Handle(GetI18nLabelOverridesQuery request, CancellationToken cancellationToken)
    {
        return await db.I18nLabelOverrides
            .AsNoTracking()
            .OrderBy(o => o.Key).ThenBy(o => o.LanguageCode)
            .Select(o => new I18nLabelOverrideResponseModel(
                o.Id, o.Key, o.LanguageCode, o.Value,
                o.IsMachineTranslated, o.IsPendingTranslation, o.SourceLanguageCode,
                o.CreatedAt, o.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
