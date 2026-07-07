using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

/// <summary>
/// S4b provenance backfill — idempotent, admin-only classification of
/// pre-provenance purchase orders. Only rows still carrying the untouched
/// default triple (OriginSource=Manual, OriginUserId=null,
/// OriginReference=null) are candidates, so re-running is a no-op for
/// anything already classified (by this endpoint or by a creation path):
///   1. Provider != null            → ExternalIntegration (reference = provider)
///   2. converted auto-PO suggestion → AutoMrp (reference = suggestion id)
///   3. everything else stays Manual.
/// </summary>
public record BackfillPurchaseOrderOriginsCommand : IRequest<BackfillPurchaseOrderOriginsResponseModel>;

public record BackfillPurchaseOrderOriginsResponseModel(
    int ExternalIntegrationCount,
    int AutoMrpCount,
    int RemainingManualCount);

public class BackfillPurchaseOrderOriginsHandler(
    AppDbContext db,
    ILogger<BackfillPurchaseOrderOriginsHandler> logger)
    : IRequestHandler<BackfillPurchaseOrderOriginsCommand, BackfillPurchaseOrderOriginsResponseModel>
{
    public async Task<BackfillPurchaseOrderOriginsResponseModel> Handle(
        BackfillPurchaseOrderOriginsCommand request, CancellationToken cancellationToken)
    {
        // Idempotence gate — only untouched-default rows are candidates.
        var candidates = db.PurchaseOrders
            .Where(po => po.OriginSource == PoOriginSource.Manual
                && po.OriginUserId == null
                && po.OriginReference == null);

        var totalCandidates = await candidates.CountAsync(cancellationToken);

        // 1. External ingestion — a Provider stamp means the row came from an
        //    accounting-sync / external system. Provider wins over the
        //    suggestion match below.
        var externalPos = await candidates
            .Where(po => po.Provider != null)
            .ToListAsync(cancellationToken);

        foreach (var po in externalPos)
        {
            po.OriginSource = PoOriginSource.ExternalIntegration;
            po.OriginReference = po.Provider;
        }

        // 2. Auto-PO conversions — the suggestion table records which PO each
        //    suggestion was converted into. Min(id) picks a deterministic
        //    reference when several suggestions converged on one PO.
        var suggestionByPoId = await db.AutoPoSuggestions
            .AsNoTracking()
            .Where(s => s.ConvertedPurchaseOrderId != null)
            .GroupBy(s => s.ConvertedPurchaseOrderId!.Value)
            .Select(g => new { PoId = g.Key, SuggestionId = g.Min(s => s.Id) })
            .ToDictionaryAsync(x => x.PoId, x => x.SuggestionId, cancellationToken);

        var convertedPoIds = suggestionByPoId.Keys.ToList();
        var autoMrpPos = await candidates
            .Where(po => po.Provider == null && convertedPoIds.Contains(po.Id))
            .ToListAsync(cancellationToken);

        foreach (var po in autoMrpPos)
        {
            po.OriginSource = PoOriginSource.AutoMrp;
            po.OriginReference = $"Auto-PO suggestion #{suggestionByPoId[po.Id]}";
        }

        // Activity logging: deliberately skipped. This is a bulk,
        // administrative, idempotent backfill — per-row ActivityLog entries
        // would flood every PO's Activity tab with rows that describe no
        // domain event, and a single summary row has no meaningful entity to
        // anchor to (ActivityLog is polymorphic EntityType/EntityId). The
        // Serilog line below plus the returned counts are the audit trail.
        await db.SaveChangesAsync(cancellationToken);

        var remainingManual = totalCandidates - externalPos.Count - autoMrpPos.Count;

        logger.LogInformation(
            "[S4b] PO origin backfill — {External} → ExternalIntegration, {AutoMrp} → AutoMrp, {Manual} left Manual (of {Total} candidates)",
            externalPos.Count, autoMrpPos.Count, remainingManual, totalCandidates);

        return new BackfillPurchaseOrderOriginsResponseModel(
            externalPos.Count, autoMrpPos.Count, remainingManual);
    }
}
