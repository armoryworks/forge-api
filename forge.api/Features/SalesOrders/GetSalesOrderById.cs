using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record GetSalesOrderByIdQuery(int Id) : IRequest<SalesOrderDetailResponseModel>;

public class GetSalesOrderByIdHandler(ISalesOrderRepository repo, AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetSalesOrderByIdQuery, SalesOrderDetailResponseModel>
{
    public async Task<SalesOrderDetailResponseModel> Handle(GetSalesOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var so = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        // Collect all assignee IDs from jobs across all lines
        var assigneeIds = so.Lines
            .SelectMany(l => l.Jobs)
            .Where(j => j.AssigneeId.HasValue)
            .Select(j => j.AssigneeId!.Value)
            .Distinct()
            .ToList();

        // Build assignee name lookup
        var assigneeNames = new Dictionary<int, string>();
        if (assigneeIds.Count > 0)
        {
            assigneeNames = await userManager.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.LastName + ", " + u.FirstName,
                    cancellationToken);
        }

        // Query returns via indirect join: CustomerReturn → OriginalJob → SalesOrderLine → SalesOrder
        var returns = await db.CustomerReturns
            .Where(cr => cr.OriginalJob != null
                      && cr.OriginalJob.SalesOrderLineId != null
                      && cr.OriginalJob.SalesOrderLine!.SalesOrderId == request.Id)
            .Include(cr => cr.OriginalJob)
            .Include(cr => cr.ReworkJob)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var subtotal = so.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new SalesOrderDetailResponseModel(
            so.Id,
            so.OrderNumber,
            so.CustomerId,
            so.Customer.Name,
            so.QuoteId,
            so.Quote?.QuoteNumber,
            so.ShippingAddressId,
            so.BillingAddressId,
            so.Status.ToString(),
            so.CreditTerms?.ToString(),
            so.ConfirmedDate,
            so.RequestedDeliveryDate,
            so.CustomerPO,
            so.Notes,
            so.TaxRate,
            subtotal,
            subtotal * so.TaxRate,
            subtotal * (1 + so.TaxRate),
            so.Lines.Select(l => new SalesOrderLineResponseModel(
                l.Id,
                l.PartId,
                l.Part?.PartNumber,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.Quantity * l.UnitPrice,
                l.LineNumber,
                l.ShippedQuantity,
                l.RemainingQuantity,
                l.IsFullyShipped,
                l.Notes,
                l.Jobs.Select(j => new SalesOrderLineJobModel(
                    j.Id,
                    j.JobNumber,
                    j.Title,
                    j.CurrentStage?.Name,
                    j.AssigneeId.HasValue && assigneeNames.TryGetValue(j.AssigneeId.Value, out var name) ? name : null,
                    j.Priority.ToString(),
                    j.DueDate,
                    j.IsArchived)).ToList()
            )).ToList(),
            so.Shipments.Select(s => new SalesOrderShipmentModel(
                s.Id,
                s.ShipmentNumber,
                s.Status.ToString(),
                s.Carrier,
                s.TrackingNumber,
                s.ShippedDate,
                s.DeliveredDate,
                s.ShippingCost ?? 0m,
                s.Weight,
                s.Notes,
                s.Lines.Select(sl => new SalesOrderShipmentLineModel(
                    sl.Id,
                    sl.PartId,
                    sl.Part?.PartNumber,
                    sl.Quantity,
                    sl.Notes,
                    sl.SalesOrderLineId)).ToList(),
                s.Packages.Select(p => new SalesOrderShipmentPackageModel(
                    p.Id,
                    p.TrackingNumber,
                    p.Carrier,
                    p.Weight,
                    p.Length,
                    p.Width,
                    p.Height,
                    p.Status)).ToList()
            )).ToList(),
            returns.Select(cr => new SalesOrderReturnModel(
                cr.Id,
                cr.ReturnNumber,
                cr.Status.ToString(),
                cr.Reason,
                cr.ReturnDate,
                cr.OriginalJobId,
                cr.OriginalJob?.JobNumber,
                cr.ReworkJobId,
                cr.ReworkJob?.JobNumber,
                cr.InspectionNotes)).ToList(),
            so.CreatedAt,
            so.UpdatedAt);
    }
}
