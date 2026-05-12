using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanDevicesQuery : IRequest<List<ScanDeviceResponseModel>>;

public class GetScanDevicesHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext)
    : IRequestHandler<GetScanDevicesQuery, List<ScanDeviceResponseModel>>
{
    public async Task<List<ScanDeviceResponseModel>> Handle(
        GetScanDevicesQuery request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        return await db.UserScanDevices
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.PairedAt)
            .Select(d => new ScanDeviceResponseModel(
                d.Id,
                d.DeviceId,
                d.DeviceName,
                d.PairedAt,
                d.IsActive))
            .ToListAsync(cancellationToken);
    }
}
