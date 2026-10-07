using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.ComplianceForms;
using Forge.Api.Features.EmployeeProfile;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetAdminUsersQuery : IRequest<List<AdminUserResponseModel>>;

public class GetAdminUsersHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<GetAdminUsersQuery, List<AdminUserResponseModel>>
{
    public async Task<List<AdminUserResponseModel>> Handle(GetAdminUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Include(u => u.WorkLocation)
            .OrderBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        // Batch-load scan identifier types per user
        var scanTypes = await db.Set<Forge.Core.Entities.UserScanIdentifier>()
            .Where(s => s.IsActive && s.DeletedAt == null)
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                HasRfid = g.Any(s => s.IdentifierType == "rfid" || s.IdentifierType == "nfc"),
                HasBarcode = g.Any(s => s.IdentifierType == "barcode"),
            })
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        // Batch-load employee profiles for compliance status. Phase 3 / WU-19:
        // EmployeeProfile.UserId is nullable; filter to linked profiles only.
        var userIds = users.Select(u => u.Id).ToList();
        var profiles = await db.EmployeeProfiles
            .AsNoTracking()
            .Where(p => p.UserId != null && userIds.Contains(p.UserId.Value))
            .ToDictionaryAsync(p => p.UserId!.Value, cancellationToken);

        // Batch-load I-9 submissions for status computation
        var i9Submissions = await db.ComplianceFormSubmissions
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s => userIds.Contains(s.UserId)
                        && s.Template.FormType == ComplianceFormType.I9)
            .ToDictionaryAsync(s => s.UserId, cancellationToken);

        // Pre-load default location and company_state for fallback resolution
        var defaultLocation = await db.CompanyLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, cancellationToken);
        var companyStateSetting = await db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "company_state", cancellationToken);

        var stateLabels = await db.ReferenceData
            .AsNoTracking()
            .Where(r => r.GroupCode == "state_withholding")
            .ToDictionaryAsync(r => r.Code, r => r.Label, cancellationToken);

        var hrOn = EmployeeComplianceRules.IsHrTrackingOn(capabilities);
        var resolveStateCategory = await EmployeeComplianceRules.LoadStateCategoryResolverAsync(db, cancellationToken);

        var result = new List<AdminUserResponseModel>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var hasPassword = await userManager.HasPasswordAsync(user);
            var hasPendingToken = user.SetupToken != null
                && user.SetupTokenExpiresAt.HasValue
                && user.SetupTokenExpiresAt.Value > DateTimeOffset.UtcNow;
            scanTypes.TryGetValue(user.Id, out var scan);
            profiles.TryGetValue(user.Id, out var profile);

            // Resolve per-employee state: work location → default location → company_state
            var stateCode = user.WorkLocation?.State
                ?? defaultLocation?.State
                ?? companyStateSetting?.Value;

            var stateCategory = resolveStateCategory(user.WorkLocation?.State);
            var stateLabel = !string.IsNullOrWhiteSpace(stateCode) && stateLabels.TryGetValue(stateCode, out var stateName)
                ? $"State Tax Withholding ({stateName})"
                : "State Tax Withholding";

            // Compute compliance items (mirrors GetProfileCompleteness logic)
            var complianceItems = new (string Key, string Label, bool IsComplete)[]
            {
                ("w4", "W-4 Federal Tax Withholding", profile?.W4CompletedAt is not null),
                ("i9", "I-9 Employment Eligibility", profile?.I9CompletedAt is not null),
                ("state_withholding", stateLabel,
                    EmployeeComplianceRules.IsStateWithholdingSatisfied(profile, stateCategory)),
                ("emergency_contact", "Emergency Contact", EmployeeComplianceRules.HasEmergencyContact(profile)),
                ("address", "Home Address", EmployeeComplianceRules.HasHomeAddress(profile)),
                ("direct_deposit", "Direct Deposit", profile?.DirectDepositCompletedAt is not null),
                ("workers_comp", "Workers' Comp", profile?.WorkersCompAcknowledgedAt is not null),
                ("handbook", "Employee Handbook", profile?.HandbookAcknowledgedAt is not null),
            };

            var treatAsComplete = !hrOn || profile?.OnboardingBypassedAt is not null;
            var completedCount = treatAsComplete ? complianceItems.Length : complianceItems.Count(i => i.IsComplete);
            var canBeAssigned = EmployeeComplianceRules.CanBeAssignedJobs(profile, stateCategory, hrOn);
            var missingItems = treatAsComplete ? Array.Empty<string>() : complianceItems.Where(i => !i.IsComplete).Select(i => i.Label).ToArray();

            i9Submissions.TryGetValue(user.Id, out var i9Submission);
            var i9Status = I9StatusComputer.Compute(i9Submission);

            result.Add(new AdminUserResponseModel(
                user.Id,
                user.Email!,
                user.FirstName,
                user.LastName,
                user.Initials,
                user.AvatarColor,
                user.IsActive,
                roles.ToArray(),
                user.CreatedAt,
                hasPassword,
                hasPendingToken,
                scan?.HasRfid ?? false,
                scan?.HasBarcode ?? false,
                canBeAssigned,
                completedCount,
                complianceItems.Length,
                missingItems,
                user.WorkLocationId,
                user.WorkLocation?.Name,
                i9Status,
                user.IsNonEmployee));
        }

        return result;
    }
}
