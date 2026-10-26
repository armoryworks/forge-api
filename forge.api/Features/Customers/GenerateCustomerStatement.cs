using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers;

public record GenerateCustomerStatementQuery(int CustomerId) : IRequest<byte[]>;

public class GenerateCustomerStatementHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GenerateCustomerStatementQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateCustomerStatementQuery request, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([request.CustomerId], ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var invoices = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Include(i => i.CreditApplications)
            .Where(i => i.CustomerId == request.CustomerId
                && i.Status != Forge.Core.Enums.InvoiceStatus.Voided
                && i.Status != Forge.Core.Enums.InvoiceStatus.Draft)
            .ToListAsync(ct);

        var payments = await db.Payments
            .Include(p => p.Applications)
            .Where(p => p.CustomerId == request.CustomerId)
            .ToListAsync(ct);

        var creditMemos = await db.CreditMemos.AsNoTracking()
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Include(m => m.Refunds)
            .Where(m => m.CustomerId == request.CustomerId
                && m.Status != Forge.Core.Enums.CreditMemoStatus.Draft
                && m.Status != Forge.Core.Enums.CreditMemoStatus.Voided)
            .ToListAsync(ct);
        var refunds = await db.CustomerRefunds.AsNoTracking()
            .Where(r => r.CustomerId == request.CustomerId && r.Status == Forge.Core.Enums.CustomerRefundStatus.Recorded)
            .ToListAsync(ct);

        var companyName = await CompanyIdentity.GetCompanyNameAsync(db, ct);

        var appliedInvoiceIds = payments.SelectMany(p => p.Applications).Select(a => a.InvoiceId).Distinct().ToList();
        var appliedInvoiceCurrency = await db.Invoices.AsNoTracking()
            .Where(i => appliedInvoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CurrencyId })
            .ToDictionaryAsync(i => i.Id, i => i.CurrencyId, ct);

        var baseCode = await MoneyFormat.BaseCodeAsync(db, ct);
        var currencyIds = invoices.Select(i => i.CurrencyId).Concat(appliedInvoiceCurrency.Values)
            .Concat(creditMemos.Select(m => m.CurrencyId)).Concat(refunds.Select(r => r.CurrencyId)).Distinct().ToList();
        var currencies = await db.Currencies.AsNoTracking()
            .Where(c => currencyIds.Contains(c.Id) || c.Code.ToUpper() == baseCode)
            .ToListAsync(ct);
        var currencyMoney = currencyIds.ToDictionary(
            id => id, id => MoneyFormat.For(currencies.FirstOrDefault(c => c.Id == id), baseCode));
        var baseMoney = MoneyFormat.For(currencies.FirstOrDefault(c => c.Code.ToUpper() == baseCode), baseCode);

        var paymentFallback = await StatementPaymentFallbackAsync(customer, invoices, currencyMoney, baseCode, baseMoney, ct);
        var paymentMoney = payments.ToDictionary(p => p.Id, p =>
        {
            var applied = p.Applications
                .Select(a => appliedInvoiceCurrency.TryGetValue(a.InvoiceId, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Distinct()
                .ToList();
            return applied.Count == 1 ? currencyMoney[applied[0]!.Value] : paymentFallback;
        });

        var document = new CustomerStatementPdfDocument(
            customer, invoices, payments, companyName, clock.UtcNow, currencyMoney, paymentMoney, baseMoney, creditMemos, refunds);

        return document.GeneratePdf();
    }

    private async Task<MoneyFormatter> StatementPaymentFallbackAsync(
        Customer customer,
        List<Invoice> invoices,
        IReadOnlyDictionary<int, MoneyFormatter> currencyMoney,
        string baseCode,
        MoneyFormatter baseMoney,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(customer.DefaultCurrency))
        {
            var code = customer.DefaultCurrency.Trim().ToUpperInvariant();
            var currency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Code.ToUpper() == code, ct);
            return currency is null ? MoneyFormat.For(code, null, 2, baseCode) : MoneyFormat.For(currency, baseCode);
        }

        var invoiceCurrencies = invoices.Select(i => i.CurrencyId).Distinct().ToList();
        if (invoiceCurrencies.Count == 1)
            return currencyMoney[invoiceCurrencies[0]];

        return invoiceCurrencies.Count == 0 ? baseMoney : baseMoney.WithCode();
    }
}
