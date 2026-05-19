using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ComplianceForms;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetAdminUsersQuery : IRequest<List<AdminUserResponseModel>>;

public class GetAdminUsersHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetAdminUsersQuery, List<AdminUserResponseModel>>
{
    public async Task<List<AdminUserResponseModel>> Handle(GetAdminUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Include(u => u.WorkLocation)
            .Include(u => u.RoleTemplate)
            .OrderBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        // Batch-load scan identifier types per user
        var scanTypes = await db.Set<UserScanIdentifier>()
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

        // Pre-load all state withholding reference data for batch lookup
        var stateRefs = await db.ReferenceData
            .AsNoTracking()
            .Where(r => r.GroupCode == "state_withholding")
            .ToDictionaryAsync(r => r.Code, cancellationToken);

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

            var isNoTaxState = false;
            string stateLabel = "State Tax Withholding";
            if (!string.IsNullOrWhiteSpace(stateCode) && stateRefs.TryGetValue(stateCode, out var stateRef))
            {
                stateLabel = $"State Tax Withholding ({stateRef.Label})";
                if (!string.IsNullOrWhiteSpace(stateRef.Metadata))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(stateRef.Metadata);
                        if (doc.RootElement.TryGetProperty("category", out var cat) && cat.GetString() == "no_tax")
                            isNoTaxState = true;
                    }
                    catch (JsonException) { }
                }
            }

            // Compute compliance items (mirrors GetProfileCompleteness logic)
            var complianceItems = new (string Key, string Label, bool IsComplete, bool BlocksAssignment)[]
            {
                ("w4", "W-4 Federal Tax Withholding", profile?.W4CompletedAt is not null, true),
                ("i9", "I-9 Employment Eligibility", profile?.I9CompletedAt is not null, true),
                ("state_withholding", stateLabel,
                    isNoTaxState || profile?.StateWithholdingCompletedAt is not null,
                    !isNoTaxState),
                ("emergency_contact", "Emergency Contact",
                    profile is not null &&
                    !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                    !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone), true),
                ("address", "Home Address",
                    profile is not null &&
                    !string.IsNullOrWhiteSpace(profile.Street1) &&
                    !string.IsNullOrWhiteSpace(profile.City) &&
                    !string.IsNullOrWhiteSpace(profile.State) &&
                    !string.IsNullOrWhiteSpace(profile.ZipCode), false),
                ("direct_deposit", "Direct Deposit", profile?.DirectDepositCompletedAt is not null, false),
                ("workers_comp", "Workers' Comp", profile?.WorkersCompAcknowledgedAt is not null, false),
                ("handbook", "Employee Handbook", profile?.HandbookAcknowledgedAt is not null, false),
            };

            // Bypass flag — mirrors GetProfileCompleteness short-circuit. Treat profile as complete
            // when an admin explicitly skipped onboarding (employee completed off-platform).
            var isBypassed = profile?.OnboardingBypassedAt is not null;
            var completedCount = isBypassed ? complianceItems.Length : complianceItems.Count(i => i.IsComplete);
            var canBeAssigned = isBypassed || complianceItems.Where(i => i.BlocksAssignment).All(i => i.IsComplete);
            var missingItems = isBypassed ? Array.Empty<string>() : complianceItems.Where(i => !i.IsComplete).Select(i => i.Label).ToArray();

            i9Submissions.TryGetValue(user.Id, out var i9Submission);
            var i9Status = I9StatusComputer.Compute(i9Submission);

            // Phase 3 / WU-06 / C1 — surface rollup template assignment to UI.
            string[]? templateRoles = null;
            if (user.RoleTemplate is not null)
            {
                try
                {
                    templateRoles = JsonSerializer.Deserialize<string[]>(
                        user.RoleTemplate.IncludedRoleNamesJson) ?? [];
                }
                catch (JsonException) { templateRoles = []; }
            }

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
                user.RoleTemplateId,
                user.RoleTemplate?.Name,
                templateRoles));
        }

        return result;
    }
}
