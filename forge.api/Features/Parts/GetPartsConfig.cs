using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Client-facing parts configuration derived from system settings. Exposed on
/// PartsController (capability CAP-MD-PARTS) so any part-creating role can read
/// it — the admin system-settings endpoint is Admin-only and unreachable for
/// Engineers/ProductionWorkers who create parts.
/// </summary>
public record PartsConfigResponseModel(bool AllowManualPartNumbers);

public record GetPartsConfigQuery() : IRequest<PartsConfigResponseModel>;

public class GetPartsConfigHandler(ISystemSettingRepository systemSettings)
    : IRequestHandler<GetPartsConfigQuery, PartsConfigResponseModel>
{
    // Keep in sync with CreatePartHandler.AllowManualPartNumbersKey.
    private const string AllowManualPartNumbersKey = "parts.allow_manual_numbers";

    public async Task<PartsConfigResponseModel> Handle(GetPartsConfigQuery request, CancellationToken cancellationToken)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPartNumbersKey, cancellationToken);
        var allow = setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
        return new PartsConfigResponseModel(allow);
    }
}
