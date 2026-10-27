using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentRuns;

/// <summary>Payment runs, newest first, with per-item counts and the cash and credit totals.</summary>
public record GetPaymentRunsQuery(PaymentRunStatus? Status = null) : IRequest<List<PaymentRunListItemModel>>;

public class GetPaymentRunsHandler(AppDbContext db) : IRequestHandler<GetPaymentRunsQuery, List<PaymentRunListItemModel>>
{
    private const int MaxRows = 200;

    public async Task<List<PaymentRunListItemModel>> Handle(GetPaymentRunsQuery request, CancellationToken ct)
    {
        var query = db.PaymentRuns.AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Credits)
            .AsSplitQuery()
            .AsQueryable();
        if (request.Status is PaymentRunStatus status)
            query = query.Where(r => r.Status == status);

        var runs = await query.OrderByDescending(r => r.Id).Take(MaxRows).ToListAsync(ct);
        return runs.Select(r =>
        {
            var credit = r.Items.Sum(i => i.Credits.Sum(c => c.Amount));
            return new PaymentRunListItemModel(
                r.Id, r.RunNumber, r.Status.ToString(), r.PaymentDate, r.Method, r.Items.Count,
                r.Items.Count(i => i.Status == PaymentRunItemStatus.Paid),
                r.Items.Count(i => i.Status == PaymentRunItemStatus.Failed),
                r.Items.Count(i => i.Status == PaymentRunItemStatus.Pending),
                r.Items.Sum(i => Math.Max(0m, i.Amount - i.Credits.Sum(c => c.Amount))),
                credit, r.LastError, r.CreatedAt, r.CompletedAt);
        }).ToList();
    }
}

public record GetPaymentRunByIdQuery(int Id) : IRequest<PaymentRunDetailResponseModel>;

public class GetPaymentRunByIdHandler(AppDbContext db) : IRequestHandler<GetPaymentRunByIdQuery, PaymentRunDetailResponseModel>
{
    public async Task<PaymentRunDetailResponseModel> Handle(GetPaymentRunByIdQuery request, CancellationToken ct)
    {
        var run = await db.PaymentRuns.AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Credits)
            .Include(r => r.Items).ThenInclude(i => i.VendorBill)
            .Include(r => r.Items).ThenInclude(i => i.Vendor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Payment run {request.Id} not found");

        var paymentIds = run.Items.Where(i => i.VendorPaymentId != null).Select(i => i.VendorPaymentId!.Value).Distinct().ToList();
        var paymentNumbers = await db.VendorPayments.AsNoTracking().IgnoreQueryFilters()
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PaymentNumber, ct);
        var currencyCodes = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        return new PaymentRunDetailResponseModel(
            run.Id, run.RunNumber, run.Status.ToString(), run.PaymentDate, run.Method, run.ReferenceNumber, run.Notes,
            run.ApplyVendorCredits, run.LastError, run.CreatedAt, run.StartedAt, run.CompletedAt,
            run.Items.OrderBy(i => i.VendorId).ThenBy(i => i.Id)
                .Select(i =>
                {
                    var credit = i.Credits.Sum(c => c.Amount);
                    return new PaymentRunItemModel(
                        i.Id, i.VendorBillId, i.VendorBill?.BillNumber, i.VendorId, i.Vendor?.CompanyName ?? $"Vendor {i.VendorId}",
                        i.CurrencyId, currencyCodes.GetValueOrDefault(i.CurrencyId), i.Amount, i.DiscountAmount, credit,
                        Math.Max(0m, i.Amount - credit), i.Status.ToString(), i.VendorPaymentId,
                        i.VendorPaymentId is int pid ? paymentNumbers.GetValueOrDefault(pid) : null,
                        i.ErrorMessage, i.ProcessedAt);
                })
                .ToList());
    }
}
