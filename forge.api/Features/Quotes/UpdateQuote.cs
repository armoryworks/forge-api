using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

public record UpdateQuoteCommand(
    int Id,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal? TaxRate,
    string? CustomerPO = null,
    // Optional editable quote number — see UpdateQuoteRequestModel.QuoteNumber.
    string? QuoteNumber = null) : IRequest;

public class UpdateQuoteValidator : AbstractValidator<UpdateQuoteCommand>
{
    public UpdateQuoteValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).When(x => x.TaxRate.HasValue);
        RuleFor(x => x.CustomerPO).MaximumLength(50).When(x => x.CustomerPO is not null);
        RuleFor(x => x.QuoteNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.QuoteNumber));
    }
}

public class UpdateQuoteHandler(
    IQuoteRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    // S1: optional/null-default so isolated unit-test constructions stay valid; DI supplies it.
    Forge.Api.Services.TaxOverrideGuard? taxGuard = null)
    : IRequestHandler<UpdateQuoteCommand>
{
    // System setting that gates caller-supplied quote numbers (shared with CreateQuote).
    private const string AllowManualQuoteNumbersKey = "quotes.allow_manual_numbers";

    public async Task Handle(UpdateQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        // User-settable quote number — Draft-only (the number is on customer-facing
        // documents once the quote is sent), gated by the manual-numbers setting, and
        // uniqueness-checked (excluding this quote). Evaluated before the general
        // Draft guard so a number change on a sent quote gets the number-specific
        // message. The DB partial-unique index is the final backstop.
        var quoteNumberChanged = false;
        if (request.QuoteNumber is not null)
        {
            var newNumber = request.QuoteNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, quote.QuoteNumber, StringComparison.Ordinal))
            {
                if (quote.Status != QuoteStatus.Draft)
                    throw new InvalidOperationException(
                        "This quote's number can only be changed while it is Draft.");
                if (!await ManualQuoteNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual quote numbers are disabled. Turn on 'quotes.allow_manual_numbers' in settings to change a quote number.");
                if (await repo.QuoteNumberExistsAsync(newNumber, quote.Id, cancellationToken))
                    throw new InvalidOperationException($"Quote number '{newNumber}' is already in use.");
                // Record the rename in the identifier registry: ensure the current number is on record
                // (covers pre-registry quotes), then supersede it — the old number stays resolvable.
                await identifiers.IssueAsync(BusinessEntityType.Quote, quote.Id, quote.QuoteNumber!, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Quote, quote.Id, newNumber, cancellationToken);
                quote.QuoteNumber = newNumber;
                quoteNumberChanged = true;
            }
        }

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

        if (quoteNumberChanged)
            db.LogActivityAt("updated", $"Quote number changed to {quote.QuoteNumber}", ("Quote", quote.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ManualQuoteNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualQuoteNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
