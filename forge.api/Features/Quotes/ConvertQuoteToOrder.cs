using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record ConvertQuoteToOrderCommand(int Id) : IRequest<SalesOrderListItemModel>;

public class ConvertQuoteToOrderHandler(IQuoteRepository quoteRepo, ISalesOrderRepository orderRepo)
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

        var orderNumber = await orderRepo.GenerateNextOrderNumberAsync(cancellationToken);

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = quote.CustomerId,
            QuoteId = quote.Id,
            ShippingAddressId = quote.ShippingAddressId,
            TaxRate = quote.TaxRate,
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

        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new SalesOrderListItemModel(
            order.Id, order.OrderNumber, order.CustomerId, quote.Customer.Name,
            order.Status.ToString(), null, order.Lines.Count,
            total, null, order.CreatedAt);
    }
}
