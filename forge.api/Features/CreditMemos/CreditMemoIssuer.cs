using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.Payments;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

/// <summary>
/// Issues a draft credit memo: checks it, stamps it Issued, posts it, auto-applies it to its invoice's balance when it
/// credits one, and queues it for the connected accounting provider. Runs inside the caller's transaction.
/// </summary>
public class CreditMemoIssuer(
    AppDbContext db,
    ICreditMemoPostingService posting,
    IClock clock,
    IAccountingProviderFactory? accountingProviders = null,
    ISyncQueueRepository? syncQueue = null)
{
    public async Task IssueAsync(CreditMemo memo, CancellationToken ct)
    {
        if (memo.Status != CreditMemoStatus.Draft)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoNotDraftCode,
                $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only a draft can be issued.");

        if (memo.InvoiceId is int creditedInvoiceId && db.Database.CurrentTransaction is not null)
            await db.AcquireAdvisoryLockAsync("credit-memos.invoice", creditedInvoiceId.ToString(CultureInfo.InvariantCulture), ct);
        await CreditMemoRules.EnsureIssuableAsync(db, memo, ct);

        var userId = CreditMemoRules.UserId(db);
        memo.Status = CreditMemoStatus.Issued;
        memo.IssuedAt = clock.UtcNow;
        memo.IssuedByUserId = db.CurrentUserId;

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("issued",
            memo.IsWriteOff
                ? $"Issued write-off {memo.CreditMemoNumber} for {money.Format(memo.Total)}"
                : $"Issued credit memo {memo.CreditMemoNumber} for {money.Format(memo.Total)}",
            ("CreditMemo", memo.Id), ("Customer", memo.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.PostIssueAsync(memo.Id, userId, ct);

        if (memo.InvoiceId is int invoiceId)
        {
            var invoice = await CreditMemoRules.LoadInvoiceAsync(db, invoiceId, ct);
            if (invoice is not null
                && invoice.Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue
                && invoice.BalanceDue > 0m)
            {
                var amount = Math.Min(memo.OpenAmount, invoice.BalanceDue);
                var application = await CreditMemoRules.ApplyAsync(db, memo, invoice, amount, clock.UtcNow, ct);
                db.LogActivityAt("credit-applied",
                    $"Applied {money.Format(amount)} of credit memo {memo.CreditMemoNumber} to invoice {invoice.InvoiceNumber}",
                    ("CreditMemo", memo.Id), ("Invoice", invoice.Id));
                await db.SaveChangesAsync(ct);
                await posting.PostApplicationAsync(application.Id, userId, ct);
                if (invoice.Status == InvoiceStatus.Paid && invoice.SalesOrderId is int soId)
                    await PaymentApplicationRules.CompletePaidSalesOrdersAsync(db, [soId], ct);
            }
        }

        await EnqueueSyncAsync(memo, ct);
    }

    private async Task EnqueueSyncAsync(CreditMemo memo, CancellationToken ct)
    {
        if (accountingProviders is null || syncQueue is null
            || await accountingProviders.GetConnectedExternalProviderAsync(ct) is null)
            return;

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == memo.CustomerId)
            .Select(c => new { c.ExternalId })
            .FirstAsync(ct);
        var currency = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == memo.CurrencyId)
            .Select(c => new { c.Code, c.IsBaseCurrency })
            .FirstOrDefaultAsync(ct);
        var settingKey = memo.IsWriteOff ? CreditMemoSync.BadDebtItemSettingKey : CreateInvoiceHandler.FreightItemSettingKey;
        var mappedItem = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == settingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var itemId = string.IsNullOrWhiteSpace(mappedItem) ? null : mappedItem;

        var document = new AccountingDocument(
            Type: AccountingDocumentType.CreditMemo,
            CustomerExternalId: customer.ExternalId ?? string.Empty,
            LineItems: memo.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new AccountingLineItem(
                    l.Description,
                    l.Quantity,
                    l.LineType == CreditMemoLineType.RestockingFee ? -l.UnitPrice : l.UnitPrice,
                    l.LineType is CreditMemoLineType.WriteOff or CreditMemoLineType.Freight ? itemId : null,
                    IsFreight: l.LineType == CreditMemoLineType.Freight,
                    Taxable: l.IsTaxable))
                .ToList(),
            RefNumber: memo.CreditMemoNumber,
            Amount: memo.Total,
            Date: memo.CreditDate,
            CurrencyCode: currency is { IsBaseCurrency: false } ? currency.Code : null,
            ExchangeRate: currency is { IsBaseCurrency: false } ? memo.FxRate : null);
        var operation = memo.IsWriteOff ? CreditMemoSync.CreateWriteOffOperation : CreditMemoSync.CreateCreditMemoOperation;
        await syncQueue.EnqueueAsync("CreditMemo", memo.Id, operation, JsonSerializer.Serialize(document), ct);
    }
}
