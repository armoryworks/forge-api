using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record UnpairScanDeviceCommand(int DeviceId) : IRequest;

public class UnpairScanDeviceHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext)
    : IRequestHandler<UnpairScanDeviceCommand>
{
    public async Task Handle(UnpairScanDeviceCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var device = await db.UserScanDevices
            .Where(d => d.Id == request.DeviceId && d.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.DeviceId} not found");

        db.UserScanDevices.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
    }
}
