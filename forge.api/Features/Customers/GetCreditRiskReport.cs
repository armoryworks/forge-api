using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Customers;

public record GetCreditRiskReportQuery : IRequest<List<CreditStatusResponseModel>>;

public class GetCreditRiskReportHandler(AppDbContext db) : IRequestHandler<GetCreditRiskReportQuery, List<CreditStatusResponseModel>>
{
    public async Task<List<CreditStatusResponseModel>> Handle(GetCreditRiskReportQuery request, CancellationToken ct)
    {
        var customers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CreditLimit != null || c.IsOnCreditHold)
            .Select(c => new { c.Id, c.Name, c.CreditLimit, c.IsOnCreditHold, c.CreditHoldReason })
            .ToListAsync(ct);

        if (customers.Count == 0)
            return [];

        var customerIds = customers.Select(c => c.Id).ToList();

        var exposures = await CustomerCreditExposure.ForCustomersAsync(db, customerIds, ct);

        var results = customers.Select(c =>
        {
            var openAr = exposures[c.Id].OpenArBalance;
            var pendingOrders = exposures[c.Id].PendingOrdersTotal;
            var totalExposure = openAr + pendingOrders;
            var availableCredit = (c.CreditLimit ?? 0) - totalExposure;
            var utilization = c.CreditLimit > 0
                ? totalExposure / c.CreditLimit.Value * 100
                : 0;
            var isOverLimit = c.CreditLimit.HasValue && totalExposure > c.CreditLimit.Value;

            var riskLevel = c.IsOnCreditHold ? CreditRisk.OnHold
                : isOverLimit ? CreditRisk.High
                : utilization > 80 ? CreditRisk.Medium
                : CreditRisk.Low;

            return new CreditStatusResponseModel
            {
                CustomerId = c.Id,
                CustomerName = c.Name,
                CreditLimit = c.CreditLimit,
                OpenArBalance = openAr,
                PendingOrdersTotal = pendingOrders,
                TotalExposure = totalExposure,
                AvailableCredit = availableCredit,
                UtilizationPercent = utilization,
                IsOnHold = c.IsOnCreditHold,
                HoldReason = c.CreditHoldReason,
                IsOverLimit = isOverLimit,
                RiskLevel = riskLevel,
            };
        })
        .OrderByDescending(r => r.UtilizationPercent)
        .ToList();

        return results;
    }
}
