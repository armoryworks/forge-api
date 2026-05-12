using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Workflows;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Runs;

/// <summary>
/// Workflow Pattern Phase 3 — Current user's in-flight runs (not completed,
/// not abandoned). Powers the "resume" prompts on the dashboard / list pages.
/// </summary>
public record ListActiveWorkflowRunsQuery : IRequest<IReadOnlyList<WorkflowRunResponseModel>>;

public class ListActiveWorkflowRunsHandler(AppDbContext db)
    : IRequestHandler<ListActiveWorkflowRunsQuery, IReadOnlyList<WorkflowRunResponseModel>>
{
    public async Task<IReadOnlyList<WorkflowRunResponseModel>> Handle(
        ListActiveWorkflowRunsQuery request, CancellationToken ct)
    {
        var userId = db.CurrentUserId ?? 0;
        if (userId == 0) return Array.Empty<WorkflowRunResponseModel>();

        var rows = await db.WorkflowRuns
            .AsNoTracking()
            .Where(r => r.StartedByUserId == userId
                        && r.CompletedAt == null
                        && r.AbandonedAt == null)
            .OrderByDescending(r => r.LastActivityAt)
            .ToListAsync(ct);

        return rows.Select(r => r.ToResponse()).ToList();
    }
}
