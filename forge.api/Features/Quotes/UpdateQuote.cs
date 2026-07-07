using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Quotes;

public record UpdateQuoteCommand(
    int Id,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal? TaxRate,
    string? CustomerPO = null) : IRequest;

public class UpdateQuoteValidator : AbstractValidator<UpdateQuoteCommand>
{
    public UpdateQuoteValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).When(x => x.TaxRate.HasValue);
        RuleFor(x => x.CustomerPO).MaximumLength(50).When(x => x.CustomerPO is not null);
    }
}

public class UpdateQuoteHandler(
    IQuoteRepository repo,
    // S1: optional/null-default so isolated unit-test constructions stay valid; DI supplies it.
    Forge.Api.Services.TaxOverrideGuard? taxGuard = null)
    : IRequestHandler<UpdateQuoteCommand>
{
    public async Task Handle(UpdateQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only Draft quotes can be updated");

        if (request.ShippingAddressId.HasValue) quote.ShippingAddressId = request.ShippingAddressId;
        if (request.ExpirationDate.HasValue) quote.ExpirationDate = request.ExpirationDate;
        if (request.Notes != null) quote.Notes = request.Notes;
        if (request.TaxRate.HasValue)
        {
            quote.TaxRate = request.TaxRate.Value;

            // S1: a rate deviating from the customer's computed default requires
            // a verified tax certificate; matching the default clears the stamp.
            if (taxGuard is not null)
            {
                var defaultRate = await taxGuard.GetDefaultRateAsync(quote.CustomerId, cancellationToken);
                quote.TaxDocumentId = await taxGuard.EnsureCanOverrideAsync(
                    quote.CustomerId, request.TaxRate.Value, defaultRate, cancellationToken);
            }
        }
        // Empty string clears the PO; null leaves it untouched (patch semantics).
        if (request.CustomerPO != null)
            quote.CustomerPO = string.IsNullOrWhiteSpace(request.CustomerPO) ? null : request.CustomerPO.Trim();

        await repo.SaveChangesAsync(cancellationToken);
    }
}
