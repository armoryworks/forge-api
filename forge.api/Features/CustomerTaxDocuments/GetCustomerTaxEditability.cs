using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record GetCustomerTaxEditabilityQuery(int CustomerId) : IRequest<CustomerTaxEditabilityResponseModel>;

/// <summary>
/// S1 — read-side twin of <see cref="Forge.Api.Services.TaxOverrideGuard"/>:
/// tells the quote dialog whether the tax-rate field should be editable for
/// this customer. Editable iff a Verified, unexpired CustomerTaxDocument exists.
/// </summary>
public class GetCustomerTaxEditabilityHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetCustomerTaxEditabilityQuery, CustomerTaxEditabilityResponseModel>
{
    public async Task<CustomerTaxEditabilityResponseModel> Handle(
        GetCustomerTaxEditabilityQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var active = await db.CustomerTaxDocuments.AsNoTracking()
            .Where(d => d.CustomerId == request.CustomerId
                && d.Status == TaxDocumentStatus.Verified
                && (d.ExpirationDate == null || d.ExpirationDate > now))
            .OrderByDescending(d => d.VerifiedAt)
            .Select(d => new { d.Id, d.StateCode, d.ExpirationDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (active is not null)
            return new CustomerTaxEditabilityResponseModel(
                true, null, active.Id, active.StateCode, active.ExpirationDate);

        // Distinguish "expired" from "never verified" for a more useful reason.
        var hasExpired = await db.CustomerTaxDocuments.AsNoTracking()
            .AnyAsync(d => d.CustomerId == request.CustomerId
                && d.Status == TaxDocumentStatus.Verified
                && d.ExpirationDate != null && d.ExpirationDate <= now, cancellationToken);

        var reason = hasExpired
            ? "The customer's verified state tax certificate has expired."
            : "No verified state tax certificate is on file for this customer.";

        return new CustomerTaxEditabilityResponseModel(false, reason, null, null, null);
    }
}
