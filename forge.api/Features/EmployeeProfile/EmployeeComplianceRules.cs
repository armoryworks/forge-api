using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Data.Context;

using EmployeeProfileEntity = Forge.Core.Entities.EmployeeProfile;

namespace Forge.Api.Features.EmployeeProfile;

public static class EmployeeComplianceRules
{
    public const string HrTrackingCapability = "CAP-HR-HIRE";
    public const string NoTaxCategory = "no_tax";

    private const string StateWithholdingGroup = "state_withholding";
    private const string DefaultCategory = "state_form";

    public static bool IsHrTrackingOn(ICapabilitySnapshotProvider capabilities)
        => capabilities.IsEnabled(HrTrackingCapability);

    public static bool CanBeAssignedJobs(EmployeeProfileEntity? profile, string? stateCategory, bool hrOn)
    {
        if (!hrOn) return true;
        if (profile is null) return false;
        if (profile.OnboardingBypassedAt is not null) return true;

        return profile.W4CompletedAt is not null &&
               profile.I9CompletedAt is not null &&
               IsStateWithholdingSatisfied(profile, stateCategory) &&
               HasEmergencyContact(profile);
    }

    public static bool IsProfileComplete(EmployeeProfileEntity? profile, string? stateCategory, bool hrOn)
    {
        if (!hrOn) return true;
        if (profile is null) return false;
        if (profile.OnboardingBypassedAt is not null) return true;

        return CanBeAssignedJobs(profile, stateCategory, hrOn) &&
               HasHomeAddress(profile) &&
               profile.DirectDepositCompletedAt is not null &&
               profile.WorkersCompAcknowledgedAt is not null &&
               profile.HandbookAcknowledgedAt is not null;
    }

    public static async Task<bool> IsProfileCompleteAsync(
        AppDbContext db, ICapabilitySnapshotProvider capabilities, int userId, CancellationToken ct)
    {
        if (!IsHrTrackingOn(capabilities)) return true;

        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null) return false;
        if (profile.OnboardingBypassedAt is not null) return true;

        var stateCategory = await ResolveStateCategoryAsync(db, userId, ct);
        return IsProfileComplete(profile, stateCategory, hrOn: true);
    }

    public static bool IsNoTaxState(string? stateCategory) => stateCategory == NoTaxCategory;

    public static bool IsStateWithholdingSatisfied(EmployeeProfileEntity? profile, string? stateCategory)
        => IsNoTaxState(stateCategory) || profile?.StateWithholdingCompletedAt is not null;

    public static bool HasEmergencyContact(EmployeeProfileEntity? profile)
        => profile is not null &&
           !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
           !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone);

    public static bool HasHomeAddress(EmployeeProfileEntity? profile)
        => profile is not null &&
           !string.IsNullOrWhiteSpace(profile.Street1) &&
           !string.IsNullOrWhiteSpace(profile.City) &&
           !string.IsNullOrWhiteSpace(profile.State) &&
           !string.IsNullOrWhiteSpace(profile.ZipCode);

    public static async Task<string?> ResolveStateCategoryAsync(AppDbContext db, int userId, CancellationToken ct)
        => (await ResolveStateWithholdingInfoAsync(db, userId, ct))?.Category;

    public static async Task<StateWithholdingInfoModel?> ResolveStateWithholdingInfoAsync(
        AppDbContext db, int userId, CancellationToken ct)
    {
        var workLocationState = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.WorkLocation != null ? u.WorkLocation.State : null)
            .FirstOrDefaultAsync(ct);

        var stateCode = workLocationState;
        var source = "Work Location";

        if (string.IsNullOrWhiteSpace(stateCode))
        {
            stateCode = await db.CompanyLocations
                .AsNoTracking()
                .Where(l => l.IsDefault && l.IsActive)
                .Select(l => l.State)
                .FirstOrDefaultAsync(ct);
            source = "Default Location";
        }

        if (string.IsNullOrWhiteSpace(stateCode))
        {
            stateCode = await db.SystemSettings
                .AsNoTracking()
                .Where(s => s.Key == "company_state")
                .Select(s => s.Value)
                .FirstOrDefaultAsync(ct);
            source = "Company Setting";
        }

        if (string.IsNullOrWhiteSpace(stateCode))
            return null;

        var stateRef = await db.ReferenceData
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupCode == StateWithholdingGroup && r.Code == stateCode, ct);

        if (stateRef is null)
            return null;

        var (category, formName) = ParseMetadata(stateRef.Metadata);
        return new StateWithholdingInfoModel(stateCode, stateRef.Label, category, formName, source);
    }

    public static async Task<Func<string?, string?>> LoadStateCategoryResolverAsync(AppDbContext db, CancellationToken ct)
    {
        var fallbackState = await db.CompanyLocations
            .AsNoTracking()
            .Where(l => l.IsDefault && l.IsActive)
            .Select(l => l.State)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(fallbackState))
        {
            fallbackState = await db.SystemSettings
                .AsNoTracking()
                .Where(s => s.Key == "company_state")
                .Select(s => s.Value)
                .FirstOrDefaultAsync(ct);
        }

        var categories = (await db.ReferenceData
                .AsNoTracking()
                .Where(r => r.GroupCode == StateWithholdingGroup)
                .Select(r => new { r.Code, r.Metadata })
                .ToListAsync(ct))
            .GroupBy(r => r.Code)
            .ToDictionary(g => g.Key, g => ParseMetadata(g.First().Metadata).Category);

        return workLocationState =>
        {
            var stateCode = string.IsNullOrWhiteSpace(workLocationState) ? fallbackState : workLocationState;
            if (string.IsNullOrWhiteSpace(stateCode)) return null;
            return categories.TryGetValue(stateCode, out var category) ? category : null;
        };
    }

    public static async Task<Func<EmployeeProfileEntity?, string?, bool>> LoadAssignabilityCheckAsync(
        AppDbContext db, ICapabilitySnapshotProvider capabilities, CancellationToken ct)
    {
        if (!IsHrTrackingOn(capabilities))
            return (_, _) => true;

        var resolveCategory = await LoadStateCategoryResolverAsync(db, ct);
        return (profile, workLocationState) => CanBeAssignedJobs(profile, resolveCategory(workLocationState), hrOn: true);
    }

    private static (string Category, string? FormName) ParseMetadata(string? metadata)
    {
        var category = DefaultCategory;
        string? formName = null;

        if (string.IsNullOrWhiteSpace(metadata))
            return (category, formName);

        try
        {
            using var doc = JsonDocument.Parse(metadata);
            if (doc.RootElement.TryGetProperty("category", out var cat))
                category = cat.GetString() ?? DefaultCategory;
            if (doc.RootElement.TryGetProperty("formName", out var form))
                formName = form.GetString();
        }
        catch (JsonException)
        {
        }

        return (category, formName);
    }
}
