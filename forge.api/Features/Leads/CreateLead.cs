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
        // A lead is identified by a company OR a person — individuals (no
        // company) are first-class, so require at least one of the two rather
        // than mandating CompanyName.
        RuleFor(x => x.Data.CompanyName)
            .Must((cmd, _) => !string.IsNullOrWhiteSpace(cmd.Data.CompanyName) || !string.IsNullOrWhiteSpace(cmd.Data.ContactName))
            .WithMessage("Provide a company name or a contact name.");
        RuleFor(x => x.Data.CompanyName).MaximumLength(200);
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
            CompanyName = data.CompanyName?.Trim() ?? string.Empty,
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
        var shapeFragment = lead.EngagementShape == Core.Enums.LeadEngagementShape.Unknown
            ? ""
            : $" [{lead.EngagementShape}]";
        // Show "Company — Contact" when both exist; for an individual (blank
        // company) DisplayName already resolves to the contact, so don't repeat it.
        var contactFragment = !string.IsNullOrWhiteSpace(lead.CompanyName) && !string.IsNullOrEmpty(lead.ContactName)
            ? $" — {lead.ContactName}"
            : "";
        db.LogActivityAt(
            "created",
            $"Created lead: {lead.DisplayName}{contactFragment}{(string.IsNullOrEmpty(lead.Source) ? "" : $" (source: {lead.Source})")}{shapeFragment}",
            ("Lead", lead.Id));
        await db.SaveChangesAsync(cancellationToken);

        return (await repo.GetByIdAsync(lead.Id, cancellationToken))!;
    }
}
