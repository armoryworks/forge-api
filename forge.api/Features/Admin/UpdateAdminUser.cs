using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdateAdminUserCommand(
    int Id,
    string? FirstName,
    string? LastName,
    string? Initials,
    string? AvatarColor,
    bool? IsActive,
    string? Role) : IRequest<AdminUserResponseModel>;

public class UpdateAdminUserValidator : AbstractValidator<UpdateAdminUserCommand>
{
    public UpdateAdminUserValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.FirstName).MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.Initials).MaximumLength(4).When(x => x.Initials is not null);
        RuleFor(x => x.AvatarColor).MaximumLength(20).When(x => x.AvatarColor is not null);
        RuleFor(x => x.Role).MaximumLength(50).When(x => x.Role is not null);
    }
}

public class UpdateAdminUserHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<UpdateAdminUserCommand, AdminUserResponseModel>
{
    public async Task<AdminUserResponseModel> Handle(UpdateAdminUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(u => u.WorkLocation)
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {request.Id} not found.");

        if (request.FirstName is not null) user.FirstName = request.FirstName;
        if (request.LastName is not null) user.LastName = request.LastName;
        if (request.Initials is not null) user.Initials = request.Initials;
        if (request.AvatarColor is not null) user.AvatarColor = request.AvatarColor;
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to update user: {errors}");
        }

        if (request.Role is not null)
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, request.Role);

            // Cross-entity audit row: role change is system-wide audit only.
            await auditWriter.WriteAsync("RoleAssigned", db.CurrentUserId ?? 0,
                entityType: "ApplicationUser",
                entityId: user.Id,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetUserId = user.Id,
                    fromRoles = currentRoles.ToArray(),
                    toRole = request.Role,
                }),
                ct: cancellationToken);
        }

        if (request.IsActive.HasValue)
        {
            // Activation/deactivation is captured by per-entity SaveChanges
            // already, but the explicit DEACTIVATED action is high-signal —
            // surface it as a discrete system-wide row too.
            await auditWriter.WriteAsync(
                request.IsActive.Value ? "UserActivated" : "UserDeactivated",
                db.CurrentUserId ?? 0,
                entityType: "ApplicationUser",
                entityId: user.Id,
                ct: cancellationToken);
        }

        var roles = await userManager.GetRolesAsync(user);
        var hasPassword = await userManager.HasPasswordAsync(user);
        var hasPendingToken = user.SetupToken != null
            && user.SetupTokenExpiresAt.HasValue
            && user.SetupTokenExpiresAt.Value > DateTimeOffset.UtcNow;
        var userScans = await db.Set<Forge.Core.Entities.UserScanIdentifier>()
            .Where(s => s.UserId == user.Id && s.IsActive && s.DeletedAt == null)
            .Select(s => s.IdentifierType)
            .ToListAsync(cancellationToken);

        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var complianceItems = new (string Label, bool IsComplete, bool BlocksAssignment)[]
        {
            ("W-4 Federal Tax Withholding", profile?.W4CompletedAt is not null, true),
            ("I-9 Employment Eligibility", profile?.I9CompletedAt is not null, true),
            ("State Tax Withholding", profile?.StateWithholdingCompletedAt is not null, true),
            ("Emergency Contact",
                profile is not null &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone), true),
            ("Home Address",
                profile is not null &&
                !string.IsNullOrWhiteSpace(profile.Street1) &&
                !string.IsNullOrWhiteSpace(profile.City) &&
                !string.IsNullOrWhiteSpace(profile.State) &&
                !string.IsNullOrWhiteSpace(profile.ZipCode), false),
            ("Direct Deposit", profile?.DirectDepositCompletedAt is not null, false),
            ("Workers' Comp", profile?.WorkersCompAcknowledgedAt is not null, false),
            ("Employee Handbook", profile?.HandbookAcknowledgedAt is not null, false),
        };

        var completedCount = complianceItems.Count(i => i.IsComplete);
        var canBeAssigned = complianceItems.Where(i => i.BlocksAssignment).All(i => i.IsComplete);
        var missingItems = complianceItems.Where(i => !i.IsComplete).Select(i => i.Label).ToArray();

        // Phase 3 / WU-06 / C1 — include rollup template assignment.
        string[]? templateRoles = null;
        if (user.RoleTemplate is not null)
        {
            try
            {
                templateRoles = System.Text.Json.JsonSerializer.Deserialize<string[]>(
                    user.RoleTemplate.IncludedRoleNamesJson) ?? [];
            }
            catch (System.Text.Json.JsonException) { templateRoles = []; }
        }

        return new AdminUserResponseModel(
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
            userScans.Any(t => t == "rfid" || t == "nfc"),
            userScans.Any(t => t == "barcode"),
            canBeAssigned,
            completedCount,
            complianceItems.Length,
            missingItems,
            user.WorkLocationId,
            user.WorkLocation?.Name,
            null, // I9Status — computed in Batch F (I9StatusComputer)
            user.RoleTemplateId,
            user.RoleTemplate?.Name,
            templateRoles);
    }
}
