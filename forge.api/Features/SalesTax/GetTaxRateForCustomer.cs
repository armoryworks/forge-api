using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesTax;

public record GetTaxRateForCustomerQuery(int CustomerId) : IRequest<SalesTaxRateResponseModel?>;

public class GetTaxRateForCustomerHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetTaxRateForCustomerQuery, SalesTaxRateResponseModel?>
{
    public async Task<SalesTaxRateResponseModel?> Handle(
        GetTaxRateForCustomerQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        var exempt = customer?.IsTaxExemptOn(DateOnly.FromDateTime(now.UtcDateTime)) ?? false;

        var effective = db.SalesTaxRates.AsNoTracking()
            .Where(r => r.IsActive
                     && r.EffectiveFrom <= now
                     && (r.EffectiveTo == null || r.EffectiveTo > now)
                     && (exempt ? r.ExemptFlag || r.Rate == 0m : !r.ExemptFlag));

        if (customer?.DefaultTaxCodeId is int defaultTaxCodeId)
        {
            var customerRate = await Project(effective.Where(r => r.Id == defaultTaxCodeId))
                .FirstOrDefaultAsync(cancellationToken);
            if (customerRate is not null)
                return customerRate;
        }

        var state = await db.CustomerAddresses
            .Where(a => a.CustomerId == request.CustomerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.AddressType == AddressType.Billing || a.AddressType == AddressType.Both)
            .Select(a => a.State)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(state))
        {
            var stateCode = state.Trim().ToUpper();
            var stateRate = await Project(Prefer(effective.Where(r => r.StateCode == stateCode), exempt))
                .FirstOrDefaultAsync(cancellationToken);

            if (stateRate is not null)
                return stateRate;
        }

        return await Project(Prefer(effective.Where(r => r.IsDefault), exempt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<SalesTaxRate> Prefer(IQueryable<SalesTaxRate> rates, bool exempt)
        => rates
            .OrderByDescending(r => exempt && r.ExemptFlag)
            .ThenByDescending(r => r.EffectiveFrom)
            .ThenByDescending(r => r.IsDefault)
            .ThenByDescending(r => r.Rate)
            .ThenByDescending(r => r.Id);

    private static IQueryable<SalesTaxRateResponseModel> Project(IQueryable<SalesTaxRate> rates)
        => rates.Select(r => new SalesTaxRateResponseModel(
            r.Id, r.Name, r.Code, r.StateCode, r.Rate, r.EffectiveFrom, r.EffectiveTo,
            r.IsDefault, r.IsActive, r.Description, r.ExemptFlag, r.GlPostingAccount));
}
