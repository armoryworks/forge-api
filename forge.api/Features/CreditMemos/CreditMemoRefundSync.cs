using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CreditMemos;

/// <summary>Queues a recorded customer refund for the connected accounting provider, when one is connected.</summary>
public class CreditMemoRefundSync(
    AppDbContext db,
    IAccountingProviderFactory? accountingProviders = null,
    ISyncQueueRepository? syncQueue = null)
{
    public async Task EnqueueAsync(CustomerRefund refund, CancellationToken ct)
    {
        if (accountingProviders is null || syncQueue is null
            || await accountingProviders.GetConnectedExternalProviderAsync(ct) is null)
            return;

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == refund.CustomerId)
            .Select(c => new { c.ExternalId })
            .FirstAsync(ct);
        var currency = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == refund.CurrencyId)
            .Select(c => new { c.Code, c.IsBaseCurrency })
            .FirstOrDefaultAsync(ct);

        var mapping = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == CreditMemoSync.RefundItemSettingKey || s.Key == CreditMemoSync.RefundAccountSettingKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var payload = new AccountingRefund(
            customer.ExternalId ?? string.Empty,
            refund.RefundNumber,
            refund.Amount,
            refund.RefundDate,
            refund.Method.ToString(),
            refund.Reference ?? refund.RefundNumber,
            currency is { IsBaseCurrency: false } ? currency.Code : null,
            currency is { IsBaseCurrency: false } ? refund.FxRate : null,
            NullIfBlank(mapping.GetValueOrDefault(CreditMemoSync.RefundItemSettingKey)),
            NullIfBlank(mapping.GetValueOrDefault(CreditMemoSync.RefundAccountSettingKey)));
        await syncQueue.EnqueueAsync("CustomerRefund", refund.Id, CreditMemoSync.CreateRefundReceiptOperation,
            JsonSerializer.Serialize(payload), ct);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
