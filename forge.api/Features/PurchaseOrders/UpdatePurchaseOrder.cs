using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

public record UpdatePurchaseOrderCommand(
    int Id,
    string? Notes,
    DateTimeOffset? ExpectedDeliveryDate,
    // Bought-parts effort PR2.5 — landed-cost header fields. Editable in
    // Draft only; once Submitted, the FX snapshot is locked and these no
    // longer move (carrier costs and Incoterm renegotiation post-submit
    // would be a different workflow).
    Incoterm? Incoterm = null,
    decimal? EstimatedFreight = null,
    string? QuoteCurrency = null,
    decimal? FxRate = null,
    string? FxRateSource = null) : IRequest;

public class UpdatePurchaseOrderValidator : AbstractValidator<UpdatePurchaseOrderCommand>
{
    public UpdatePurchaseOrderValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x.EstimatedFreight)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.EstimatedFreight.HasValue);
        RuleFor(x => x.QuoteCurrency)
            .Length(3)
            .When(x => !string.IsNullOrEmpty(x.QuoteCurrency))
            .WithMessage("QuoteCurrency must be a 3-letter ISO-4217 code");
        RuleFor(x => x.FxRate)
            .GreaterThan(0m)
            .When(x => x.FxRate.HasValue);
        RuleFor(x => x.FxRateSource)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.FxRateSource));
    }
}

public class UpdatePurchaseOrderHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<UpdatePurchaseOrderCommand>
{
    public async Task Handle(UpdatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // Notes/ExpectedDeliveryDate: editable through Submitted (legacy
        // behavior preserved). Header landed-cost fields: Draft only.
        if (po.Status != PurchaseOrderStatus.Draft && po.Status != PurchaseOrderStatus.Submitted)
            throw new InvalidOperationException("Can only update Draft or Submitted purchase orders");

        if (request.Notes != null) po.Notes = request.Notes;
        if (request.ExpectedDeliveryDate.HasValue) po.ExpectedDeliveryDate = request.ExpectedDeliveryDate;

        var landedCostFieldsTouched = request.Incoterm.HasValue
            || request.EstimatedFreight.HasValue
            || !string.IsNullOrEmpty(request.QuoteCurrency)
            || request.FxRate.HasValue
            || !string.IsNullOrEmpty(request.FxRateSource);

        if (landedCostFieldsTouched)
        {
            if (po.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException(
                    "Incoterm, freight estimate, and currency fields can only be edited while the PO is in Draft.");

            if (request.Incoterm.HasValue) po.Incoterm = request.Incoterm.Value;
            if (request.EstimatedFreight.HasValue) po.EstimatedFreight = request.EstimatedFreight.Value;
            if (!string.IsNullOrEmpty(request.QuoteCurrency)) po.QuoteCurrency = request.QuoteCurrency;
            if (request.FxRate.HasValue) po.FxRate = request.FxRate.Value;
            if (!string.IsNullOrEmpty(request.FxRateSource)) po.FxRateSource = request.FxRateSource;
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
