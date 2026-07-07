using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

public record ConvertQuoteToOrderCommand(int Id) : IRequest<SalesOrderListItemModel>;

// S2: db is optional / null-default so the existing isolated (mock-repo) unit-test
// constructions stay valid — the DI path always supplies it. When present, it drives
// the payment-schedule re-link below.
public class ConvertQuoteToOrderHandler(IQuoteRepository quoteRepo, ISalesOrderRepository orderRepo, AppDbContext? db = null)
    : IRequestHandler<ConvertQuoteToOrderCommand, SalesOrderListItemModel>
{
    public async Task<SalesOrderListItemModel> Handle(ConvertQuoteToOrderCommand request, CancellationToken cancellationToken)
    {
        var quote = await quoteRepo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Accepted)
            throw new InvalidOperationException("Only Accepted quotes can be converted to orders");

        if (quote.SalesOrder != null)
            throw new InvalidOperationException("Quote has already been converted to an order");

        // AUDIT-S4 / BE20-C: a zero-line quote (e.g. estimate-derived) must not become a
        // live, confirmable empty order. Reject before generating an order number.
        if (quote.Lines.Count == 0)
            throw new InvalidOperationException("Cannot convert a quote with no lines to an order");

        var orderNumber = await orderRepo.GenerateNextOrderNumberAsync(cancellationToken);

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = quote.CustomerId,
            QuoteId = quote.Id,
            ShippingAddressId = quote.ShippingAddressId,
            TaxRate = quote.TaxRate,
            // AUDIT-S3: preserve the quote's Notes onto the order (was dropped on convert).
            Notes = quote.Notes,
            // F7: the customer's PO reference captured at quote time follows the order.
            CustomerPO = quote.CustomerPO,
        };

        var lineNumber = 1;
        foreach (var ql in quote.Lines)
        {
            order.Lines.Add(new SalesOrderLine
            {
                PartId = ql.PartId,
                Description = ql.Description,
                Quantity = ql.Quantity,
                UnitPrice = ql.UnitPrice,
                LineNumber = lineNumber++,
                Notes = ql.Notes,
            });
        }

        quote.Status = QuoteStatus.ConvertedToOrder;

        await orderRepo.AddAsync(order, cancellationToken);
        await quoteRepo.SaveChangesAsync(cancellationToken);

        // S2: a pre-payment schedule defined on the quote follows the order — the SAME
        // row is re-linked (SalesOrderId set), never cloned, and goes Active. Runs after
        // the save so order.Id is assigned.
        if (db is not null)
        {
            var schedule = await db.PaymentSchedules.FirstOrDefaultAsync(
                s => s.QuoteId == quote.Id && s.Status != PaymentScheduleStatus.Cancelled,
                cancellationToken);
            if (schedule is not null)
            {
                schedule.SalesOrderId = order.Id;
                schedule.Status = PaymentScheduleStatus.Active;
                db.LogActivityAt(
                    "payment-schedule-activated",
                    $"Payment schedule re-linked to order {order.OrderNumber} and activated",
                    ("Quote", quote.Id), ("SalesOrder", order.Id));
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new SalesOrderListItemModel(
            order.Id, order.OrderNumber, order.CustomerId, quote.Customer.Name,
            order.Status.ToString(), order.CustomerPO, order.Lines.Count,
            total, null, order.CreatedAt,
            SalesOrderId: order.Id, JobId: null);
    }
}
