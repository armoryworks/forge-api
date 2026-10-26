using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.CreditMemos;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerReturns;

public record ResolveCustomerReturnCommand(
    int Id,
    string? InspectionNotes,
    ReturnDisposition? Disposition = null,
    decimal? Quantity = null,
    int? RestockLocationId = null,
    string? LotNumber = null,
    bool IssueCredit = true,
    decimal? CreditAmount = null,
    decimal? RestockingFeePercent = null) : IRequest;

public class ResolveCustomerReturnValidator : AbstractValidator<ResolveCustomerReturnCommand>
{
    public ResolveCustomerReturnValidator()
    {
        RuleFor(x => x.InspectionNotes).MaximumLength(2000).When(x => x.InspectionNotes != null);
        RuleFor(x => x.Disposition).IsInEnum().When(x => x.Disposition.HasValue);
        RuleFor(x => x.Quantity).GreaterThan(0m).When(x => x.Quantity.HasValue);
        RuleFor(x => x.RestockLocationId).NotNull()
            .When(x => x.Disposition == ReturnDisposition.ReturnToStock)
            .WithMessage("Choose the location the returned goods go back into.")
            .WithErrorCode(ReturnResolveCodes.LocationRequired);
        RuleFor(x => x.LotNumber).MaximumLength(100);
        RuleFor(x => x.CreditAmount).GreaterThanOrEqualTo(0m).When(x => x.CreditAmount.HasValue);
        RuleFor(x => x.RestockingFeePercent).InclusiveBetween(0m, 100m).When(x => x.RestockingFeePercent.HasValue);
    }
}

/// <summary>
/// Resolves a customer return. With a disposition it also moves the goods (see <see cref="ReturnDispositionService"/>)
/// and, unless told not to, issues the return's credit memo: the returned quantity at the invoiced price (or the
/// credit amount given), tax at the invoice's rate, less any restocking fee, auto-applied to the invoice's balance.
/// Channel returns never issue a memo here: the channel settlement books their refund.
/// </summary>
public class ResolveCustomerReturnHandler(
    AppDbContext db,
    ReturnDispositionService? dispositions = null,
    CreditMemoIssuer? issuer = null,
    IClock? clock = null)
    : IRequestHandler<ResolveCustomerReturnCommand>
{
    public async Task Handle(ResolveCustomerReturnCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        var ret = await db.CustomerReturns
            .Include(r => r.OriginalJob)
            .Include(r => r.SalesOrderLine)
            .Include(r => r.InvoiceLine)
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        if (ret.Status == CustomerReturnStatus.Closed)
            throw new InvalidOperationException("Cannot resolve a closed return");

        if (request.InspectionNotes != null)
            ret.InspectionNotes = request.InspectionNotes;

        var description = $"Customer return {ret.ReturnNumber} resolved.";
        if (request.Disposition is ReturnDisposition disposition)
        {
            if (ret.Disposition is not null)
                throw CreditMemoRules.Failure("Disposition", ReturnResolveCodes.AlreadyDisposed,
                    $"Return {ret.ReturnNumber} already has a disposition ({ret.Disposition}).");

            var quantity = request.Quantity ?? ret.Quantity
                ?? throw CreditMemoRules.Failure("Quantity", ReturnResolveCodes.QuantityRequired,
                    $"Return {ret.ReturnNumber} has no returned quantity; enter the quantity being resolved.");
            if (ret.Quantity is decimal returned && quantity > returned)
                throw CreditMemoRules.Failure("Quantity", ReturnResolveCodes.QuantityExceedsReturned,
                    string.Create(CultureInfo.InvariantCulture, $"Only {returned:0.####} was returned on {ret.ReturnNumber}."));

            await LinkInvoiceLineAsync(ret, ct);

            if (disposition == ReturnDisposition.ReturnToStock)
                await EnsureRestockableAsync(ret, request, ct);

            if (dispositions is not null)
                await dispositions.ApplyAsync(ret, disposition, quantity, request.RestockLocationId, request.LotNumber, ct);
            else
                ret.Disposition = disposition;

            if (request.IssueCredit && ret.ChannelId is null && issuer is not null)
                await IssueReturnCreditAsync(ret, quantity, request, ct);

            description = string.Create(CultureInfo.InvariantCulture,
                $"Customer return {ret.ReturnNumber} resolved: {DispositionLabel(disposition)}, quantity {quantity:0.####}.");
        }

        ret.Status = CustomerReturnStatus.Resolved;
        db.LogActivityAt("resolved", description, ("CustomerReturn", ret.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    private async Task LinkInvoiceLineAsync(CustomerReturn ret, CancellationToken ct)
    {
        if (ret.InvoiceLineId is not null)
            return;

        var orderLineId = ret.SalesOrderLineId ?? ret.OriginalJob?.SalesOrderLineId;
        if (orderLineId is not int soLineId)
            return;

        var line = await db.InvoiceLines
            .Where(l => l.SalesOrderLineId == soLineId
                && l.Invoice.CustomerId == ret.CustomerId
                && l.Invoice.Status != InvoiceStatus.Draft && l.Invoice.Status != InvoiceStatus.Voided)
            .OrderByDescending(l => l.InvoiceId)
            .FirstOrDefaultAsync(ct);
        if (line is null)
            return;

        ret.InvoiceLine = line;
        ret.InvoiceLineId = line.Id;
        ret.InvoiceId = line.InvoiceId;
    }

    private async Task EnsureRestockableAsync(CustomerReturn ret, ResolveCustomerReturnCommand request, CancellationToken ct)
    {
        var partId = ReturnDispositionService.PartIdFor(ret)
            ?? throw CreditMemoRules.Failure("Disposition", ReturnResolveCodes.NoPart,
                $"Return {ret.ReturnNumber} has no part to put back into stock. Choose scrap, rework or no return.");
        if (!await db.StorageLocations.AnyAsync(l => l.Id == request.RestockLocationId, ct))
            throw new KeyNotFoundException($"Location {request.RestockLocationId} not found");

        var traceability = await db.Parts.AsNoTracking()
            .Where(p => p.Id == partId).Select(p => p.TraceabilityType).FirstOrDefaultAsync(ct);
        if (traceability == TraceabilityType.Lot && string.IsNullOrWhiteSpace(request.LotNumber))
            throw CreditMemoRules.Failure("LotNumber", ReturnResolveCodes.LotRequired,
                "This part is lot-tracked; enter the lot the returned goods go back under.");
    }

    private async Task IssueReturnCreditAsync(CustomerReturn ret, decimal quantity, ResolveCustomerReturnCommand request, CancellationToken ct)
    {
        var invoiceLine = ret.InvoiceLine;
        Invoice? invoice = null;
        if (invoiceLine is not null)
            invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceLine.InvoiceId, ct);

        var goods = request.CreditAmount
            ?? (invoiceLine is not null ? Money.Round(quantity * invoiceLine.UnitPrice) : (decimal?)null)
            ?? throw CreditMemoRules.Failure("CreditAmount", ReturnResolveCodes.CreditAmountRequired,
                $"Return {ret.ReturnNumber} is not linked to an invoice line; enter the credit amount.");
        if (goods <= 0m)
            return;

        var feePercent = request.RestockingFeePercent ?? await RestockingFeePercentAsync(ct);
        var fee = Money.Round(goods * feePercent / 100m);
        var unitPrice = invoiceLine is not null && request.CreditAmount is null
            ? invoiceLine.UnitPrice
            : Math.Round(goods / quantity, 4, MidpointRounding.AwayFromZero);
        var taxCode = invoiceLine is { IsTaxable: false } ? InvoiceLine.NonTaxableCode : null;

        var reason = $"Return {ret.ReturnNumber}: {ret.Reason}";
        var functional = await new Forge.Api.Services.Fx.DocumentCurrencyResolver(db).GetFunctionalCurrencyIdAsync(ct);
        var memo = new CreditMemo
        {
            CreditMemoNumber = await CreditMemoRules.NextCreditMemoNumberAsync(db, ct),
            CustomerId = ret.CustomerId,
            CurrencyId = invoice?.CurrencyId ?? functional,
            FxRate = invoice?.FxRate ?? 1m,
            InvoiceId = invoice?.Id,
            CustomerReturnId = ret.Id,
            ReasonType = CreditMemoReasonType.Return,
            Reason = reason.Length > 500 ? reason[..500] : reason,
            CreditDate = clock?.UtcNow ?? ret.ReturnDate,
            TaxRate = invoice?.TaxRate ?? 0m,
            SalesTaxRateId = invoice?.SalesTaxRateId,
            CreatedByUserId = db.CurrentUserId,
            Status = CreditMemoStatus.Draft,
        };
        memo.Lines.Add(new CreditMemoLine
        {
            LineNumber = 1,
            LineType = CreditMemoLineType.Item,
            PartId = ReturnDispositionService.PartIdFor(ret),
            InvoiceLineId = invoiceLine?.Id,
            Description = invoiceLine?.Description ?? reason,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TaxCode = taxCode,
        });
        if (fee > 0m)
            memo.Lines.Add(new CreditMemoLine
            {
                LineNumber = 2,
                LineType = CreditMemoLineType.RestockingFee,
                Description = string.Create(CultureInfo.InvariantCulture, $"Restocking fee {feePercent:0.##}%"),
                Quantity = 1m,
                UnitPrice = fee,
                TaxCode = taxCode,
            });

        db.CreditMemos.Add(memo);
        await db.SaveChangesAsync(ct);
        ret.CreditMemoId = memo.Id;
        db.LogActivityAt("created", $"Created credit memo {memo.CreditMemoNumber} for return {ret.ReturnNumber}",
            ("CreditMemo", memo.Id), ("CustomerReturn", ret.Id));

        await issuer!.IssueAsync(memo, ct);
    }

    private async Task<decimal> RestockingFeePercentAsync(CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == CreditMemoSettings.RestockingFeePercentKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) && pct is > 0m and <= 100m
            ? pct
            : 0m;
    }

    private static string DispositionLabel(ReturnDisposition disposition) => disposition switch
    {
        ReturnDisposition.ReturnToStock => "returned to stock",
        ReturnDisposition.Scrap => "scrapped",
        ReturnDisposition.Rework => "sent to rework",
        _ => "no goods returned",
    };
}
