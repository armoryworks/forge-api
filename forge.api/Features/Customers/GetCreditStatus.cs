using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Customers;

public record GetCreditStatusQuery(int CustomerId) : IRequest<CreditStatusResponseModel>;

public class GetCreditStatusHandler(AppDbContext db) : IRequestHandler<GetCreditStatusQuery, CreditStatusResponseModel>
{
    public async Task<CreditStatusResponseModel> Handle(GetCreditStatusQuery request, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, request.CustomerId, ct);
        var openArBalance = exposure.OpenArBalance;
        var pendingOrdersTotal = exposure.PendingOrdersTotal;

        var totalExposure = openArBalance + pendingOrdersTotal;
        var availableCredit = (customer.CreditLimit ?? 0) - totalExposure;
        var utilizationPercent = customer.CreditLimit > 0
            ? totalExposure / customer.CreditLimit.Value * 100
            : 0;
        var isOverLimit = customer.CreditLimit.HasValue && totalExposure > customer.CreditLimit.Value;

        var riskLevel = customer.IsOnCreditHold ? CreditRisk.OnHold
            : isOverLimit ? CreditRisk.High
            : utilizationPercent > 80 ? CreditRisk.Medium
            : CreditRisk.Low;

        // Phase 3 / WU-14 / H3 / P4-OVERPAY — surface unapplied credits.
        // Pull every customer payment with applications, then filter
        // client-side to those where unapplied > 0. Computing
        // (Amount - sum(applications)) in SQL is fine but shaping it
        // through EF Core requires a sub-aggregate; the customer payment
        // count for any one customer is small enough that the in-memory
        // filter is the simpler implementation.
        var paymentRows = await db.Payments
            .AsNoTracking()
            .Where(p => p.CustomerId == request.CustomerId)
            .Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                p.Amount,
                p.ReferenceNumber,
                Applied = p.Applications.Sum(a => (decimal?)a.Amount) ?? 0m,
            })
            .ToListAsync(ct);

        var unappliedCredits = paymentRows
            .Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                Unapplied = p.Amount - p.Applied,
                p.ReferenceNumber,
            })
            .Where(p => p.Unapplied > 0)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => new UnappliedCreditDetail(
                p.Id, p.PaymentNumber, p.PaymentDate, p.Unapplied, p.ReferenceNumber))
            .ToList();
        var unappliedCreditAmount = unappliedCredits.Sum(c => c.Amount);

        return new CreditStatusResponseModel
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CreditLimit = customer.CreditLimit,
            OpenArBalance = openArBalance,
            PendingOrdersTotal = pendingOrdersTotal,
            TotalExposure = totalExposure,
            AvailableCredit = availableCredit,
            UtilizationPercent = utilizationPercent,
            IsOnHold = customer.IsOnCreditHold,
            HoldReason = customer.CreditHoldReason,
            IsOverLimit = isOverLimit,
            RiskLevel = riskLevel,
            UnappliedCreditAmount = unappliedCreditAmount,
            UnappliedCredits = unappliedCredits,
        };
    }
}
