using FluentValidation;
using MediatR;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record CreateQuoteCommand(
    int CustomerId,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal TaxRate,
    List<CreateQuoteLineModel> Lines,
    string? CustomerPO = null,
    // Optional caller-supplied quote number — see CreateQuoteRequestModel.QuoteNumber.
    string? QuoteNumber = null) : IRequest<QuoteListItemModel>;

public class CreateQuoteValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.CustomerPO).MaximumLength(50).When(x => x.CustomerPO is not null);
        // Matches the quotes.quote_number column (varchar(20)). Uniqueness is
        // checked in the handler since it needs a DB lookup.
        RuleFor(x => x.QuoteNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.QuoteNumber));
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
    Forge.Api.Services.CustomerPriceResolver? priceResolver = null,
    // S1: optional/null-default for the same reason; DI supplies it.
    Forge.Api.Services.TaxOverrideGuard? taxGuard = null,
    // Optional/null-default for the same reason; DI supplies both.
    ISystemSettingRepository? systemSettings = null,
    IBusinessIdentifierService? identifiers = null)
    : IRequestHandler<CreateQuoteCommand, QuoteListItemModel>
{
    // System setting that gates caller-supplied quote numbers. Stored as "true"/"false".
    private const string AllowManualQuoteNumbersKey = "quotes.allow_manual_numbers";

    public async Task<QuoteListItemModel> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken);
        // Phase 3 H2 / WU-12: customer-active check on quote create.
        ActiveCheck.EnsureActive(customer, "Customer", "customerId", request.CustomerId);

        // S1: a tax rate deviating from the customer's computed default requires
        // a verified tax certificate — the qualifying doc is stamped on the quote.
        int? taxDocumentId = null;
        if (taxGuard is not null)
        {
            var defaultRate = await taxGuard.GetDefaultRateAsync(request.CustomerId, cancellationToken);
            taxDocumentId = await taxGuard.EnsureCanOverrideAsync(
                request.CustomerId, request.TaxRate, defaultRate, cancellationToken);
        }

        var quoteNumber = await ResolveQuoteNumberAsync(request, cancellationToken);

        var quote = new Quote
        {
            QuoteNumber = quoteNumber,
            CustomerId = request.CustomerId,
            ShippingAddressId = request.ShippingAddressId,
            ExpirationDate = request.ExpirationDate,
            Notes = request.Notes,
            TaxRate = request.TaxRate,
            TaxDocumentId = taxDocumentId,
            CustomerPO = string.IsNullOrWhiteSpace(request.CustomerPO) ? null : request.CustomerPO.Trim(),
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

        // Record the number in the identifier registry (history + resolution).
        if (identifiers is not null)
            await identifiers.IssueAsync(BusinessEntityType.Quote, quote.Id, quote.QuoteNumber!, cancellationToken);

        var total = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new QuoteListItemModel(
            quote.Id, quote.QuoteNumber, quote.CustomerId, customer.Name,
            quote.Status.ToString(), quote.Lines.Count, total,
            quote.ExpirationDate, quote.CreatedAt);
    }

    // Uses a caller-supplied quote number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential "QT" number.
    private async Task<string> ResolveQuoteNumberAsync(CreateQuoteCommand request, CancellationToken ct)
    {
        var supplied = request.QuoteNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualQuoteNumbersAllowedAsync(ct))
        {
            if (await repo.QuoteNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Quote number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GenerateNextQuoteNumberAsync(ct);
    }

    private async Task<bool> ManualQuoteNumbersAllowedAsync(CancellationToken ct)
    {
        if (systemSettings is null) return false;
        var setting = await systemSettings.FindByKeyAsync(AllowManualQuoteNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
