using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanDevicesQuery : IRequest<List<ScanDeviceResponseModel>>;

public class GetScanDevicesHandler(AppDbContext db)
    : IRequestHandler<GetScanDevicesQuery, List<ScanDeviceResponseModel>>
{
    public async Task<List<ScanDeviceResponseModel>> Handle(
        GetScanDevicesQuery request, CancellationToken cancellationToken)
    {
        var devices = await db.UserScanDevices
            .AsNoTracking()
            .OrderBy(d => d.DeviceName ?? d.DeviceId)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        return await ScanDeviceProjection.ToResponsesAsync(db, devices, cancellationToken);
    }
}
