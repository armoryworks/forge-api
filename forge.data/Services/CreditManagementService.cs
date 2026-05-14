using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class CreditManagementService(AppDbContext db) : ICreditManagementService
{
    public async Task<CreditStatusResponseModel> GetCreditStatusAsync(int customerId, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found");

        var openArBalance = await db.Invoices
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Voided)
            .Select(i => new
            {
                LineTotal = i.Lines.Sum(l => l.Quantity * l.UnitPrice),
                TaxRate = i.TaxRate,
                Paid = i.PaymentApplications.Sum(pa => pa.Amount),
            })
            .ToListAsync(ct);

        var arBalance = openArBalance.Sum(i => i.LineTotal * (1 + i.TaxRate) - i.Paid);

        var pendingOrders = await db.SalesOrders
            .AsNoTracking()
            .Where(so => so.CustomerId == customerId
                && so.Status != SalesOrderStatus.Cancelled
                && so.Status != SalesOrderStatus.Completed)
            .Select(so => new
            {
                LineTotal = so.Lines.Sum(l => l.Quantity * l.UnitPrice),
                TaxRate = so.TaxRate,
            })
            .ToListAsync(ct);

        var pendingTotal = pendingOrders.Sum(so => so.LineTotal * (1 + so.TaxRate));
        var totalExposure = arBalance + pendingTotal;
        var availableCredit = (customer.CreditLimit ?? 0) - totalExposure;
        var utilizationPercent = customer.CreditLimit > 0
            ? totalExposure / customer.CreditLimit.Value * 100
            : 0;
        var isOverLimit = customer.CreditLimit.HasValue && totalExposure > customer.CreditLimit.Value;

        var riskLevel = customer.IsOnCreditHold ? CreditRisk.OnHold
            : isOverLimit ? CreditRisk.High
            : utilizationPercent > 80 ? CreditRisk.Medium
            : CreditRisk.Low;

        return new CreditStatusResponseModel
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CreditLimit = customer.CreditLimit,
            OpenArBalance = arBalance,
            PendingOrdersTotal = pendingTotal,
            TotalExposure = totalExposure,
            AvailableCredit = availableCredit,
            UtilizationPercent = utilizationPercent,
            IsOnHold = customer.IsOnCreditHold,
            HoldReason = customer.CreditHoldReason,
            IsOverLimit = isOverLimit,
            RiskLevel = riskLevel,
        };
    }

    public async Task<bool> CheckCreditForOrderAsync(int customerId, decimal orderAmount, CancellationToken ct)
    {
        var status = await GetCreditStatusAsync(customerId, ct);
        if (status.IsOnHold) return false;
        if (!status.CreditLimit.HasValue) return true;
        return (status.TotalExposure + orderAmount) <= status.CreditLimit.Value;
    }

    public async Task PlaceHoldAsync(int customerId, int placedById, string reason, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found");

        customer.IsOnCreditHold = true;
        customer.CreditHoldReason = reason;
        customer.CreditHoldAt = DateTimeOffset.UtcNow;
        customer.CreditHoldById = placedById;

        var hold = new CreditHold
        {
            CustomerId = customerId,
            Reason = CreditHoldReason.ManualHold,
            Notes = reason,
            PlacedById = placedById,
            PlacedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        db.CreditHolds.Add(hold);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseHoldAsync(int customerId, int releasedById, string? releaseNotes, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found");

        customer.IsOnCreditHold = false;
        customer.CreditHoldReason = null;
        customer.CreditHoldAt = null;
        customer.CreditHoldById = null;

        var activeHold = await db.CreditHolds
            .FirstOrDefaultAsync(h => h.CustomerId == customerId && h.IsActive, ct);

        if (activeHold != null)
        {
            activeHold.IsActive = false;
            activeHold.ReleasedById = releasedById;
            activeHold.ReleasedAt = DateTimeOffset.UtcNow;
            activeHold.ReleaseNotes = releaseNotes;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CreditStatusResponseModel>> GetCreditRiskReportAsync(CancellationToken ct)
    {
        var customers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CreditLimit.HasValue)
            .ToListAsync(ct);

        var results = new List<CreditStatusResponseModel>();
        foreach (var customer in customers)
        {
            var status = await GetCreditStatusAsync(customer.Id, ct);
            results.Add(status);
        }

        return results.OrderByDescending(r => r.UtilizationPercent).ToList();
    }

    public Task CheckCreditReviewsDueAsync(CancellationToken ct)
    {
        // Implementation deferred — Hangfire job uses CheckCreditReviewsDueJob directly
        return Task.CompletedTask;
    }
}
