using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// S1 — gates quote tax-rate overrides behind a verified customer tax document.
/// A quote's tax rate may deviate from the computed default (the customer's
/// state rate per <see cref="GetTaxRateForCustomerQuery"/>) only when the
/// customer has a Verified, unexpired <see cref="CustomerTaxDocument"/> on
/// file. Registered as a concrete scoped service (same convention as
/// <see cref="CustomerPriceResolver"/>).
/// </summary>
public class TaxOverrideGuard(AppDbContext db, IMediator mediator, IClock clock)
{
    /// <summary>
    /// The computed default rate for a customer — the same source the UI's
    /// auto-fill uses (`adminService.getTaxRateForCustomer` →
    /// <see cref="GetTaxRateForCustomerHandler"/>). 0 when no rate is configured.
    /// </summary>
    public async Task<decimal> GetDefaultRateAsync(int customerId, CancellationToken ct)
    {
        var rate = await mediator.Send(new GetTaxRateForCustomerQuery(customerId), ct);
        return rate?.Rate ?? 0m;
    }

    /// <summary>
    /// Ensures the requested tax rate is allowed. When it matches the computed
    /// default, returns null (no certificate needed). When it deviates, requires
    /// a Verified, unexpired tax document for the customer — returning the
    /// qualifying document's id and recording a "quote.tax_override" audit row
    /// (flushed by the caller's SaveChangesAsync) — or throws.
    /// </summary>
    /// <exception cref="InvalidOperationException">No verified, unexpired certificate on file.</exception>
    public async Task<int?> EnsureCanOverrideAsync(
        int customerId, decimal requestedRate, decimal computedDefaultRate, CancellationToken ct)
    {
        // Round to 6 decimal places: the UI round-trips the rate through a
        // percentage (rate*100, 4dp), so absorb float noise instead of treating
        // 0.072500001 as an override of 0.0725.
        if (decimal.Round(requestedRate, 6) == decimal.Round(computedDefaultRate, 6))
            return null;

        var now = clock.UtcNow;
        var documentId = await db.CustomerTaxDocuments.AsNoTracking()
            .Where(d => d.CustomerId == customerId
                && d.Status == TaxDocumentStatus.Verified
                && (d.ExpirationDate == null || d.ExpirationDate > now))
            .OrderByDescending(d => d.VerifiedAt)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);

        if (documentId is null)
            throw new InvalidOperationException(
                "Editing the tax rate requires a verified state tax certificate on file for this customer.");

        // Audit every gated override. Anchored to the Customer because the
        // quote id isn't known yet on create; Details carries the rates + doc.
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = db.CurrentUserId ?? 0,
            Action = "quote.tax_override",
            EntityType = "Customer",
            EntityId = customerId,
            Details = JsonSerializer.Serialize(new
            {
                oldRate = computedDefaultRate,
                newRate = requestedRate,
                taxDocumentId = documentId.Value,
            }),
            CreatedAt = clock.UtcNow,
        });

        return documentId;
    }
}
