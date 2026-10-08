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

public record ApproveSuggestionCommand(int SuggestionId, int UserId) : IRequest<ReorderSuggestionResponseModel>;

public class ApproveSuggestionHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService,
    IPartSourcingResolver sourcingResolver,
    IMediator mediator,
    IClock clock)
    : IRequestHandler<ApproveSuggestionCommand, ReorderSuggestionResponseModel>
{
    public async Task<ReorderSuggestionResponseModel> Handle(
        ApproveSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await db.ReorderSuggestions
            .Include(s => s.Part)
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Reorder suggestion {request.SuggestionId} not found");

        if (suggestion.Status != ReorderSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion is already {suggestion.Status}");

        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (suggestion.Part.ProcurementSource == ProcurementSource.Make)
            suggestion.ResultingJobId = await CreateJobAsync(suggestion, now, cancellationToken);
        else
            suggestion.ResultingPurchaseOrderId = await CreatePurchaseOrderAsync(suggestion, request.UserId, cancellationToken);

        suggestion.Status = ReorderSuggestionStatus.Approved;
        suggestion.ApprovedByUserId = request.UserId;
        suggestion.ApprovedAt = now;

        await ReplenishmentAssignee.CloseTasksAsync(
            db, [suggestion.Id], FollowUpStatus.Completed, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var result = await mediator.Send(new GetReorderSuggestionsQuery(null, suggestion.Id), cancellationToken);
        return result.Single();
    }

    private async Task<int> CreateJobAsync(ReorderSuggestion suggestion, DateTimeOffset now, CancellationToken ct)
    {
        var (track, _) = await ProductionTrackResolver.ResolveAsync(db, ct)
            ?? throw new InvalidOperationException(
                "No active production track is set up. Add one before approving make suggestions.");

        var routing = await db.Operations
            .Where(o => o.PartId == suggestion.PartId)
            .ToListAsync(ct);
        var leadTimeDays = OperationTimeMath.MakeLeadTimeDays(routing, suggestion.SuggestedQuantity);

        var job = await mediator.Send(new CreateJobCommand(
            Title: $"{suggestion.Part.PartNumber} x {suggestion.SuggestedQuantity.ToString("0.####", CultureInfo.InvariantCulture)}",
            Description: null,
            TrackTypeId: track.Id,
            AssigneeId: null,
            CustomerId: null,
            Priority: JobPriority.Normal,
            DueDate: now.AddDays(leadTimeDays),
            PartId: suggestion.PartId,
            Quantity: suggestion.SuggestedQuantity), ct);

        return job.Id;
    }

    private async Task<int> CreatePurchaseOrderAsync(ReorderSuggestion suggestion, int userId, CancellationToken ct)
    {
        var sourcing = await sourcingResolver.ResolveAsync(suggestion.PartId, ct);
        var vendorId = sourcing.PreferredVendorId
            ?? suggestion.VendorId
            ?? suggestion.Part.PreferredVendorId
            ?? throw new InvalidOperationException(
                "No vendor configured for this part. Set a preferred vendor before approving.");

        var poNumber = await poRepo.GenerateNextPONumberAsync(ct);

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = vendorId,
            Notes = $"Auto-created from reorder suggestion #{suggestion.Id} for {suggestion.Part.PartNumber}",
            // S4b provenance — replenishment-driven (MRP) PO; the approving
            // user is kept for audit alongside the suggestion reference.
            OriginSource = PoOriginSource.AutoMrp,
            OriginUserId = userId > 0 ? userId : null,
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

        await poRepo.AddAsync(po, ct);
        await poRepo.SaveChangesAsync(ct);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, ct);

        var vendorName = await db.Vendors
            .Where(v => v.Id == vendorId)
            .Select(v => v.CompanyName)
            .FirstOrDefaultAsync(ct);

        db.LogActivityAt(
            "created",
            $"Created purchase order {po.PONumber} for {vendorName} from reorder suggestion #{suggestion.Id}",
            ("PurchaseOrder", po.Id));

        suggestion.VendorId = vendorId;
        return po.Id;
    }
}
