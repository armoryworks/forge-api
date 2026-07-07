using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record GetQuoteByIdQuery(int Id) : IRequest<QuoteDetailResponseModel>;

public class GetQuoteByIdHandler(IQuoteRepository repo)
    : IRequestHandler<GetQuoteByIdQuery, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(GetQuoteByIdQuery request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        var subtotal = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new QuoteDetailResponseModel(
            quote.Id,
            quote.QuoteNumber ?? string.Empty,
            quote.CustomerId,
            quote.Customer.Name,
            quote.ShippingAddressId,
            quote.Status.ToString(),
            quote.SentDate,
            quote.ExpirationDate,
            quote.AcceptedDate,
            quote.Notes,
            quote.TaxRate,
            subtotal,
            subtotal * quote.TaxRate,
            subtotal * (1 + quote.TaxRate),
            quote.SalesOrder?.Id,
            quote.SalesOrder?.OrderNumber,
            quote.SourceEstimateId,
            quote.Lines.Select(l => new QuoteLineResponseModel(
                l.Id,
                l.PartId,
                l.Part?.PartNumber,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.Quantity * l.UnitPrice,
                l.LineNumber,
                l.Notes)).ToList(),
            quote.CreatedAt,
            quote.UpdatedAt,
            quote.CustomerPO);
    }
}
