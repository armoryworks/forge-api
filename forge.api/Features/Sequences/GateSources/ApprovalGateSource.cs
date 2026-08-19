using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Sequences;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>
/// Go when a terminal APPROVED ApprovalRequest exists for the referenced entity (config <c>{ "entityType", "entityId" }</c>
/// or <c>{ "fromSubject": true }</c>). Reuses the Approvals feature; re-evaluated on <c>ApprovalCompletedEvent</c>.
/// </summary>
public class ApprovalGateSource(AppDbContext db) : IGateSource
{
    public SequenceGateSourceType SourceType => SequenceGateSourceType.Approval;

    public string? CustomKey => null;

    public async Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken)
    {
        var cfg = SequenceGateConfig.Parse(context.Gate.ConfigJson);
        var type = cfg.GetBool("fromSubject") ? context.Instance.SubjectEntityType : cfg.GetString("entityType");
        var id = cfg.GetBool("fromSubject") ? context.Instance.SubjectEntityId : cfg.GetInt("entityId");
        if (string.IsNullOrEmpty(type) || id is null)
            return SequenceGateVerdictResult.NoGo("Gate config names no entity to approve");

        var latest = await db.ApprovalRequests
            .Where(r => r.EntityType == type && r.EntityId == id)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return SequenceGateVerdictResult.NoGo("No approval requested");
        return latest.Status is ApprovalRequestStatus.Approved or ApprovalRequestStatus.AutoApproved
            ? SequenceGateVerdictResult.Go($"Approved {latest.CompletedAt:u}")
            : SequenceGateVerdictResult.NoGo($"Approval {latest.Status}");
    }
}
