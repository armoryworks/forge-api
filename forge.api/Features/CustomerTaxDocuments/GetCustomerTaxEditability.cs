using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record GetCustomerTaxEditabilityQuery(int CustomerId) : IRequest<CustomerTaxEditabilityResponseModel>;

/// <summary>
/// Read-side twin of <see cref="TaxOverrideGuard"/>: tells the quote, sales-order and invoice
/// screens whether the caller may change a document's tax rate for this customer (only the
/// <see cref="TaxOverrideGuard.OverrideRoles"/>), and which verified, unexpired certificate is on
/// file for the certificate-backed exemption reasons. When no tax configuration applies to the customer
/// (<c>RateDetermined</c> false) a typed rate is not an override, so every caller may enter it.
/// </summary>
public class GetCustomerTaxEditabilityHandler(AppDbContext db, IClock clock, IHttpContextAccessor? httpContext = null)
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

        var computed = await GetTaxRateForCustomerHandler.ResolveAsync(db, request.CustomerId, now, cancellationToken);
        var rateDetermined = computed is not null
            || await TaxOverrideGuard.IsExemptTodayAsync(db, request.CustomerId, now, cancellationToken);

        var canEdit = !rateDetermined
            || (httpContext?.HttpContext?.User is { } user && TaxOverrideGuard.OverrideRoles.Any(user.IsInRole));
        var reason = canEdit
            ? null
            : "Only an Admin, Manager, Office Manager or Controller can change the tax rate from the customer's rate.";

        return new CustomerTaxEditabilityResponseModel(
            canEdit, reason, active?.Id, active?.StateCode, active?.ExpirationDate, rateDetermined);
    }
}
