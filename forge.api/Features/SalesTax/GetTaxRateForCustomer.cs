using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesTax;

public record GetTaxRateForCustomerQuery(int CustomerId) : IRequest<SalesTaxRateResponseModel?>;

public class GetTaxRateForCustomerHandler(AppDbContext db)
    : IRequestHandler<GetTaxRateForCustomerQuery, SalesTaxRateResponseModel?>
{
    public async Task<SalesTaxRateResponseModel?> Handle(
        GetTaxRateForCustomerQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Find the customer's default billing/shipping state (destination-based taxation).
        // Priority: default billing → default any → first address.
        var state = await db.CustomerAddresses
            .Where(a => a.CustomerId == request.CustomerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.AddressType == AddressType.Billing || a.AddressType == AddressType.Both)
            .Select(a => a.State)
            .FirstOrDefaultAsync(cancellationToken);

        // Try to find an active rate for the customer's state.
        // "Active" = EffectiveFrom <= now AND (EffectiveTo == null OR EffectiveTo > now).
        if (!string.IsNullOrWhiteSpace(state))
        {
            var stateCode = state.Trim().ToUpper();
            var stateRate = await db.SalesTaxRates
                .AsNoTracking()
                .Where(r => r.StateCode == stateCode
                         && r.IsActive
                         && r.EffectiveFrom <= now
                         && (r.EffectiveTo == null || r.EffectiveTo > now))
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new SalesTaxRateResponseModel(
                    r.Id, r.Name, r.Code, r.StateCode, r.Rate, r.EffectiveFrom, r.EffectiveTo,
                    r.IsDefault, r.IsActive, r.Description, r.ExemptFlag, r.GlPostingAccount))
                .FirstOrDefaultAsync(cancellationToken);

            if (stateRate is not null)
                return stateRate;
        }

        // Fall back to the default rate (also effective-date aware).
        return await db.SalesTaxRates
            .AsNoTracking()
            .Where(r => r.IsDefault
                     && r.IsActive
                     && r.EffectiveFrom <= now
                     && (r.EffectiveTo == null || r.EffectiveTo > now))
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new SalesTaxRateResponseModel(
                r.Id, r.Name, r.Code, r.StateCode, r.Rate, r.EffectiveFrom, r.EffectiveTo,
                r.IsDefault, r.IsActive, r.Description, r.ExemptFlag, r.GlPostingAccount))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
