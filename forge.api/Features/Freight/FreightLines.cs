using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Freight;

/// <summary>
/// Shared rules for freight lines on quotes, sales orders, invoices and vendor bills: the default description,
/// the starting tax code, and the validation codes the UI maps to translated messages.
/// </summary>
public static class FreightLines
{
    /// <summary>Description a freight line gets when none is given. The UI sends its own translated text.</summary>
    public const string DefaultDescription = "Freight";

    /// <summary>A freight line names a part.</summary>
    public const string PartCode = "freight-line-part";

    /// <summary>A vendor-bill freight line names a PO line.</summary>
    public const string PoLineCode = "freight-line-po-line";

    /// <summary>A sales order's freight was already billed on another invoice.</summary>
    public const string SoFreightAlreadyBilledCode = "so-freight-already-billed";

    /// <summary>The PO a freight line clears does not exist.</summary>
    public const string PurchaseOrderNotFoundCode = "freight-purchase-order-not-found";

    /// <summary>
    /// The tax code a freight line starts with: taxable ("TAX") when the jurisdiction taxes freight and the
    /// customer is not exempt, else non-taxable ("NON").
    /// </summary>
    public static string DefaultTaxCode(bool jurisdictionTaxesFreight, bool customerExempt)
        => jurisdictionTaxesFreight && !customerExempt ? TaxableCode : InvoiceLine.NonTaxableCode;

    /// <summary>
    /// The tax code a new freight line for <paramref name="customerId"/> starts with on <paramref name="asOf"/>,
    /// from the customer's resolved jurisdiction (<see cref="GetTaxRateForCustomerHandler.ResolveAsync"/>) and exemption.
    /// An explicit <paramref name="taxable"/> wins.
    /// </summary>
    public static async Task<string> TaxCodeForAsync(
        AppDbContext db, int customerId, DateTimeOffset asOf, bool? taxable, CancellationToken ct)
    {
        if (taxable is bool explicitTaxable)
            return explicitTaxable ? TaxableCode : InvoiceLine.NonTaxableCode;

        var exempt = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.IsTaxExempt && (c.ExemptionExpiryDate == null || c.ExemptionExpiryDate >= DateOnly.FromDateTime(asOf.UtcDateTime)))
            .FirstOrDefaultAsync(ct);
        var rate = await GetTaxRateForCustomerHandler.ResolveAsync(db, customerId, asOf, ct);
        return DefaultTaxCode(rate?.FreightTaxable ?? false, exempt);
    }

    /// <summary>The explicit taxable code freight lines carry (item lines leave it null to inherit).</summary>
    public const string TaxableCode = "TAX";

    /// <summary>A 400 with <paramref name="code"/> on <paramref name="property"/>.</summary>
    public static ValidationException Failure(string property, string code, string message)
        => new(message, [new ValidationFailure(property, message) { ErrorCode = code }]);
}
