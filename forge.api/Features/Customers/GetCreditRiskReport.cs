using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

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

        // Batch: open AR balances per customer
        var invoiceData = await db.Invoices
            .AsNoTracking()
            .Where(i => customerIds.Contains(i.CustomerId)
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Voided)
            .GroupBy(i => i.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Balance = g.Sum(i => i.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + i.TaxRate)
                    - i.PaymentApplications.Sum(pa => pa.Amount)),
            })
            .ToListAsync(ct);

        var arByCustomer = invoiceData.ToDictionary(x => x.CustomerId, x => x.Balance);

        // Batch: pending sales order totals per customer
        var orderData = await db.SalesOrders
            .AsNoTracking()
            .Where(so => customerIds.Contains(so.CustomerId)
                && so.Status != SalesOrderStatus.Cancelled
                && so.Status != SalesOrderStatus.Completed)
            .GroupBy(so => so.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Total = g.Sum(so => so.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + so.TaxRate)),
            })
            .ToListAsync(ct);

        var ordersByCustomer = orderData.ToDictionary(x => x.CustomerId, x => x.Total);

        var results = customers.Select(c =>
        {
            var openAr = arByCustomer.GetValueOrDefault(c.Id);
            var pendingOrders = ordersByCustomer.GetValueOrDefault(c.Id);
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
