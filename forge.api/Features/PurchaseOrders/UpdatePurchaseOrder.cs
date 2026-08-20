using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record UpdatePurchaseOrderCommand(
    int Id,
    string? Notes,
    DateTimeOffset? ExpectedDeliveryDate,
    // Optional caller-supplied PO number — editable in Draft only, gated by
    // purchase_orders.allow_manual_numbers.
    string? PONumber = null,
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

public class UpdatePurchaseOrderHandler(
    IPurchaseOrderRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db)
    : IRequestHandler<UpdatePurchaseOrderCommand>
{
    // System setting that gates caller-supplied PO numbers (shared with CreatePurchaseOrder).
    private const string AllowManualPONumbersKey = "purchase_orders.allow_manual_numbers";

    public async Task Handle(UpdatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // Notes/ExpectedDeliveryDate: editable through Submitted (legacy
        // behavior preserved). Header landed-cost fields: Draft only.
        if (po.Status != PurchaseOrderStatus.Draft && po.Status != PurchaseOrderStatus.Submitted)
            throw new InvalidOperationException("Can only update Draft or Submitted purchase orders");

        // User-settable PO number — Draft only, manual numbers enabled, and unique
        // (excluding this PO). Registry records the rename; the old number stays resolvable.
        if (request.PONumber is not null)
        {
            var newNumber = request.PONumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, po.PONumber, StringComparison.Ordinal))
            {
                if (po.Status != PurchaseOrderStatus.Draft)
                    throw new InvalidOperationException(
                        "A purchase order number can only be changed while the PO is in Draft.");
                if (!await ManualPONumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual purchase order numbers are disabled. Turn on 'purchase_orders.allow_manual_numbers' in settings to change a PO number.");
                if (await repo.PONumberExistsAsync(newNumber, po.Id, cancellationToken))
                    throw new InvalidOperationException($"Purchase order number '{newNumber}' is already in use.");
                await identifiers.IssueAsync(BusinessEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.PurchaseOrder, po.Id, newNumber, cancellationToken);
                db.LogActivityAt(
                    "updated",
                    $"Changed PO number from {po.PONumber} to {newNumber}",
                    ("PurchaseOrder", po.Id));
                po.PONumber = newNumber;
            }
        }

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

    private async Task<bool> ManualPONumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPONumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
