using FluentValidation;
using MediatR;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Leads;

public record UpdateLeadCommand(int Id, UpdateLeadRequestModel Data) : IRequest<LeadResponseModel>;

public class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
{
    public UpdateLeadCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.CompanyName).MaximumLength(200).When(x => x.Data.CompanyName is not null);
        RuleFor(x => x.Data.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Data.Email));
        RuleFor(x => x.Data.Phone).MaximumLength(50).When(x => x.Data.Phone is not null);
    }
}

public class UpdateLeadHandler(ILeadRepository repo, AppDbContext db) : IRequestHandler<UpdateLeadCommand, LeadResponseModel>
{
    public async Task<LeadResponseModel> Handle(UpdateLeadCommand request, CancellationToken cancellationToken)
    {
        var lead = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Lead not found.");

        var data = request.Data;
        var changedFields = new List<string>();

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
}
