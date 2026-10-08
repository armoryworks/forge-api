using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Replenishment;

public record ApproveBulkSuggestionsCommand(List<int> SuggestionIds, int UserId) : IRequest<BulkApproveResult>;

public record BulkApproveResult(int ApprovedCount, int SkippedCount, List<int> CreatedPoIds, List<int> CreatedJobIds);

public class ApproveBulkSuggestionsHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService,
    IPartSourcingResolver sourcingResolver,
    IMediator mediator,
    IClock clock)
    : IRequestHandler<ApproveBulkSuggestionsCommand, BulkApproveResult>
{
    public async Task<BulkApproveResult> Handle(
        ApproveBulkSuggestionsCommand request, CancellationToken cancellationToken)
    {
        var suggestions = await db.ReorderSuggestions
            .Include(s => s.Part)
            .Where(s => request.SuggestionIds.Contains(s.Id)
                && s.Status == ReorderSuggestionStatus.Pending)
            .ToListAsync(cancellationToken);

        var approvedCount = 0;
        var skippedCount = request.SuggestionIds.Count - suggestions.Count;
        var createdPoIds = new List<int>();
        var createdJobIds = new List<int>();
        var approvedIds = new List<int>();
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var makeSuggestions = suggestions
            .Where(s => s.Part.ProcurementSource == ProcurementSource.Make)
            .ToList();
        var buySuggestions = suggestions.Except(makeSuggestions).ToList();

        if (makeSuggestions.Count > 0)
        {
            var resolvedTrack = await ProductionTrackResolver.ResolveAsync(db, cancellationToken);
            if (resolvedTrack is null)
            {
                skippedCount += makeSuggestions.Count;
            }
            else
            {
                var track = resolvedTrack.Value.Track;
                var makePartIds = makeSuggestions.Select(s => s.PartId).Distinct().ToList();
                var routingByPart = await ReplenishmentPlanning.LoadRoutingsAsync(db, makePartIds, cancellationToken);

                foreach (var s in makeSuggestions)
                {
                    var routing = routingByPart.TryGetValue(s.PartId, out var ops) ? ops : [];
                    var leadTimeDays = OperationTimeMath.MakeLeadTimeDays(routing, s.SuggestedQuantity);

                    var job = await mediator.Send(new CreateJobCommand(
                        Title: $"{s.Part.PartNumber} x {s.SuggestedQuantity.ToString("0.####", CultureInfo.InvariantCulture)}",
                        Description: null,
                        TrackTypeId: track.Id,
                        AssigneeId: null,
                        CustomerId: null,
                        Priority: JobPriority.Normal,
                        DueDate: now.AddDays(leadTimeDays),
                        PartId: s.PartId,
                        Quantity: s.SuggestedQuantity), cancellationToken);

                    createdJobIds.Add(job.Id);
                    s.ResultingJobId = job.Id;
                    Approve(s);
                }
            }
        }

        var sourcingByPart = buySuggestions.Count == 0
            ? new Dictionary<int, PartSourcingValues>()
            : await sourcingResolver.ResolveManyAsync(
                buySuggestions.Select(s => s.PartId).Distinct().ToList(), cancellationToken);

        // Group suggestions by vendor to consolidate lines per vendor where possible
        var byVendor = buySuggestions
            .GroupBy(s => (sourcingByPart.TryGetValue(s.PartId, out var sv) ? sv.PreferredVendorId : null)
                ?? s.VendorId
                ?? s.Part.PreferredVendorId)
            .ToList();

        foreach (var vendorGroup in byVendor)
        {
            if (vendorGroup.Key is null)
            {
                // No vendor — skip all in this group
                skippedCount += vendorGroup.Count();
                continue;
            }

            var poNumber = await poRepo.GenerateNextPONumberAsync(cancellationToken);

            // S4b provenance — replenishment-driven (MRP) PO. OriginReference
            // is varchar(200); truncate defensively for large batches.
            var originReference = $"Reorder suggestions {string.Join(", ", vendorGroup.Select(s => $"#{s.Id}"))}";
            if (originReference.Length > 200)
                originReference = originReference[..200];

            var po = new PurchaseOrder
            {
                PONumber = poNumber,
                VendorId = vendorGroup.Key.Value,
                Notes = $"Bulk auto-created from {vendorGroup.Count()} reorder suggestion(s)",
                OriginSource = PoOriginSource.AutoMrp,
                OriginUserId = request.UserId > 0 ? request.UserId : null,
                OriginReference = originReference,
            };

            foreach (var s in vendorGroup)
            {
                po.Lines.Add(new PurchaseOrderLine
                {
                    PartId = s.PartId,
                    Description = s.Part.Description ?? s.Part.Name,
                    // Phase 3 / WU-10 — OrderedQuantity decimal; keep Math.Ceiling.
                    OrderedQuantity = Math.Ceiling(s.SuggestedQuantity),
                    UnitPrice = 0,
                });
            }

            await poRepo.AddAsync(po, cancellationToken);
            await poRepo.SaveChangesAsync(cancellationToken);

            await barcodeService.CreateBarcodeAsync(
                BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

            var vendorName = await db.Vendors
                .Where(v => v.Id == po.VendorId)
                .Select(v => v.CompanyName)
                .FirstOrDefaultAsync(cancellationToken);

            db.LogActivityAt(
                "created",
                $"Created purchase order {po.PONumber} for {vendorName} from {vendorGroup.Count()} reorder suggestion(s)",
                ("PurchaseOrder", po.Id));

            createdPoIds.Add(po.Id);

            foreach (var s in vendorGroup)
            {
                s.VendorId = po.VendorId;
                s.ResultingPurchaseOrderId = po.Id;
                Approve(s);
            }
        }

        await ReplenishmentAssignee.CloseTasksAsync(
            db, approvedIds, FollowUpStatus.Completed, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new BulkApproveResult(approvedCount, skippedCount, createdPoIds, createdJobIds);

        void Approve(ReorderSuggestion s)
        {
            s.Status = ReorderSuggestionStatus.Approved;
            s.ApprovedByUserId = request.UserId;
            s.ApprovedAt = now;
            approvedIds.Add(s.Id);
            approvedCount++;
        }
    }
}
