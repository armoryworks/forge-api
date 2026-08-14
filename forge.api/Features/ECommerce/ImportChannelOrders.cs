using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.RetailOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// Polls a channel's connector and turns what it returns into retail orders.
///
/// <para>This replaces the old <c>ImportECommerceOrdersCommand</c>, which
/// delegated order creation to <c>IECommerceService.ImportOrderAsync</c> inside
/// the integrations project — bypassing MediatR, activity logging and
/// capability gating, and (in the only implementation that existed) returning a
/// random integer instead of creating anything. Here the connector only
/// supplies data; the order is created by
/// <see cref="CreateRetailOrderCommand"/>, on the same path manual retail entry
/// uses.</para>
/// </summary>
public record ImportChannelOrdersCommand(int ChannelId, DateTimeOffset? Since = null)
    : IRequest<List<ECommerceOrderSyncResponseModel>>;

public class ImportChannelOrdersHandler(
    AppDbContext db,
    IECommerceServiceFactory connectorFactory,
    IMediator mediator,
    IClock clock,
    ILogger<ImportChannelOrdersHandler> logger)
    : IRequestHandler<ImportChannelOrdersCommand, List<ECommerceOrderSyncResponseModel>>
{
    /// <summary>How far back a first-ever poll reaches when the integration has no LastSyncAt.</summary>
    private static readonly TimeSpan InitialLookback = TimeSpan.FromDays(30);

    public async Task<List<ECommerceOrderSyncResponseModel>> Handle(
        ImportChannelOrdersCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels
            .Include(c => c.ECommerceIntegration)
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.ChannelId} not found");

        if (!channel.IsRetail)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' is {channel.ChannelType}. Only retail and marketplace channels import orders.");
        }

        var integration = channel.ECommerceIntegration
            ?? throw new InvalidOperationException(
                $"Channel '{channel.Code}' has no e-commerce integration attached — nothing to poll. " +
                "Manual channels take orders through the retail-order endpoint instead.");

        if (!integration.IsActive)
            throw new InvalidOperationException($"Integration '{integration.Name}' is inactive.");

        var connector = connectorFactory.For(integration.Platform);
        var since = request.Since ?? integration.LastSyncAt ?? clock.UtcNow - InitialLookback;

        IReadOnlyList<ECommerceOrder> polled;
        try
        {
            polled = await connector.PollOrdersAsync(
                integration.EncryptedCredentials, integration.StoreUrl ?? string.Empty, since, ct);
        }
        catch (Exception ex)
        {
            // Record why the poll failed on the integration so the admin screen
            // shows it, then rethrow — a failed poll is not a successful import
            // of zero orders, and LastSyncAt must NOT advance or the window
            // silently skips those orders forever.
            integration.LastError = Truncate(ex.Message, 2000);
            await db.SaveChangesAsync(ct);
            throw;
        }

        var results = new List<ECommerceOrderSyncResponseModel>(polled.Count);

        // Pre-load the sync rows for everything in this batch rather than
        // querying per order.
        var externalIds = polled.Select(o => o.ExternalId).ToList();
        var existingSyncs = await db.ECommerceOrderSyncs
            .Where(s => s.IntegrationId == integration.Id && externalIds.Contains(s.ExternalOrderId))
            .ToDictionaryAsync(s => s.ExternalOrderId, ct);

        foreach (var order in polled)
        {
            ct.ThrowIfCancellationRequested();

            if (existingSyncs.TryGetValue(order.ExternalId, out var priorSync)
                && priorSync.Status == ECommerceOrderSyncStatus.Imported)
            {
                results.Add(ToModel(priorSync, ECommerceOrderSyncStatus.Skipped));
                continue;
            }

            // Reuse the prior row on retry so a failed order does not accumulate
            // one sync record per attempt.
            var sync = priorSync ?? new ECommerceOrderSync
            {
                IntegrationId = integration.Id,
                ExternalOrderId = order.ExternalId,
                ExternalOrderNumber = order.OrderNumber,
                ImportedAt = clock.UtcNow,
            };

            sync.OrderDataJson = JsonSerializer.Serialize(order);
            sync.ImportedAt = clock.UtcNow;

            try
            {
                var result = await mediator.Send(
                    new CreateRetailOrderCommand(ToRetailOrderModel(channel, order)), ct);

                sync.SalesOrderId = result.Order.Id;
                sync.Status = ECommerceOrderSyncStatus.Imported;
                sync.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                // One bad order must not abort the batch — the rest are real
                // obligations that still need importing. It lands as Failed and
                // is retried on the next poll.
                logger.LogError(ex,
                    "[CHANNEL-IMPORT] Order {ExternalId} on channel {Channel} failed to import",
                    order.ExternalId, channel.Code);
                sync.Status = ECommerceOrderSyncStatus.Failed;
                sync.ErrorMessage = Truncate(ex.Message, 2000);
            }

            if (priorSync is null)
                db.ECommerceOrderSyncs.Add(sync);

            await db.SaveChangesAsync(ct);
            results.Add(ToModel(sync, sync.Status));
        }

        // Advance the watermark only after the batch is durably recorded. Failed
        // orders keep their Failed sync rows and are re-polled next time,
        // because the poll window is inclusive of updated_at.
        integration.LastSyncAt = clock.UtcNow;
        integration.LastError = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[CHANNEL-IMPORT] Channel {Channel}: {Imported} imported, {Skipped} skipped, {Failed} failed",
            channel.Code,
            results.Count(r => r.Status == ECommerceOrderSyncStatus.Imported),
            results.Count(r => r.Status == ECommerceOrderSyncStatus.Skipped),
            results.Count(r => r.Status == ECommerceOrderSyncStatus.Failed));

        return results;
    }

    /// <summary>
    /// Translate the connector's normalised order into the retail-order command.
    /// Prices and tax come across exactly as the platform reported them — they
    /// are what the buyer actually paid, and re-resolving them from a price list
    /// would invent a number that matches no receipt.
    /// </summary>
    internal static CreateRetailOrderRequestModel ToRetailOrderModel(SalesChannel channel, ECommerceOrder order)
    {
        // Derive a rate from the reported amounts rather than trusting a rate
        // field: marketplaces report totals, and a blended multi-jurisdiction
        // rate is not something they expose.
        var taxRate = order.SubtotalAmount > 0m
            ? decimal.Round(order.TaxAmount / order.SubtotalAmount, 6, MidpointRounding.AwayFromZero)
            : 0m;

        return new CreateRetailOrderRequestModel
        {
            ChannelId = channel.Id,
            ExternalOrderNumber = order.OrderNumber,
            ExternalOrderId = order.ExternalId,
            OrderDate = order.OrderDate,
            TaxRate = taxRate,
            TaxCollectedBy = order.TaxCollectedBy ?? channel.TaxCollectedBy,
            ShippingAmount = order.ShippingAmount,
            Notes = order.Notes,
            Buyer = new RetailBuyerInput
            {
                // Fall back to the order id, never to the email — a marketplace
                // relay address rotates, so keying identity on it would split one
                // buyer across many rows.
                ExternalBuyerId = string.IsNullOrWhiteSpace(order.BuyerId)
                    ? $"order:{order.ExternalId}"
                    : order.BuyerId,
                DisplayName = string.IsNullOrWhiteSpace(order.CustomerName)
                    ? order.ShippingAddress.Name
                    : order.CustomerName,
                ContactEmail = order.CustomerEmail,
                Phone = order.CustomerPhone,
                MarketingConsent = order.MarketingConsent,
            },
            ShipTo = new OrderShipToInput
            {
                Name = string.IsNullOrWhiteSpace(order.ShippingAddress.Name)
                    ? order.CustomerName
                    : order.ShippingAddress.Name,
                Line1 = order.ShippingAddress.Line1,
                Line2 = order.ShippingAddress.Line2,
                City = order.ShippingAddress.City,
                State = order.ShippingAddress.State,
                PostalCode = order.ShippingAddress.PostalCode,
                Country = order.ShippingAddress.Country,
                Phone = order.CustomerPhone,
                // Platforms validate at checkout; re-validating would spend a
                // USPS call to confirm what the buyer already paid to ship to.
                IsValidated = true,
            },
            Lines = order.Lines.Select(l => new CreateRetailOrderLineModel
            {
                ExternalSku = string.IsNullOrWhiteSpace(l.ExternalSku) ? null : l.ExternalSku,
                Description = string.IsNullOrWhiteSpace(l.ProductName)
                    ? l.ExternalSku ?? "Imported item"
                    : l.ProductName,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
            }).ToList(),
        };
    }

    private static ECommerceOrderSyncResponseModel ToModel(
        ECommerceOrderSync sync, ECommerceOrderSyncStatus status) => new()
    {
        Id = sync.Id,
        ExternalOrderId = sync.ExternalOrderId,
        ExternalOrderNumber = sync.ExternalOrderNumber,
        SalesOrderId = sync.SalesOrderId,
        Status = status,
        ErrorMessage = sync.ErrorMessage,
        ImportedAt = sync.ImportedAt,
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
