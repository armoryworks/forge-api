using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetConsignmentStockSummaryQuery(int? VendorId, int? CustomerId) : IRequest<ConsignmentStockSummaryResponseModel>;

public class GetConsignmentStockSummaryHandler(AppDbContext db) : IRequestHandler<GetConsignmentStockSummaryQuery, ConsignmentStockSummaryResponseModel>
{
    public async Task<ConsignmentStockSummaryResponseModel> Handle(GetConsignmentStockSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = db.ConsignmentAgreements.AsNoTracking().AsQueryable();

        if (request.VendorId.HasValue)
            query = query.Where(a => a.VendorId == request.VendorId.Value);

        if (request.CustomerId.HasValue)
            query = query.Where(a => a.CustomerId == request.CustomerId.Value);

        var agreements = await query
            .Include(a => a.Vendor)
            .Include(a => a.Customer)
            .Include(a => a.Part)
            .ToListAsync(cancellationToken);
        var ids = agreements.Select(a => a.Id).ToList();

        var onHand = await db.BinContents.AsNoTracking()
            .Where(c => c.ConsignmentAgreementId != null && ids.Contains(c.ConsignmentAgreementId.Value) && c.RemovedAt == null)
            .GroupBy(c => c.ConsignmentAgreementId!.Value)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(c => c.Quantity) })
            .ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);
        var transactions = await db.ConsignmentTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.AgreementId))
            .Select(t => new { t.AgreementId, t.Type, t.Quantity, t.ExtendedAmount, t.VendorBillId })
            .ToListAsync(cancellationToken);
        var byAgreement = transactions.ToLookup(t => t.AgreementId);

        var rows = agreements.Select(a =>
        {
            var own = byAgreement[a.Id].ToList();
            var quantity = a.Direction == ConsignmentDirection.Inbound
                ? onHand.GetValueOrDefault(a.Id)
                : own.Where(t => t.Type is ConsignmentTransactionType.Receipt or ConsignmentTransactionType.Adjustment or ConsignmentTransactionType.Reconciliation).Sum(t => t.Quantity)
                    - own.Where(t => t.Type is ConsignmentTransactionType.Consumption or ConsignmentTransactionType.Return).Sum(t => t.Quantity);
            var unbilled = own.Where(t => t.Type == ConsignmentTransactionType.Consumption && t.VendorBillId == null).ToList();
            return new ConsignmentAgreementStockResponseModel
            {
                AgreementId = a.Id,
                Direction = a.Direction,
                Status = a.Status,
                OwnerName = a.Direction == ConsignmentDirection.Inbound ? a.Vendor?.CompanyName ?? "" : a.Customer?.Name ?? "",
                PartId = a.PartId,
                PartNumber = a.Part.PartNumber,
                AgreedUnitPrice = a.AgreedUnitPrice,
                OnHandQuantity = quantity,
                OnHandValue = Math.Round(quantity * a.AgreedUnitPrice, 2, MidpointRounding.AwayFromZero),
                UnbilledQuantity = unbilled.Sum(t => t.Quantity),
                UnbilledValue = unbilled.Sum(t => t.ExtendedAmount),
                MinStockQuantity = a.MinStockQuantity,
                MaxStockQuantity = a.MaxStockQuantity,
                BelowMinimum = a.MinStockQuantity is decimal min && quantity < min,
                AboveMaximum = a.MaxStockQuantity is decimal max && quantity > max,
            };
        }).OrderBy(r => r.OwnerName).ThenBy(r => r.PartNumber).ToList();

        var byOwner = agreements
            .GroupBy(a => a.Direction == ConsignmentDirection.Inbound
                ? new { OwnerType = "Vendor", OwnerId = a.VendorId ?? 0, OwnerName = a.Vendor?.CompanyName ?? "" }
                : new { OwnerType = "Customer", OwnerId = a.CustomerId ?? 0, OwnerName = a.Customer?.Name ?? "" })
            .Select(g =>
            {
                var members = g.Select(a => a.Id).ToHashSet();
                var groupRows = rows.Where(r => members.Contains(r.AgreementId)).ToList();
                return new ConsignmentStockByOwnerResponseModel
                {
                    OwnerType = g.Key.OwnerType,
                    OwnerId = g.Key.OwnerId,
                    OwnerName = g.Key.OwnerName,
                    AgreementCount = g.Count(),
                    TotalQuantity = groupRows.Sum(r => r.OnHandQuantity),
                    TotalValue = groupRows.Sum(r => r.OnHandValue),
                };
            })
            .ToList();

        return new ConsignmentStockSummaryResponseModel
        {
            TotalAgreements = agreements.Count,
            ActiveAgreements = agreements.Count(a => a.Status == ConsignmentAgreementStatus.Active),
            TotalConsignedValue = byOwner.Sum(o => o.TotalValue),
            TotalUnbilledValue = rows.Sum(r => r.UnbilledValue),
            ByOwner = byOwner,
            Agreements = rows,
        };
    }
}
