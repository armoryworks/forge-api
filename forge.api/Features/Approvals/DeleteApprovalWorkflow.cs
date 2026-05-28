using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

// F-11-APPR-02: retire (soft-delete) a stale approval workflow. Workflows were create/edit
// only, so obsolete ones accumulated with no way to remove them. Soft delete keeps history
// (and any in-flight requests that reference the workflow) intact.
public sealed record DeleteApprovalWorkflowCommand(int Id) : IRequest;

public sealed class DeleteApprovalWorkflowHandler(AppDbContext db) : IRequestHandler<DeleteApprovalWorkflowCommand>
{
    public async Task Handle(DeleteApprovalWorkflowCommand request, CancellationToken ct)
    {
        var workflow = await db.ApprovalWorkflows.FirstOrDefaultAsync(w => w.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Approval workflow {request.Id} not found.");

        workflow.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
