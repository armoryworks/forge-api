using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Shopify Admin REST connector.
///
/// <para>Shopify is the storefront case: you are the merchant of record, so tax
/// is the seller's liability and money arrives through your own payment
/// processor rather than a platform payout. <see cref="PollSettlementsAsync"/>
/// therefore returns nothing — there is no marketplace settlement to
/// reconcile.</para>
///
/// <para>Credentials are the Admin API access token (<c>shpat_…</c>) and the
/// store URL is the myshopify host. Both come from the
/// <see cref="Entities.ECommerceIntegration"/> row, decrypted by the caller.</para>
/// </summary>
public class ShopifyECommerceService(
    IHttpClientFactory httpClientFactory,
    ILogger<ShopifyECommerceService> logger) : IECommerceService
{
    /// <summary>Pinned so a Shopify-side version rollout cannot silently change response shapes.</summary>
    private const string ApiVersion = "2025-01";

    /// <summary>Shopify's maximum page size. Fewer round trips on a backlog.</summary>
    private const int PageSize = 250;

    /// <summary>Stops a malformed cursor loop from paging forever; 200 pages is 50k orders.</summary>
    private const int MaxPages = 200;

    public ECommercePlatform Platform => ECommercePlatform.Shopify;

    public async Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);
        var orders = new List<ECommerceOrder>();

        // `status=any` because a cancelled or archived order still matters —
        // it may already have been imported and now needs its state reflected.
        var url = $"orders.json?limit={PageSize}&status=any"
            + $"&updated_at_min={Uri.EscapeDataString(since.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}";

        for (var page = 0; page < MaxPages && url is not null; page++)
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "poll orders", ct);

            var payload = await response.Content.ReadFromJsonAsync<ShopifyOrdersEnvelope>(JsonOptions, ct);
            if (payload?.Orders is null || payload.Orders.Count == 0)
                break;

            orders.AddRange(payload.Orders.Select(MapOrder));

            // Shopify cursor-paginates via the Link header; there is no total
            // count and page numbers are not supported.
            url = NextPageUrl(response);
        }

        logger.LogInformation("[Shopify] Polled {Count} order(s) from {Store} since {Since}",
            orders.Count, storeUrl, since);
        return orders;
    }

    public async Task<IReadOnlyList<ECommerceListing>> PollListingsAsync(
        string credentials, string storeUrl, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);
        var listings = new List<ECommerceListing>();
        var url = $"products.json?limit={PageSize}";

        for (var page = 0; page < MaxPages && url is not null; page++)
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "poll listings", ct);

            var payload = await response.Content.ReadFromJsonAsync<ShopifyProductsEnvelope>(JsonOptions, ct);
            if (payload?.Products is null || payload.Products.Count == 0)
                break;

            // A Shopify product holds variants, and the variant — not the
            // product — is what carries the SKU, price and inventory. Variants
            // are what map to parts.
            listings.AddRange(payload.Products.SelectMany(p => (p.Variants ?? []).Select(v => new ECommerceListing
            {
                ExternalListingId = v.Id.ToString(CultureInfo.InvariantCulture),
                ExternalSku = string.IsNullOrWhiteSpace(v.Sku) ? null : v.Sku,
                Title = string.IsNullOrWhiteSpace(v.Title) || v.Title == "Default Title"
                    ? p.Title
                    : $"{p.Title} — {v.Title}",
                Price = ParseDecimal(v.Price),
                AvailableQuantity = v.InventoryQuantity,
                IsActive = string.Equals(p.Status, "active", StringComparison.OrdinalIgnoreCase),
            })));

            url = NextPageUrl(response);
        }

        logger.LogInformation("[Shopify] Polled {Count} listing(s) from {Store}", listings.Count, storeUrl);
        return listings;
    }

    /// <summary>
    /// Always empty. On Shopify the money reaches you through your own payment
    /// processor, so there is no platform payout batch to reconcile — the
    /// settlement concept applies to marketplaces only.
    /// </summary>
    public Task<IReadOnlyList<ECommerceSettlement>> PollSettlementsAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ECommerceSettlement>>([]);

    public async Task SyncInventoryAsync(
        string credentials, string storeUrl, string externalListingId, decimal quantity, CancellationToken ct)
    {
        // externalListingId is the variant id. Shopify sets inventory through
        // the inventory_item attached to the variant, not on the variant itself.
        var client = CreateClient(credentials, storeUrl);

        using var variantResponse = await client.GetAsync($"variants/{externalListingId}.json", ct);
        await EnsureSuccessAsync(variantResponse, "read variant for inventory sync", ct);

        var variantPayload = await variantResponse.Content
            .ReadFromJsonAsync<ShopifyVariantEnvelope>(JsonOptions, ct);

        var inventoryItemId = variantPayload?.Variant?.InventoryItemId
            ?? throw new InvalidOperationException(
                $"Shopify variant {externalListingId} returned no inventory_item_id; cannot set inventory.");

        using var locationsResponse = await client.GetAsync("locations.json", ct);
        await EnsureSuccessAsync(locationsResponse, "read locations for inventory sync", ct);

        var locations = await locationsResponse.Content
            .ReadFromJsonAsync<ShopifyLocationsEnvelope>(JsonOptions, ct);

        var locationId = locations?.Locations?.FirstOrDefault(l => l.Active)?.Id
            ?? throw new InvalidOperationException("Shopify store has no active location to set inventory against.");

        using var setResponse = await client.PostAsJsonAsync(
            "inventory_levels/set.json",
            new
            {
                location_id = locationId,
                inventory_item_id = inventoryItemId,
                // Shopify tracks whole units; a fractional on-hand cannot be
                // published, so it is floored rather than rounded up — better to
                // under-promise than to oversell.
                available = (int)Math.Floor(quantity),
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(setResponse, "set inventory level", ct);
    }

    public async Task UpdateOrderStatusAsync(
        string credentials, string storeUrl, string externalOrderId, string status,
        string? trackingNumber, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);

        if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            using var cancelResponse = await client.PostAsync($"orders/{externalOrderId}/cancel.json", null, ct);
            await EnsureSuccessAsync(cancelResponse, "cancel order", ct);
            return;
        }

        // Anything else is treated as fulfilment. Shopify requires the
        // fulfilment order ids rather than the order id itself.
        using var foResponse = await client.GetAsync($"orders/{externalOrderId}/fulfillment_orders.json", ct);
        await EnsureSuccessAsync(foResponse, "read fulfillment orders", ct);

        var foPayload = await foResponse.Content
            .ReadFromJsonAsync<ShopifyFulfillmentOrdersEnvelope>(JsonOptions, ct);

        var openIds = (foPayload?.FulfillmentOrders ?? [])
            .Where(f => string.Equals(f.Status, "open", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Id)
            .ToList();

        if (openIds.Count == 0)
        {
            logger.LogInformation(
                "[Shopify] Order {OrderId} has no open fulfillment orders — nothing to fulfil", externalOrderId);
            return;
        }

        using var fulfilResponse = await client.PostAsJsonAsync(
            "fulfillments.json",
            new
            {
                fulfillment = new
                {
                    line_items_by_fulfillment_order = openIds.Select(id => new { fulfillment_order_id = id }),
                    tracking_info = string.IsNullOrWhiteSpace(trackingNumber)
                        ? null
                        : new { number = trackingNumber },
                    notify_customer = true,
                },
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(fulfilResponse, "create fulfillment", ct);
    }

    public async Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct)
    {
        try
        {
            var client = CreateClient(credentials, storeUrl);
            using var response = await client.GetAsync("shop.json", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // A failed connection test is an answer, not an error — the admin
            // screen wants false, not a 500.
            logger.LogWarning(ex, "[Shopify] Connection test to {Store} failed", storeUrl);
            return false;
        }
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static ECommerceOrder MapOrder(ShopifyOrder o)
    {
        var shipping = o.ShippingLines?.Sum(s => ParseDecimal(s.Price) ?? 0m) ?? 0m;
        var tax = ParseDecimal(o.TotalTax) ?? 0m;

        return new ECommerceOrder
        {
            ExternalId = o.Id.ToString(CultureInfo.InvariantCulture),
            OrderNumber = o.Name ?? o.OrderNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            BuyerId = o.Customer?.Id?.ToString(CultureInfo.InvariantCulture),
            CustomerName = BuildName(o),
            CustomerEmail = o.Email ?? o.Customer?.Email ?? string.Empty,
            CustomerPhone = o.Phone ?? o.Customer?.Phone ?? o.ShippingAddress?.Phone,
            // Shopify reports this per-customer; absent means no consent.
            MarketingConsent = string.Equals(
                o.Customer?.EmailMarketingConsent?.State, "subscribed", StringComparison.OrdinalIgnoreCase),
            Lines = (o.LineItems ?? []).Select(l => new ECommerceOrderLine
            {
                ExternalSku = l.Sku ?? string.Empty,
                ExternalListingId = l.VariantId?.ToString(CultureInfo.InvariantCulture),
                ProductName = l.Name ?? l.Title ?? string.Empty,
                Quantity = l.Quantity,
                UnitPrice = ParseDecimal(l.Price) ?? 0m,
                LineTotal = (ParseDecimal(l.Price) ?? 0m) * l.Quantity,
                TaxAmount = l.TaxLines?.Sum(t => ParseDecimal(t.Price) ?? 0m),
                DiscountAmount = l.TotalDiscount is null ? null : ParseDecimal(l.TotalDiscount),
            }).ToList(),
            ShippingAddress = MapAddress(o.ShippingAddress) ?? new ECommerceAddress(),
            BillingAddress = MapAddress(o.BillingAddress),
            SubtotalAmount = ParseDecimal(o.SubtotalPrice) ?? 0m,
            ShippingAmount = shipping,
            DiscountAmount = ParseDecimal(o.TotalDiscounts) ?? 0m,
            TaxAmount = tax,
            TotalAmount = ParseDecimal(o.TotalPrice) ?? 0m,
            // On a storefront the seller is always the merchant of record. The
            // one exception Shopify models is a marketplace-facilitator flag on
            // the tax lines, which it sets when it remits on your behalf.
            TaxCollectedBy = o.TaxLines?.Any(t => t.ChannelLiable == true) == true
                ? TaxCollectedBy.Marketplace
                : TaxCollectedBy.Seller,
            CurrencyCode = o.Currency ?? "USD",
            OrderDate = o.CreatedAt ?? DateTimeOffset.UnixEpoch,
            PlatformStatus = o.FinancialStatus,
            Notes = o.Note,
        };
    }

    private static string BuildName(ShopifyOrder o)
    {
        var candidate = $"{o.Customer?.FirstName} {o.Customer?.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(candidate)) return candidate;

        candidate = o.ShippingAddress?.Name?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(candidate)) return candidate;

        // Never return empty — the buyer name is required downstream and is what
        // appears on the pick ticket.
        return o.Email ?? $"Shopify order {o.Id}";
    }

    private static ECommerceAddress? MapAddress(ShopifyAddress? a) => a is null ? null : new ECommerceAddress
    {
        Name = a.Name ?? $"{a.FirstName} {a.LastName}".Trim(),
        Line1 = a.Address1 ?? string.Empty,
        Line2 = a.Address2,
        City = a.City ?? string.Empty,
        State = a.ProvinceCode ?? a.Province ?? string.Empty,
        PostalCode = a.Zip ?? string.Empty,
        Country = a.CountryCode ?? "US",
    };

    // ── HTTP plumbing ────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpClient CreateClient(string credentials, string storeUrl)
    {
        if (string.IsNullOrWhiteSpace(credentials))
            throw new InvalidOperationException("Shopify integration has no access token configured.");
        if (string.IsNullOrWhiteSpace(storeUrl))
            throw new InvalidOperationException("Shopify integration has no store URL configured.");

        var host = storeUrl.Trim()
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        var client = httpClientFactory.CreateClient(nameof(ShopifyECommerceService));
        client.BaseAddress = new Uri($"https://{host}/admin/api/{ApiVersion}/");
        client.DefaultRequestHeaders.Add("X-Shopify-Access-Token", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Shopify's cursor pagination lives in the Link header
    /// (<c>&lt;url&gt;; rel="next"</c>). Returns the relative next-page URL, or
    /// null when this was the last page.
    /// </summary>
    private static string? NextPageUrl(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var linkValues))
            return null;

        foreach (var header in linkValues)
        {
            foreach (var part in header.Split(','))
            {
                if (!part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                    continue;

                var start = part.IndexOf('<');
                var end = part.IndexOf('>');
                if (start < 0 || end <= start) continue;

                var absolute = part[(start + 1)..end];
                // Return it relative to BaseAddress so the client's base and
                // auth headers still apply.
                var idx = absolute.IndexOf($"/api/{ApiVersion}/", StringComparison.Ordinal);
                return idx >= 0
                    ? absolute[(idx + $"/api/{ApiVersion}/".Length)..]
                    : absolute;
            }
        }

        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shopify request failed to {what}: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 500)}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ── Wire shapes. Internal to the connector: nothing outside it sees Shopify's schema. ──

    private sealed record ShopifyOrdersEnvelope([property: JsonPropertyName("orders")] List<ShopifyOrder>? Orders);
    private sealed record ShopifyProductsEnvelope([property: JsonPropertyName("products")] List<ShopifyProduct>? Products);
    private sealed record ShopifyVariantEnvelope([property: JsonPropertyName("variant")] ShopifyVariant? Variant);
    private sealed record ShopifyLocationsEnvelope([property: JsonPropertyName("locations")] List<ShopifyLocation>? Locations);
    private sealed record ShopifyFulfillmentOrdersEnvelope(
        [property: JsonPropertyName("fulfillment_orders")] List<ShopifyFulfillmentOrder>? FulfillmentOrders);

    private sealed record ShopifyLocation(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("active")] bool Active);

    private sealed record ShopifyFulfillmentOrder(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record ShopifyProduct(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("variants")] List<ShopifyVariant>? Variants);

    private sealed record ShopifyVariant(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("sku")] string? Sku,
        [property: JsonPropertyName("price")] string? Price,
        [property: JsonPropertyName("inventory_quantity")] int? InventoryQuantity,
        [property: JsonPropertyName("inventory_item_id")] long? InventoryItemId);

    private sealed record ShopifyOrder(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("order_number")] long? OrderNumber,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("subtotal_price")] string? SubtotalPrice,
        [property: JsonPropertyName("total_tax")] string? TotalTax,
        [property: JsonPropertyName("total_price")] string? TotalPrice,
        [property: JsonPropertyName("total_discounts")] string? TotalDiscounts,
        [property: JsonPropertyName("financial_status")] string? FinancialStatus,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("customer")] ShopifyCustomer? Customer,
        [property: JsonPropertyName("shipping_address")] ShopifyAddress? ShippingAddress,
        [property: JsonPropertyName("billing_address")] ShopifyAddress? BillingAddress,
        [property: JsonPropertyName("line_items")] List<ShopifyLineItem>? LineItems,
        [property: JsonPropertyName("shipping_lines")] List<ShopifyShippingLine>? ShippingLines,
        [property: JsonPropertyName("tax_lines")] List<ShopifyTaxLine>? TaxLines);

    private sealed record ShopifyCustomer(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("email_marketing_consent")] ShopifyMarketingConsent? EmailMarketingConsent);

    private sealed record ShopifyMarketingConsent(
        [property: JsonPropertyName("state")] string? State);

    private sealed record ShopifyAddress(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("address1")] string? Address1,
        [property: JsonPropertyName("address2")] string? Address2,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("province")] string? Province,
        [property: JsonPropertyName("province_code")] string? ProvinceCode,
        [property: JsonPropertyName("zip")] string? Zip,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("phone")] string? Phone);

    private sealed record ShopifyLineItem(
        [property: JsonPropertyName("sku")] string? Sku,
        [property: JsonPropertyName("variant_id")] long? VariantId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("price")] string? Price,
        [property: JsonPropertyName("total_discount")] string? TotalDiscount,
        [property: JsonPropertyName("tax_lines")] List<ShopifyTaxLine>? TaxLines);

    private sealed record ShopifyShippingLine(
        [property: JsonPropertyName("price")] string? Price);

    private sealed record ShopifyTaxLine(
        [property: JsonPropertyName("price")] string? Price,
        [property: JsonPropertyName("channel_liable")] bool? ChannelLiable);
}
