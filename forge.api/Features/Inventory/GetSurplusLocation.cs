using MediatR;

using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetSurplusLocationQuery : IRequest<SurplusLocationResponseModel>;

public class GetSurplusLocationHandler(AppDbContext db, ISettingsService settings)
    : IRequestHandler<GetSurplusLocationQuery, SurplusLocationResponseModel>
{
    public async Task<SurplusLocationResponseModel> Handle(GetSurplusLocationQuery request, CancellationToken cancellationToken)
    {
        var location = await SurplusStock.ConfiguredLocationAsync(db, settings, cancellationToken);
        return new SurplusLocationResponseModel(location?.Id, location?.Name);
    }
}
