using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record GetEmployeeDetailQuery(int EmployeeId) : IRequest<EmployeeDetailResponseModel?>;

public class GetEmployeeDetailHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetEmployeeDetailQuery, EmployeeDetailResponseModel?>
{
    public async Task<EmployeeDetailResponseModel?> Handle(GetEmployeeDetailQuery request, CancellationToken ct)
    {
        // Phase 3 / WU-19 / F9 — id-resolution order designed to preserve
        // backward compatibility with the legacy user-keyed contract while
        // adding User-less Employee addressing:
        //
        //   1. EmployeeProfile.Id == id AND UserId IS NULL → User-less
        //      projection. This is the only way to address a User-less
        //      Employee, so we check it first.
        //   2. User.Id == id → legacy projection (Employee == User
        //      assumption). Pre-WU-19 callers continue to work.
        //   3. EmployeeProfile.Id == id with linked UserId → fall through
        //      to the linked User's projection. Lets new EmployeeProfile.Id
        //      callers continue to work after the Employee is promoted.
        //   4. Else 404.

        var userlessProfile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId && p.UserId == null && p.DeletedAt == null, ct);

        if (userlessProfile is not null)
            return ProjectUserlessProfile(userlessProfile);

        var user = await db.Users
            .Include(u => u.WorkLocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.EmployeeId, ct);

        if (user is not null)
        {
            var profile = await db.EmployeeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
            return await ProjectFromUser(user, profile, ct);
        }

        // Linked EmployeeProfile lookup (id was a profile id, not a user id)
        var linkedProfile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId && p.UserId != null && p.DeletedAt == null, ct);

        if (linkedProfile is not null)
        {
            var linked = await db.Users
                .Include(u => u.WorkLocation)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == linkedProfile.UserId!.Value, ct);
            if (linked is not null)
                return await ProjectFromUser(linked, linkedProfile, ct);
        }

        return null;
    }

    private async Task<EmployeeDetailResponseModel> ProjectFromUser(
        ApplicationUser user, Core.Entities.EmployeeProfile? profile, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Unknown";

        string? teamName = null;
        if (user.TeamId.HasValue)
        {
            teamName = await db.ReferenceData
                .AsNoTracking()
                .Where(r => r.Id == user.TeamId.Value)
                .Select(r => r.Label)
                .FirstOrDefaultAsync(ct);
        }

        var scanTypes = await db.Set<UserScanIdentifier>()
            .Where(s => s.UserId == user.Id && s.IsActive && s.DeletedAt == null)
            .Select(s => s.IdentifierType)
            .ToListAsync(ct);

        var complianceItems = new[]
        {
            profile?.W4CompletedAt is not null,
            profile?.I9CompletedAt is not null,
            profile?.StateWithholdingCompletedAt is not null,
            profile is not null &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone),
            profile is not null &&
                !string.IsNullOrWhiteSpace(profile.Street1) &&
                !string.IsNullOrWhiteSpace(profile.City) &&
                !string.IsNullOrWhiteSpace(profile.State) &&
                !string.IsNullOrWhiteSpace(profile.ZipCode),
            profile?.DirectDepositCompletedAt is not null,
            profile?.WorkersCompAcknowledgedAt is not null,
            profile?.HandbookAcknowledgedAt is not null,
        };

        // Id semantics — legacy callers reach this branch by passing User.Id.
        // We preserve User.Id as the response Id so existing UI navigation
        // (GET /employees/{id}/stats, etc.) keeps resolving against the
        // User-keyed handlers.
        return new EmployeeDetailResponseModel(
            user.Id,
            user.Id,
            user.FirstName,
            user.LastName,
            user.Initials,
            user.AvatarColor,
            user.Email ?? string.Empty,
            profile?.PhoneNumber,
            primaryRole,
            teamName,
            user.TeamId,
            user.IsActive,
            profile?.JobTitle,
            profile?.Department,
            profile?.StartDate,
            user.CreatedAt,
            user.WorkLocationId,
            user.WorkLocation?.Name,
            !string.IsNullOrEmpty(user.PinHash),
            scanTypes.Any(t => t == "rfid" || t == "nfc"),
            scanTypes.Any(t => t == "barcode"),
            profile?.PersonalEmail,
            profile?.Street1,
            profile?.Street2,
            profile?.City,
            profile?.State,
            profile?.ZipCode,
            profile?.EmergencyContactName,
            profile?.EmergencyContactPhone,
            profile?.EmergencyContactRelationship,
            complianceItems.Count(c => c),
            complianceItems.Length);
    }

    private static EmployeeDetailResponseModel ProjectUserlessProfile(Core.Entities.EmployeeProfile profile)
    {
        var complianceItems = new[]
        {
            profile.W4CompletedAt is not null,
            profile.I9CompletedAt is not null,
            profile.StateWithholdingCompletedAt is not null,
            !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone),
            !string.IsNullOrWhiteSpace(profile.Street1) &&
                !string.IsNullOrWhiteSpace(profile.City) &&
                !string.IsNullOrWhiteSpace(profile.State) &&
                !string.IsNullOrWhiteSpace(profile.ZipCode),
            profile.DirectDepositCompletedAt is not null,
            profile.WorkersCompAcknowledgedAt is not null,
            profile.HandbookAcknowledgedAt is not null,
        };

        return new EmployeeDetailResponseModel(
            profile.Id,
            null,
            profile.FirstName ?? string.Empty,
            profile.LastName ?? string.Empty,
            null,
            null,
            profile.WorkEmail ?? string.Empty,
            profile.PhoneNumber,
            "(none)",
            null,
            null,
            true,
            profile.JobTitle,
            profile.Department,
            profile.StartDate,
            profile.CreatedAt,
            null,
            null,
            false,
            false,
            false,
            profile.PersonalEmail,
            profile.Street1,
            profile.Street2,
            profile.City,
            profile.State,
            profile.ZipCode,
            profile.EmergencyContactName,
            profile.EmergencyContactPhone,
            profile.EmergencyContactRelationship,
            complianceItems.Count(c => c),
            complianceItems.Length);
    }
}
