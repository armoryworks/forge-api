using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record ApproveBulkSuggestionsCommand(List<int> SuggestionIds, int UserId) : IRequest<BulkApproveResult>;

public record BulkApproveResult(int ApprovedCount, int SkippedCount, List<int> CreatedPoIds);

public class ApproveBulkSuggestionsHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService)
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
        var now = DateTimeOffset.UtcNow;

        // Group suggestions by vendor to consolidate lines per vendor where possible
        var byVendor = suggestions
            .GroupBy(s => s.VendorId ?? s.Part.PreferredVendorId)
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
            var po = new PurchaseOrder
            {
                PONumber = poNumber,
                VendorId = vendorGroup.Key.Value,
                Notes = $"Bulk auto-created from {vendorGroup.Count()} reorder suggestion(s)",
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

            createdPoIds.Add(po.Id);

            foreach (var s in vendorGroup)
            {
                s.Status = ReorderSuggestionStatus.Approved;
                s.ApprovedByUserId = request.UserId;
                s.ApprovedAt = now;
                s.ResultingPurchaseOrderId = po.Id;
                approvedCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new BulkApproveResult(approvedCount, skippedCount, createdPoIds);
    }
}
