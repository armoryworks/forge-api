using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Freight;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceSourcesQuery(int CustomerId) : IRequest<InvoiceSourcesResponseModel>;

public class GetInvoiceSourcesHandler(AppDbContext db)
    : IRequestHandler<GetInvoiceSourcesQuery, InvoiceSourcesResponseModel>
{
    public const int MaxResults = 200;

    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    private static readonly SalesOrderStatus[] InvoiceableOrderStatuses =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.InProduction, SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Shipped];

    public async Task<InvoiceSourcesResponseModel> Handle(GetInvoiceSourcesQuery request, CancellationToken cancellationToken)
    {
        var salesOrders = await db.SalesOrders.AsNoTracking()
            .Where(so => so.CustomerId == request.CustomerId && InvoiceableOrderStatuses.Contains(so.Status))
            .OrderByDescending(so => so.CreatedAt)
            .Take(MaxResults)
            .Select(so => new InvoiceSourceSalesOrderResponseModel(
                so.Id, so.OrderNumber, so.Customer.Name, so.TaxRate, so.CreditTerms, so.CurrencyId, null, so.PaymentTermsId))
            .ToListAsync(cancellationToken);

        var candidates = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrder.CustomerId == request.CustomerId
                && InvoiceableOrderStatuses.Contains(s.SalesOrder.Status)
                && ShippedStatuses.Contains(s.Status)
                && !db.Invoices.Any(i => i.ShipmentId == s.Id))
            .OrderByDescending(s => s.ShippedDate ?? s.CreatedAt)
            .Select(s => new InvoiceSourceShipmentResponseModel(
                s.Id, s.ShipmentNumber, s.SalesOrderId, s.SalesOrder.OrderNumber, s.ShippedDate,
                s.Lines
                    .OrderBy(l => l.SalesOrderLine != null ? l.SalesOrderLine.LineNumber : int.MaxValue)
                    .ThenBy(l => l.Id)
                    .Select(l => new InvoiceSourceShipmentLineResponseModel(
                        l.PartId ?? (l.SalesOrderLine != null ? l.SalesOrderLine.PartId : null),
                        l.Part != null ? l.Part.PartNumber
                            : l.SalesOrderLine != null && l.SalesOrderLine.Part != null ? l.SalesOrderLine.Part.PartNumber
                            : null,
                        l.Description ?? (l.SalesOrderLine != null ? l.SalesOrderLine.Description : null)
                            ?? (l.Part != null ? l.Part.Name : string.Empty),
                        l.Quantity,
                        l.SalesOrderLine != null ? l.SalesOrderLine.UnitPrice : 0m,
                        l.SalesOrderLine == null || l.SalesOrderLine.TaxCode != InvoiceLine.NonTaxableCode,
                        l.SalesOrderLineId))
                    .ToList(),
                s.FreightCharge))
            .ToListAsync(cancellationToken);

        var fullyInvoiced = await FullyInvoicedSalesOrderIdsAsync(
            candidates.Select(s => s.SalesOrderId).Distinct().ToList(), cancellationToken);

        var shipments = candidates
            .Where(s => !fullyInvoiced.Contains(s.SalesOrderId))
            .Take(MaxResults)
            .ToList();

        var orderIds = salesOrders.Select(so => so.Id).Concat(shipments.Select(s => s.SalesOrderId)).Distinct().ToList();
        var freight = await SalesOrderFreightAsync(orderIds, cancellationToken);

        salesOrders = salesOrders
            .Select(so => freight.TryGetValue(so.Id, out var f) && f.Unbilled.Count > 0 ? so with { FreightLines = f.Unbilled } : so)
            .ToList();
        shipments = shipments.Select(s => WithFreight(s, freight.GetValueOrDefault(s.SalesOrderId))).ToList();

        return new InvoiceSourcesResponseModel(salesOrders, shipments);
    }

    /// <summary>
    /// A shipment's freight lines: the order's quoted freight while it is still unbilled (quoted freight bills
    /// once, on the first invoice), else the shipment's own freight charge when the order quotes no freight. An
    /// order that quotes freight never bills a shipment's charge as well; that case carries the
    /// <see cref="FreightLines.SoFreightAlreadyBilledCode"/> warning instead.
    /// </summary>
    internal static InvoiceSourceShipmentResponseModel WithFreight(
        InvoiceSourceShipmentResponseModel shipment, (bool HasAny, List<InvoiceSourceShipmentLineResponseModel> Unbilled) freight)
    {
        var charge = shipment.FreightCharge ?? 0m;
        if (freight.Unbilled is { Count: > 0 })
            return shipment with
            {
                Lines = [.. shipment.Lines, .. freight.Unbilled],
                Warnings = charge > 0m ? [FreightLines.SoFreightAlreadyBilledCode] : null,
            };

        if (freight.HasAny)
            return charge > 0m ? shipment with { Warnings = [FreightLines.SoFreightAlreadyBilledCode] } : shipment;

        if (charge <= 0m)
            return shipment;

        return shipment with
        {
            Lines =
            [
                .. shipment.Lines,
                new InvoiceSourceShipmentLineResponseModel(
                    null, null, FreightLines.DefaultDescription, 1m, charge, Taxable: false, LineType: DocumentLineType.Freight),
            ],
        };
    }

    private async Task<Dictionary<int, (bool HasAny, List<InvoiceSourceShipmentLineResponseModel> Unbilled)>> SalesOrderFreightAsync(
        List<int> salesOrderIds, CancellationToken ct)
    {
        if (salesOrderIds.Count == 0) return [];

        var lines = await db.SalesOrderLines.AsNoTracking()
            .Where(l => salesOrderIds.Contains(l.SalesOrderId) && l.LineType == DocumentLineType.Freight)
            .OrderBy(l => l.LineNumber)
            .Select(l => new
            {
                l.Id,
                l.SalesOrderId,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                Taxable = l.TaxCode != InvoiceLine.NonTaxableCode,
                Billed = db.InvoiceLines.Any(il => il.SalesOrderLineId == l.Id && il.Invoice.Status != InvoiceStatus.Voided),
            })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.SalesOrderId)
            .ToDictionary(
                g => g.Key,
                g => (true, g.Where(l => !l.Billed)
                    .Select(l => new InvoiceSourceShipmentLineResponseModel(
                        null, null, l.Description, l.Quantity, l.UnitPrice, l.Taxable, l.Id, DocumentLineType.Freight))
                    .ToList()));
    }

    private async Task<HashSet<int>> FullyInvoicedSalesOrderIdsAsync(List<int> salesOrderIds, CancellationToken ct)
    {
        if (salesOrderIds.Count == 0) return [];

        var shipped = await db.ShipmentLines
            .Where(sl => sl.PartId != null
                && salesOrderIds.Contains(sl.Shipment.SalesOrderId)
                && ShippedStatuses.Contains(sl.Shipment.Status))
            .GroupBy(sl => new { sl.Shipment.SalesOrderId, PartId = sl.PartId!.Value })
            .Select(g => new { g.Key.SalesOrderId, g.Key.PartId, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var invoiced = await db.InvoiceLines
            .Where(il => il.PartId != null
                && il.Invoice.SalesOrderId != null
                && il.Invoice.Status != InvoiceStatus.Voided
                && salesOrderIds.Contains(il.Invoice.SalesOrderId.Value))
            .GroupBy(il => new { SalesOrderId = il.Invoice.SalesOrderId!.Value, PartId = il.PartId!.Value })
            .Select(g => new { g.Key.SalesOrderId, g.Key.PartId, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => (x.SalesOrderId, x.PartId), x => x.Qty, ct);

        return shipped
            .GroupBy(s => s.SalesOrderId)
            .Where(g => g.All(s => invoiced.GetValueOrDefault((s.SalesOrderId, s.PartId)) >= s.Qty))
            .Select(g => g.Key)
            .ToHashSet();
    }
}
