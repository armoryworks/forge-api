using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record ListDevicesQuery(int? UserId) : IRequest<List<DeviceResponseModel>>;

public class ListDevicesHandler(
    AppDbContext db,
    IClock clock,
    IOptions<MobileOptions> options)
    : IRequestHandler<ListDevicesQuery, List<DeviceResponseModel>>
{
    public async Task<List<DeviceResponseModel>> Handle(
        ListDevicesQuery request, CancellationToken cancellationToken)
    {
        var staleBefore = clock.UtcNow.AddDays(-options.Value.StaleDeviceDays);

        var query = db.UserDevices.AsNoTracking();
        if (request.UserId is not null)
            query = query.Where(d => d.UserId == request.UserId);

        return await query
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new DeviceResponseModel(
                d.Id,
                d.UserId,
                d.UserId == null
                    ? null
                    : db.Users
                        .Where(u => u.Id == d.UserId)
                        .Select(u => u.LastName + ", " + u.FirstName)
                        .FirstOrDefault(),
                d.Name,
                d.Platform,
                d.OsVersion,
                d.AppVersion,
                d.CreatedAt,
                d.LastSeenAt,
                d.RevokedAt,
                d.IsFlagged,
                d.RevokedAt == null && (d.LastSeenAt == null || d.LastSeenAt < staleBefore)))
            .ToListAsync(cancellationToken);
    }
}
