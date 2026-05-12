using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetMfaPolicyStatusQuery : IRequest<IReadOnlyList<MfaComplianceUserModel>>;

public class GetMfaPolicyStatusHandler(AppDbContext db, UserManager<ApplicationUser> userManager) : IRequestHandler<GetMfaPolicyStatusQuery, IReadOnlyList<MfaComplianceUserModel>>
{
    public async Task<IReadOnlyList<MfaComplianceUserModel>> Handle(GetMfaPolicyStatusQuery request, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Where(u => u.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var deviceLookup = await db.UserMfaDevices
            .Where(d => d.IsVerified && d.IsDefault)
            .AsNoTracking()
            .GroupBy(d => d.UserId)
            .Select(g => new { UserId = g.Key, DeviceType = g.First().DeviceType })
            .ToDictionaryAsync(x => x.UserId, x => x.DeviceType, cancellationToken);

        var result = new List<MfaComplianceUserModel>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            deviceLookup.TryGetValue(user.Id, out var deviceType);

            result.Add(new MfaComplianceUserModel
            {
                UserId = user.Id,
                FullName = $"{user.LastName}, {user.FirstName}",
                Email = user.Email!,
                Role = roles.FirstOrDefault() ?? "None",
                MfaEnabled = user.MfaEnabled,
                MfaDeviceType = user.MfaEnabled ? deviceType : null,
                IsEnforcedByPolicy = user.MfaEnforcedByPolicy,
            });
        }

        return result;
    }
}
