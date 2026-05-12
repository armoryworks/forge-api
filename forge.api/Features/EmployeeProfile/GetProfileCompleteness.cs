using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record GetProfileCompletenessQuery(int UserId) : IRequest<ProfileCompletenessResponseModel>;

public class GetProfileCompletenessHandler(AppDbContext db) : IRequestHandler<GetProfileCompletenessQuery, ProfileCompletenessResponseModel>
{
    public async Task<ProfileCompletenessResponseModel> Handle(GetProfileCompletenessQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        // Bypass flag — treat the whole profile as complete
        if (profile?.OnboardingBypassedAt is not null)
        {
            var stateInfoBypass = await ResolveStateWithholdingInfoAsync(request.UserId, ct);
            return new ProfileCompletenessResponseModel(
                IsComplete: true,
                CanBeAssignedJobs: true,
                TotalItems: 8,
                CompletedItems: 8,
                Items: [],
                StateWithholdingInfo: stateInfoBypass);
        }

        // Resolve per-employee state withholding info
        var stateInfo = await ResolveStateWithholdingInfoAsync(request.UserId, ct);
        var isNoTaxState = stateInfo?.Category == "no_tax";

        var items = new List<ProfileCompletenessItem>
        {
            // Job-assignment blockers — must be complete before user can be assigned work
            new("w4", "W-4 Federal Tax Withholding",
                profile?.W4CompletedAt is not null,
                BlocksJobAssignment: true),

            new("i9", "I-9 Employment Eligibility",
                profile?.I9CompletedAt is not null,
                BlocksJobAssignment: true),

            new("stateWithholding",
                stateInfo is not null
                    ? $"State Tax Withholding ({stateInfo.StateName})"
                    : "State Tax Withholding",
                isNoTaxState || profile?.StateWithholdingCompletedAt is not null,
                BlocksJobAssignment: !isNoTaxState),

            new("emergency_contact", "Emergency Contact",
                profile is not null &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone),
                BlocksJobAssignment: true),

            // Required but do not block job assignment
            new("address", "Home Address",
                profile is not null &&
                !string.IsNullOrWhiteSpace(profile.Street1) &&
                !string.IsNullOrWhiteSpace(profile.City) &&
                !string.IsNullOrWhiteSpace(profile.State) &&
                !string.IsNullOrWhiteSpace(profile.ZipCode),
                BlocksJobAssignment: false),

            new("directDeposit", "Direct Deposit Authorization",
                profile?.DirectDepositCompletedAt is not null,
                BlocksJobAssignment: false),

            new("workersComp", "Workers' Comp Acknowledgment",
                profile?.WorkersCompAcknowledgedAt is not null,
                BlocksJobAssignment: false),

            new("handbook", "Employee Handbook Acknowledgment",
                profile?.HandbookAcknowledgedAt is not null,
                BlocksJobAssignment: false),
        };

        var completedCount = items.Count(i => i.IsComplete);
        var canBeAssigned = items.Where(i => i.BlocksJobAssignment).All(i => i.IsComplete);

        return new ProfileCompletenessResponseModel(
            IsComplete: completedCount == items.Count,
            CanBeAssignedJobs: canBeAssigned,
            TotalItems: items.Count,
            CompletedItems: completedCount,
            Items: items,
            StateWithholdingInfo: stateInfo);
    }

    private async Task<StateWithholdingInfoModel?> ResolveStateWithholdingInfoAsync(int userId, CancellationToken ct)
    {
        // 1. Try user's assigned work location state
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.WorkLocation)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        string? stateCode = user?.WorkLocation?.State;
        var source = "Work Location";

        // 2. Fall back to default company location
        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var defaultLocation = await db.CompanyLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, ct);
            stateCode = defaultLocation?.State;
            source = "Default Location";
        }

        // 3. Fall back to company_state system setting
        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var companySetting = await db.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "company_state", ct);
            stateCode = companySetting?.Value;
            source = "Company Setting";
        }

        if (string.IsNullOrWhiteSpace(stateCode))
            return null;

        // Look up the state in reference data
        var stateRef = await db.ReferenceData
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupCode == "state_withholding" && r.Code == stateCode, ct);

        if (stateRef is null)
            return null;

        var category = "state_form";
        string? formName = null;

        if (!string.IsNullOrWhiteSpace(stateRef.Metadata))
        {
            try
            {
                using var doc = JsonDocument.Parse(stateRef.Metadata);
                if (doc.RootElement.TryGetProperty("category", out var cat))
                    category = cat.GetString() ?? "state_form";
                if (doc.RootElement.TryGetProperty("formName", out var form))
                    formName = form.GetString();
            }
            catch (JsonException) { /* ignore malformed metadata */ }
        }

        return new StateWithholdingInfoModel(stateCode, stateRef.Label, category, formName, source);
    }
}
