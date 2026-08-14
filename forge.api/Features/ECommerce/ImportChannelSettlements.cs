using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// Imports marketplace payout batches and ties them back to orders.
///
/// <para>This is what makes a marketplace deposit explainable. Account business
/// settles per document — invoice out, payment in, applied 1:1. A marketplace
/// pays on its own cycle in one lump, net of referral fees, fulfilment fees,
/// postage and refunds, and those components arrive on a different cadence than
/// the orders that caused them. Without this record the deposit from Amazon
/// matches no invoice in the system and the tie-out is manual forever.</para>
///
/// <para>⚡ ACCOUNTING BOUNDARY — the settlement record itself is operational and
/// app-resident in every mode, because the connector is what writes it. What
/// changes by mode is where the resulting journal lives.</para>
/// </summary>
public record ImportChannelSettlementsCommand(int ChannelId, DateTimeOffset? Since = null)
    : IRequest<ImportChannelSettlementsResult>;

public record ImportChannelSettlementsResult(
    int Imported,
    int Updated,
    int Reconciled,
    int WithDiscrepancy,
    int UnmatchedOrderLines);

public class ImportChannelSettlementsHandler(
    AppDbContext db,
    IECommerceServiceFactory connectorFactory,
    IClock clock,
    ILogger<ImportChannelSettlementsHandler> logger)
    : IRequestHandler<ImportChannelSettlementsCommand, ImportChannelSettlementsResult>
{
    private static readonly TimeSpan InitialLookback = TimeSpan.FromDays(90);

    /// <summary>
    /// Tolerance for the reported-vs-computed comparison. Marketplaces round per
    /// component, so a batch of a few hundred lines can legitimately differ from
    /// the reported net by a cent or two; anything larger is a real discrepancy.
    /// </summary>
    private const decimal ReconciliationTolerance = 0.05m;

    public async Task<ImportChannelSettlementsResult> Handle(
        ImportChannelSettlementsCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels
            .Include(c => c.ECommerceIntegration)
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.ChannelId} not found");

        if (channel.ChannelType != SalesChannelType.Marketplace)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' is {channel.ChannelType}. Settlement import applies to marketplaces — " +
                "on a storefront you own, money arrives through your own payment processor and there is no " +
                "platform payout to reconcile.");
        }

        var integration = channel.ECommerceIntegration
            ?? throw new InvalidOperationException(
                $"Channel '{channel.Code}' has no e-commerce integration attached — nothing to poll.");

        var connector = connectorFactory.For(integration.Platform);
        var since = request.Since ?? clock.UtcNow - InitialLookback;

        var polled = await connector.PollSettlementsAsync(
            integration.EncryptedCredentials, integration.StoreUrl ?? string.Empty, since, ct);

        if (polled.Count == 0)
            return new ImportChannelSettlementsResult(0, 0, 0, 0, 0);

        // Resolve every external order id in the batch to a sales order in one
        // query, rather than per settlement line.
        var externalOrderIds = polled
            .SelectMany(s => s.Lines)
            .Select(l => l.ExternalOrderId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        var orderIdByExternal = externalOrderIds.Count == 0
            ? []
            : await db.SalesOrders
                .Where(o => o.ChannelId == channel.Id && o.ExternalId != null && externalOrderIds.Contains(o.ExternalId))
                .Select(o => new { External = o.ExternalId!, o.Id })
                .ToDictionaryAsync(x => x.External, x => x.Id, ct);

        var existingIds = polled.Select(s => s.ExternalSettlementId).ToList();
        var existing = await db.ChannelSettlements
            .Include(s => s.Lines)
            .Where(s => s.ChannelId == channel.Id && existingIds.Contains(s.ExternalSettlementId))
            .ToDictionaryAsync(s => s.ExternalSettlementId, ct);

        var imported = 0;
        var updated = 0;
        var unmatched = 0;

        foreach (var polledSettlement in polled)
        {
            ct.ThrowIfCancellationRequested();

            var settlement = existing.GetValueOrDefault(polledSettlement.ExternalSettlementId);

            if (settlement is null)
            {
                settlement = new ChannelSettlement
                {
                    ChannelId = channel.Id,
                    ExternalSettlementId = polledSettlement.ExternalSettlementId,
                };
                db.ChannelSettlements.Add(settlement);
                imported++;
            }
            else
            {
                if (settlement.Status == ChannelSettlementStatus.Accepted)
                {
                    // Someone reviewed a variance and signed off. Re-importing
                    // would silently discard that judgement.
                    logger.LogInformation(
                        "[CHANNEL-SETTLEMENT] {External} was manually accepted — leaving it alone",
                        settlement.ExternalSettlementId);
                    continue;
                }

                // Replace the lines wholesale. A marketplace can restate a batch
                // before finalising it, and merging would double-count.
                db.ChannelSettlementLines.RemoveRange(settlement.Lines);
                settlement.Lines.Clear();
                updated++;
            }

            settlement.PeriodStart = polledSettlement.PeriodStart;
            settlement.PeriodEnd = polledSettlement.PeriodEnd;
            settlement.DepositedAt = polledSettlement.DepositedAt;
            settlement.ReportedNetAmount = polledSettlement.NetAmount;
            settlement.CurrencyCode = polledSettlement.CurrencyCode;
            settlement.RawPayloadJson = JsonSerializer.Serialize(polledSettlement);

            foreach (var line in polledSettlement.Lines)
            {
                int? salesOrderId = null;
                if (!string.IsNullOrWhiteSpace(line.ExternalOrderId)
                    && orderIdByExternal.TryGetValue(line.ExternalOrderId, out var resolved))
                {
                    salesOrderId = resolved;
                }
                else if (!string.IsNullOrWhiteSpace(line.ExternalOrderId))
                {
                    // Keep the line. An order-linked component whose order was
                    // never imported is a reconciliation exception that a human
                    // needs to see — dropping it would make the batch appear to
                    // tie out while hiding real money.
                    unmatched++;
                }

                settlement.Lines.Add(new ChannelSettlementLine
                {
                    LineType = line.LineType,
                    SalesOrderId = salesOrderId,
                    ExternalOrderId = line.ExternalOrderId,
                    Amount = line.Amount,
                    Description = line.Description,
                    PostedAt = line.PostedAt,
                });
            }

            var computed = settlement.Lines.Sum(l => l.Amount);
            var variance = settlement.ReportedNetAmount - computed;

            settlement.Status = Math.Abs(variance) <= ReconciliationTolerance && unmatched == 0
                ? ChannelSettlementStatus.Reconciled
                : ChannelSettlementStatus.Discrepancy;

            if (settlement.Status == ChannelSettlementStatus.Discrepancy)
            {
                logger.LogWarning(
                    "[CHANNEL-SETTLEMENT] {External} on {Channel}: reported {Reported}, lines sum to {Computed} " +
                    "(variance {Variance}), {Unmatched} unmatched order line(s)",
                    settlement.ExternalSettlementId, channel.Code,
                    settlement.ReportedNetAmount, computed, variance, unmatched);
            }
        }

        await db.SaveChangesAsync(ct);

        var reconciled = await db.ChannelSettlements
            .CountAsync(s => s.ChannelId == channel.Id && s.Status == ChannelSettlementStatus.Reconciled, ct);
        var discrepancies = await db.ChannelSettlements
            .CountAsync(s => s.ChannelId == channel.Id && s.Status == ChannelSettlementStatus.Discrepancy, ct);

        db.LogActivityAt(
            "settlements-imported",
            $"Imported {imported} new and refreshed {updated} settlement batch(es) from {integration.Platform}; " +
            $"{discrepancies} awaiting review",
            ("SalesChannel", channel.Id));
        await db.SaveChangesAsync(ct);

        return new ImportChannelSettlementsResult(
            imported, updated, reconciled, discrepancies, unmatched);
    }
}
