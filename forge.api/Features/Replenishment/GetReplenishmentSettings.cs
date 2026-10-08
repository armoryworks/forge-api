using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Replenishment;

public record GetReplenishmentSettingsQuery : IRequest<ReplenishmentSettingsResponseModel>;

public class GetReplenishmentSettingsHandler(ISystemSettingRepository settings)
    : IRequestHandler<GetReplenishmentSettingsQuery, ReplenishmentSettingsResponseModel>
{
    public async Task<ReplenishmentSettingsResponseModel> Handle(
        GetReplenishmentSettingsQuery request, CancellationToken cancellationToken)
    {
        var setting = await settings.FindByKeyAsync(ReplenishmentAssignee.SettingKey, cancellationToken);
        return new ReplenishmentSettingsResponseModel(ReplenishmentAssignee.Parse(setting?.Value));
    }
}
