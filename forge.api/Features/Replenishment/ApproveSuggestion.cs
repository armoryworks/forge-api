using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record ApproveSuggestionCommand(int SuggestionId, int UserId) : IRequest<ReorderSuggestionResponseModel>;

public class ApproveSuggestionHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService)
    : IRequestHandler<ApproveSuggestionCommand, ReorderSuggestionResponseModel>
{
    public async Task<ReorderSuggestionResponseModel> Handle(
        ApproveSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await db.ReorderSuggestions
            .Include(s => s.Part)
            .Include(s => s.Vendor)
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Reorder suggestion {request.SuggestionId} not found");

        if (suggestion.Status != ReorderSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion is already {suggestion.Status}");

        var vendorId = suggestion.VendorId
            ?? suggestion.Part.PreferredVendorId
            ?? throw new InvalidOperationException(
                "No vendor configured for this part. Set a preferred vendor before approving.");

        var poNumber = await poRepo.GenerateNextPONumberAsync(cancellationToken);

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = vendorId,
            Notes = $"Auto-created from reorder suggestion #{suggestion.Id} for {suggestion.Part.PartNumber}",
            // S4b provenance — replenishment-driven (MRP) PO; the approving
            // user is kept for audit alongside the suggestion reference.
            OriginSource = PoOriginSource.AutoMrp,
            OriginUserId = request.UserId > 0 ? request.UserId : null,
            OriginReference = $"Reorder suggestion #{suggestion.Id}",
        };

        po.Lines.Add(new PurchaseOrderLine
        {
            PartId = suggestion.PartId,
            Description = suggestion.Part.Description ?? suggestion.Part.Name,
            // Phase 3 / WU-10 — OrderedQuantity is decimal; preserve the
            // Math.Ceiling whole-unit purchase rounding.
            OrderedQuantity = Math.Ceiling(suggestion.SuggestedQuantity),
            UnitPrice = 0,
        });

        await poRepo.AddAsync(po, cancellationToken);
        await poRepo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

        suggestion.Status = ReorderSuggestionStatus.Approved;
        suggestion.ApprovedByUserId = request.UserId;
        suggestion.ApprovedAt = DateTimeOffset.UtcNow;
        suggestion.ResultingPurchaseOrderId = po.Id;

        await db.SaveChangesAsync(cancellationToken);

        var approvedBy = await db.Users
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        var approvedByName = approvedBy != null
            ? $"{approvedBy.LastName}, {approvedBy.FirstName}"
            : null;

        return new ReorderSuggestionResponseModel(
            suggestion.Id,
            suggestion.PartId,
            suggestion.Part.PartNumber,
            suggestion.Part.Description ?? suggestion.Part.Name,
            suggestion.VendorId,
            suggestion.Vendor?.CompanyName,
            suggestion.CurrentStock,
            suggestion.AvailableStock,
            suggestion.BurnRateDailyAvg,
            suggestion.BurnRateWindowDays,
            suggestion.DaysOfStockRemaining,
            suggestion.ProjectedStockoutDate,
            suggestion.IncomingPoQuantity,
            suggestion.EarliestPoArrival,
            suggestion.SuggestedQuantity,
            suggestion.Status,
            approvedByName,
            suggestion.ApprovedAt,
            suggestion.ResultingPurchaseOrderId,
            suggestion.DismissReason,
            null,
            suggestion.DismissedAt,
            suggestion.Notes,
            suggestion.CreatedAt);
    }
}
