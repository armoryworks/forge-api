using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads;

public record CreateLeadCommand(CreateLeadRequestModel Data) : IRequest<LeadResponseModel>;

public class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadCommandValidator()
    {
        RuleFor(x => x.Data.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.ContactName).MaximumLength(200).When(x => x.Data.ContactName is not null);
        RuleFor(x => x.Data.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Data.Email));
        RuleFor(x => x.Data.Phone).MaximumLength(50).When(x => x.Data.Phone is not null);
    }
}

public class CreateLeadHandler(ILeadRepository repo, AppDbContext db) : IRequestHandler<CreateLeadCommand, LeadResponseModel>
{
    public async Task<LeadResponseModel> Handle(CreateLeadCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("CreateLead requires an authenticated caller.");

        var lead = new Lead
        {
            CompanyName = data.CompanyName.Trim(),
            ContactName = data.ContactName?.Trim(),
            Email = data.Email?.Trim(),
            Phone = data.Phone?.Trim(),
            Source = data.Source?.Trim(),
            Notes = data.Notes?.Trim(),
            FollowUpDate = data.FollowUpDate,
            CreatedBy = userId,
            // Wave 7 — engagement-shape axis from the New Lead fork dialog.
            // Default Unknown round-trips for the "Quick add" path that
            // skips the fork.
            EngagementShape = data.EngagementShape,
            CustomFieldValues = data.CustomFieldValues,
            AccountId = data.AccountId,
        };

        await repo.AddAsync(lead, cancellationToken);

        // Repo.AddAsync already saved (gives us lead.Id). Log + flush.
        var shapeFragment = lead.EngagementShape == LeadEngagementShape.Unknown
            ? ""
            : $" [{lead.EngagementShape}]";
        db.LogActivityAt(
            "created",
            $"Created lead: {lead.CompanyName}{(string.IsNullOrEmpty(lead.ContactName) ? "" : $" — {lead.ContactName}")}{(string.IsNullOrEmpty(lead.Source) ? "" : $" (source: {lead.Source})")}{shapeFragment}",
            ("Lead", lead.Id));
        await db.SaveChangesAsync(cancellationToken);

        return (await repo.GetByIdAsync(lead.Id, cancellationToken))!;
    }
}
