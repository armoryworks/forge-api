using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scanner;

public record UnpairScanDeviceCommand(int DeviceId) : IRequest;

public class UnpairScanDeviceHandler(AppDbContext db) : IRequestHandler<UnpairScanDeviceCommand>
{
    public async Task Handle(UnpairScanDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await db.UserScanDevices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.DeviceId} not found");

        db.LogActivityAt("unpaired", $"Unpaired scanner {device.DeviceName ?? device.DeviceId}", ("UserScanDevice", device.Id));
        db.UserScanDevices.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
    }
}
