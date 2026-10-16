using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record GetQuoteByIdQuery(int Id) : IRequest<QuoteDetailResponseModel>;

public class GetQuoteByIdHandler(IQuoteRepository repo, IStandardCostResolver? costs = null)
    : IRequestHandler<GetQuoteByIdQuery, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(GetQuoteByIdQuery request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        var subtotal = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);

        var unitCosts = new Dictionary<int, QuoteLineCostBreakdownModel>();
        if (costs is not null)
        {
            foreach (var partId in quote.Lines.Where(l => l.PartId.HasValue).Select(l => l.PartId!.Value).Distinct())
            {
                var elements = await costs.ResolveAsync(partId, cancellationToken);
                if (elements.Total > 0m)
                    unitCosts[partId] = new QuoteLineCostBreakdownModel(
                        elements.Labor, elements.Overhead, elements.Material, 0m, elements.Total);
            }
        }

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
                l.Notes,
                CostBreakdownFor(l, unitCosts),
                EffectiveMarginFor(l, unitCosts))).ToList(),
            quote.CreatedAt,
            quote.UpdatedAt,
            quote.CustomerPO);
    }

    private static QuoteLineCostBreakdownModel? CostBreakdownFor(
        QuoteLine line, IReadOnlyDictionary<int, QuoteLineCostBreakdownModel> unitCosts)
        => line.PartId is int partId && unitCosts.TryGetValue(partId, out var cost) ? cost : null;

    private static decimal? EffectiveMarginFor(
        QuoteLine line, IReadOnlyDictionary<int, QuoteLineCostBreakdownModel> unitCosts)
        => line.UnitPrice > 0m && CostBreakdownFor(line, unitCosts) is { } cost
            ? Math.Round((line.UnitPrice - cost.Total) / line.UnitPrice, 4)
            : null;
}
