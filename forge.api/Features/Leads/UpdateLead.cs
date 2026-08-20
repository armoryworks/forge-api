using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads;

public record UpdateLeadCommand(int Id, UpdateLeadRequestModel Data) : IRequest<LeadResponseModel>;

public class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
{
    public UpdateLeadCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.CompanyName).MaximumLength(200).When(x => x.Data.CompanyName is not null);
        RuleFor(x => x.Data.LeadNumber).NotEmpty().MaximumLength(50).When(x => x.Data.LeadNumber is not null);
        RuleFor(x => x.Data.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Data.Email));
        RuleFor(x => x.Data.Phone).MaximumLength(50).When(x => x.Data.Phone is not null);
    }
}

public class UpdateLeadHandler(
    ILeadRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db) : IRequestHandler<UpdateLeadCommand, LeadResponseModel>
{
    // System setting that gates caller-supplied lead numbers (shared with CreateLead).
    private const string AllowManualLeadNumbersKey = "leads.allow_manual_numbers";

    public async Task<LeadResponseModel> Handle(UpdateLeadCommand request, CancellationToken cancellationToken)
    {
        var lead = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Lead not found.");

        var data = request.Data;
        var changedFields = new List<string>();

        // User-settable lead number — only when manual numbers are enabled, and only after a
        // uniqueness check that excludes this lead. The DB partial-unique index is the final backstop.
        if (data.LeadNumber is not null)
        {
            var newNumber = data.LeadNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, lead.LeadNumber, StringComparison.Ordinal))
            {
                if (!await ManualNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual lead numbers are disabled. Turn on 'leads.allow_manual_numbers' in settings to change a lead number.");
                if (await repo.LeadNumberExistsAsync(newNumber, lead.Id, cancellationToken))
                    throw new InvalidOperationException($"Lead number '{newNumber}' is already in use.");
                // Ensure the current number is on record (covers legacy leads with none), then
                // supersede it — the old number stays resolvable. RenameAsync alone opens the
                // first active row when the current value is null.
                if (!string.IsNullOrWhiteSpace(lead.LeadNumber))
                    await identifiers.IssueAsync(BusinessEntityType.Lead, lead.Id, lead.LeadNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Lead, lead.Id, newNumber, cancellationToken);
                lead.LeadNumber = newNumber;
                changedFields.Add("leadNumber");
            }
        }

        if (data.CompanyName is not null && data.CompanyName.Trim() != lead.CompanyName)
        {
            lead.CompanyName = data.CompanyName.Trim();
            changedFields.Add("companyName");
        }
        if (data.ContactName is not null && data.ContactName.Trim() != lead.ContactName)
        {
            lead.ContactName = data.ContactName.Trim();
            changedFields.Add("contactName");
        }

        // A lead must remain identifiable — company OR contact. Individuals are
        // allowed (blank company), but clearing both is not.
        if (string.IsNullOrWhiteSpace(lead.CompanyName) && string.IsNullOrWhiteSpace(lead.ContactName))
            throw new ValidationException("A lead must have a company name or a contact name.");

        if (data.Email is not null && data.Email.Trim() != lead.Email)
        {
            lead.Email = data.Email.Trim();
            changedFields.Add("email");
        }
        if (data.Phone is not null && data.Phone.Trim() != lead.Phone)
        {
            lead.Phone = data.Phone.Trim();
            changedFields.Add("phone");
        }
        if (data.Source is not null && data.Source.Trim() != lead.Source)
        {
            lead.Source = data.Source.Trim();
            changedFields.Add("source");
        }
        if (data.Status.HasValue && data.Status.Value != lead.Status)
        {
            // C1-back: the funnel is forward-only out of Converted. A converted lead became
            // a customer (it carries ConvertedCustomerId) — regressing it to New/Contacted/etc.
            // would orphan that link and resurrect a closed lead. Converted is terminal.
            if (lead.Status == LeadStatus.Converted)
                throw new InvalidOperationException(
                    "A converted lead cannot change status — conversion is final.");

            lead.Status = data.Status.Value;
            // Status transitions are the most-watched lead event — call out
            // the new status by name in the rollup so the activity tab is
            // legible at a glance ("status: Contacted" vs just "status").
            changedFields.Add($"status: {lead.Status}");
        }
        if (data.Notes is not null && data.Notes.Trim() != lead.Notes)
        {
            lead.Notes = data.Notes.Trim();
            changedFields.Add("notes");
        }
        if (data.FollowUpDate.HasValue && data.FollowUpDate != lead.FollowUpDate)
        {
            lead.FollowUpDate = data.FollowUpDate;
            changedFields.Add("followUpDate");
        }
        if (data.LostReason is not null && data.LostReason.Trim() != lead.LostReason)
        {
            lead.LostReason = data.LostReason.Trim();
            changedFields.Add("lostReason");
        }
        // Wave 7 — engagement-shape reclassification. Surface the new shape
        // by name in the rollup since it changes how the lead is queued in
        // the team's sales motion (matches the status-rename treatment above).
        if (data.EngagementShape.HasValue && data.EngagementShape.Value != lead.EngagementShape)
        {
            lead.EngagementShape = data.EngagementShape.Value;
            changedFields.Add($"engagementShape: {lead.EngagementShape}");
        }
        if (data.CustomFieldValues is not null && data.CustomFieldValues != lead.CustomFieldValues)
        {
            lead.CustomFieldValues = data.CustomFieldValues;
            changedFields.Add("customFieldValues");
        }

        // Phase 1r / Batch 13-14 — manufacturing/compliance classifications.
        // Each transition is rolled into the same activity-log entry; we
        // surface the new state by name (matches Status / EngagementShape
        // treatment above) so an auditor reading the activity tab can see
        // the trail without opening the row.
        if (data.CapabilityFit.HasValue && data.CapabilityFit.Value != lead.CapabilityFit)
        {
            lead.CapabilityFit = data.CapabilityFit.Value;
            changedFields.Add($"capabilityFit: {lead.CapabilityFit}");
        }
        if (data.NdaState.HasValue && data.NdaState.Value != lead.NdaState)
        {
            lead.NdaState = data.NdaState.Value;
            changedFields.Add($"ndaState: {lead.NdaState}");
        }
        if (data.NdaSignedAt.HasValue && data.NdaSignedAt != lead.NdaSignedAt)
        {
            lead.NdaSignedAt = data.NdaSignedAt;
            changedFields.Add("ndaSignedAt");
        }
        if (data.NdaExpiresAt.HasValue && data.NdaExpiresAt != lead.NdaExpiresAt)
        {
            lead.NdaExpiresAt = data.NdaExpiresAt;
            changedFields.Add("ndaExpiresAt");
        }
        if (data.ExportControl.HasValue && data.ExportControl.Value != lead.ExportControl)
        {
            lead.ExportControl = data.ExportControl.Value;
            changedFields.Add($"exportControl: {lead.ExportControl}");
        }
        if (data.AccountId != lead.AccountId)
        {
            lead.AccountId = data.AccountId;
            changedFields.Add(lead.AccountId.HasValue ? $"accountId: {lead.AccountId}" : "accountId: cleared");
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Lead", lead.Id));
        }

        await repo.SaveChangesAsync(cancellationToken);

        return (await repo.GetByIdAsync(lead.Id, cancellationToken))!;
    }

    private async Task<bool> ManualNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualLeadNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
