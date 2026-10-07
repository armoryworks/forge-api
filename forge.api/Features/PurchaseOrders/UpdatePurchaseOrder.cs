using System.Globalization;

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

    private static readonly PurchaseOrderStatus[] ExpectedDateEditableStatuses =
        [PurchaseOrderStatus.Acknowledged, PurchaseOrderStatus.PartiallyReceived];

    public async Task Handle(UpdatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // Notes: editable through Submitted. ExpectedDeliveryDate: editable
        // through PartiallyReceived. Header landed-cost fields: Draft only.
        var landedCostFieldsTouched = request.Incoterm.HasValue
            || request.EstimatedFreight.HasValue
            || !string.IsNullOrEmpty(request.QuoteCurrency)
            || request.FxRate.HasValue
            || !string.IsNullOrEmpty(request.FxRateSource);

        if (po.Status != PurchaseOrderStatus.Draft && po.Status != PurchaseOrderStatus.Submitted)
        {
            if (!ExpectedDateEditableStatuses.Contains(po.Status))
                throw new InvalidOperationException("Can only update Draft or Submitted purchase orders");

            var poNumberChanged = !string.IsNullOrWhiteSpace(request.PONumber)
                && !string.Equals(request.PONumber.Trim(), po.PONumber, StringComparison.Ordinal);
            var notesChanged = request.Notes is not null && request.Notes != po.Notes;
            if (poNumberChanged || notesChanged || landedCostFieldsTouched)
                throw new InvalidOperationException(
                    "Once a purchase order is acknowledged, only its expected delivery date can be changed.");
        }

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

        var changedFields = new List<string>();
        string? expectedDateChange = null;

        if (request.Notes != null && request.Notes != po.Notes)
        {
            po.Notes = request.Notes;
            changedFields.Add("notes");
        }

        if (request.ExpectedDeliveryDate.HasValue && request.ExpectedDeliveryDate != po.ExpectedDeliveryDate)
        {
            expectedDateChange = po.ExpectedDeliveryDate.HasValue
                ? $"Changed expected delivery from {FormatDate(po.ExpectedDeliveryDate.Value)} to {FormatDate(request.ExpectedDeliveryDate.Value)}"
                : $"Set expected delivery to {FormatDate(request.ExpectedDeliveryDate.Value)}";
            po.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
            changedFields.Add("expectedDeliveryDate");
        }

        if (landedCostFieldsTouched)
        {
            if (po.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException(
                    "Incoterm, freight estimate, and currency fields can only be edited while the PO is in Draft.");

            if (request.Incoterm.HasValue && request.Incoterm.Value != po.Incoterm)
            {
                po.Incoterm = request.Incoterm.Value;
                changedFields.Add("incoterm");
            }
            if (request.EstimatedFreight.HasValue && request.EstimatedFreight.Value != po.EstimatedFreight)
            {
                po.EstimatedFreight = request.EstimatedFreight.Value;
                changedFields.Add("estimatedFreight");
            }
            if (!string.IsNullOrEmpty(request.QuoteCurrency) && request.QuoteCurrency != po.QuoteCurrency)
            {
                po.QuoteCurrency = request.QuoteCurrency;
                changedFields.Add("quoteCurrency");
            }
            if (request.FxRate.HasValue && request.FxRate.Value != po.FxRate)
            {
                po.FxRate = request.FxRate.Value;
                changedFields.Add("fxRate");
            }
            if (!string.IsNullOrEmpty(request.FxRateSource) && request.FxRateSource != po.FxRateSource)
            {
                po.FxRateSource = request.FxRateSource;
                changedFields.Add("fxRateSource");
            }
        }

        if (changedFields.Count > 0)
        {
            var description = changedFields.Count == 1 && expectedDateChange is not null
                ? expectedDateChange
                : $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}";
            db.LogActivityAt("updated", description, ("PurchaseOrder", po.Id));
        }

        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

    private async Task<bool> ManualPONumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPONumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
