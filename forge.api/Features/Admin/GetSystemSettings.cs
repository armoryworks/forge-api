using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetSystemSettingsQuery : IRequest<List<SystemSettingResponseModel>>;

public class GetSystemSettingsHandler(ISystemSettingRepository repo) : IRequestHandler<GetSystemSettingsQuery, List<SystemSettingResponseModel>>
{
    public async Task<List<SystemSettingResponseModel>> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        var settings = await repo.GetAllAsync(ct);
        return settings.Select(s => new SystemSettingResponseModel(s.Id, s.Key, s.Value, s.Description)).ToList();
    }
}
