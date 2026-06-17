using FluentValidation;
using MediatR;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record CreateQuoteCommand(
    int CustomerId,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal TaxRate,
    List<CreateQuoteLineModel> Lines) : IRequest<QuoteListItemModel>;

public class CreateQuoteValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateQuoteHandler(
    IQuoteRepository repo,
    ICustomerRepository customerRepo,
    IPartRepository partRepo,
    // AUDIT-19-S1: optional/null-default so isolated unit-test constructions stay valid; DI supplies it.
    Forge.Api.Services.CustomerPriceResolver? priceResolver = null)
    : IRequestHandler<CreateQuoteCommand, QuoteListItemModel>
{
    public async Task<QuoteListItemModel> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken);
        // Phase 3 H2 / WU-12: customer-active check on quote create.
        ActiveCheck.EnsureActive(customer, "Customer", "customerId", request.CustomerId);

        var quoteNumber = await repo.GenerateNextQuoteNumberAsync(cancellationToken);

        var quote = new Quote
        {
            QuoteNumber = quoteNumber,
            CustomerId = request.CustomerId,
            ShippingAddressId = request.ShippingAddressId,
            ExpirationDate = request.ExpirationDate,
            Notes = request.Notes,
            TaxRate = request.TaxRate,
        };

        var lineNumber = 1;
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            // Phase 3 H2 / WU-12: part-active check on quote line. Skip when
            // PartId is null (free-form quote line) — same shape as SO.
            if (line.PartId is int partId && partId > 0)
            {
                var part = await partRepo.FindAsync(partId, cancellationToken);
                ActiveCheck.EnsureActive(part, "Part", $"lines[{i}].partId", partId);
            }

            // AUDIT-19-S1: resolve an unset (0) catalog-part price from the customer's price list.
            var unitPrice = line.UnitPrice;
            if (unitPrice == 0m && priceResolver is not null && line.PartId is int pricePartId && pricePartId > 0)
                unitPrice = await priceResolver.ResolveUnitPriceAsync(request.CustomerId, pricePartId, cancellationToken)
                            ?? unitPrice;

            quote.Lines.Add(new QuoteLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
            });
        }

        await repo.AddAsync(quote, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        var total = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new QuoteListItemModel(
            quote.Id, quote.QuoteNumber, quote.CustomerId, customer.Name,
            quote.Status.ToString(), quote.Lines.Count, total,
            quote.ExpirationDate, quote.CreatedAt);
    }
}
