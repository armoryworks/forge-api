using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads;

public sealed record DeleteLeadCommand(int Id) : IRequest;

public sealed class DeleteLeadHandler(ILeadRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<DeleteLeadCommand>
{
    public async Task Handle(DeleteLeadCommand request, CancellationToken cancellationToken)
    {
        var lead = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lead {request.Id} not found");

        if (lead.Status == LeadStatus.Converted)
            throw new InvalidOperationException("Converted leads cannot be deleted.");

        lead.DeletedAt = clock.UtcNow;
        // DeletedBy auto-stamped by AppDbContext.SetTimestamps.

        db.LogActivityAt(
            "deleted",
            $"Deleted lead: {lead.DisplayName}{(!string.IsNullOrWhiteSpace(lead.CompanyName) && !string.IsNullOrEmpty(lead.ContactName) ? $" — {lead.ContactName}" : "")}",
            ("Lead", lead.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
