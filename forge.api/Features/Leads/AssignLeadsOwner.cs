using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Notifications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads;

/// <summary>
/// Hand a set of leads to one rep (or back to the open queue with <paramref name="AssignedToUserId"/> 0).
/// Only the owner changes; every other field, the account link included, is left as it is. Returns
/// how many leads changed owner.
/// </summary>
public record AssignLeadsOwnerCommand(IReadOnlyList<int> LeadIds, int AssignedToUserId) : IRequest<int>;

public class AssignLeadsOwnerValidator : AbstractValidator<AssignLeadsOwnerCommand>
{
    public AssignLeadsOwnerValidator()
    {
        RuleFor(x => x.LeadIds).NotEmpty().Must(ids => ids.Count <= 500)
            .WithMessage("Assign at most 500 leads at a time.").WithErrorCode("too-many-leads");
        RuleFor(x => x.AssignedToUserId).GreaterThanOrEqualTo(0);
    }
}

public class AssignLeadsOwnerHandler(AppDbContext db, IAssignmentNotifier? assignmentNotifier = null)
    : IRequestHandler<AssignLeadsOwnerCommand, int>
{
    public async Task<int> Handle(AssignLeadsOwnerCommand request, CancellationToken ct)
    {
        var owner = await LeadOwner.ResolveAsync(db, request.AssignedToUserId, ct);
        var ids = request.LeadIds.Distinct().ToList();
        var leads = await db.Leads.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        if (leads.Count != ids.Count)
            throw new KeyNotFoundException("One or more leads were not found.");

        var changedLeads = new List<Core.Entities.Lead>();
        foreach (var lead in leads.Where(l => l.AssignedToUserId != owner.UserId))
        {
            lead.AssignedToUserId = owner.UserId;
            db.LogActivityAt(
                "updated",
                owner.UserId.HasValue ? $"Updated 1 field: owner: {owner.Name}" : "Updated 1 field: owner: cleared",
                ("Lead", lead.Id));
            changedLeads.Add(lead);
        }

        await db.SaveChangesAsync(ct);

        if (assignmentNotifier is not null && owner.UserId is int ownerId)
        {
            foreach (var lead in changedLeads)
                await assignmentNotifier.LeadAssignedAsync(lead.Id, lead.DisplayName, ownerId, ct);
        }

        return changedLeads.Count;
    }
}
