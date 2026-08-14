using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// WooCommerce REST v3 connector.
///
/// <para>Storefront case, same as Shopify: you are the merchant of record, so
/// tax is your liability and <see cref="PollSettlementsAsync"/> has nothing to
/// return — money arrives through your own processor.</para>
///
/// <para>Credentials are <c>consumer_key:consumer_secret</c>, sent as HTTP Basic
/// over TLS, which is what Woo's REST API expects for https endpoints.</para>
/// </summary>
public class WooCommerceECommerceService(
    IHttpClientFactory httpClientFactory,
    ILogger<WooCommerceECommerceService> logger) : IECommerceService
{
    private const int PageSize = 100;
    private const int MaxPages = 200;

    public ECommercePlatform Platform => ECommercePlatform.WooCommerce;

    public async Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);
        var orders = new List<ECommerceOrder>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"orders?per_page={PageSize}&page={page}&orderby=date&order=asc"
                + $"&modified_after={Uri.EscapeDataString(since.UtcDateTime.ToString("s", CultureInfo.InvariantCulture))}";

            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "poll orders", ct);

            var batch = await response.Content.ReadFromJsonAsync<List<WooOrder>>(JsonOptions, ct);
            if (batch is null || batch.Count == 0) break;

            orders.AddRange(batch.Select(MapOrder));

            // Woo returns a full page whenever more remain; a short page is the
            // last one.
            if (batch.Count < PageSize) break;
        }

        logger.LogInformation("[WooCommerce] Polled {Count} order(s) from {Store} since {Since}",
            orders.Count, storeUrl, since);
        return orders;
    }

    public async Task<IReadOnlyList<ECommerceListing>> PollListingsAsync(
        string credentials, string storeUrl, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);
        var listings = new List<ECommerceListing>();

        for (var page = 1; page <= MaxPages; page++)
        {
            using var response = await client.GetAsync($"products?per_page={PageSize}&page={page}", ct);
            await EnsureSuccessAsync(response, "poll listings", ct);

            var batch = await response.Content.ReadFromJsonAsync<List<WooProduct>>(JsonOptions, ct);
            if (batch is null || batch.Count == 0) break;

            listings.AddRange(batch.Select(p => new ECommerceListing
            {
                ExternalListingId = p.Id.ToString(CultureInfo.InvariantCulture),
                ExternalSku = string.IsNullOrWhiteSpace(p.Sku) ? null : p.Sku,
                Title = p.Name,
                Price = ParseDecimal(p.Price),
                AvailableQuantity = p.StockQuantity,
                IsActive = string.Equals(p.Status, "publish", StringComparison.OrdinalIgnoreCase),
            }));

            if (batch.Count < PageSize) break;
        }

        logger.LogInformation("[WooCommerce] Polled {Count} listing(s) from {Store}", listings.Count, storeUrl);
        return listings;
    }

    /// <summary>Always empty — see the class summary.</summary>
    public Task<IReadOnlyList<ECommerceSettlement>> PollSettlementsAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ECommerceSettlement>>([]);

    public async Task SyncInventoryAsync(
        string credentials, string storeUrl, string externalListingId, decimal quantity, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);

        using var response = await client.PutAsJsonAsync(
            $"products/{externalListingId}",
            new
            {
                manage_stock = true,
                // Floored for the same reason as Shopify: publishing a rounded-up
                // fractional on-hand would oversell.
                stock_quantity = (int)Math.Floor(quantity),
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(response, "set stock quantity", ct);
    }

    public async Task UpdateOrderStatusAsync(
        string credentials, string storeUrl, string externalOrderId, string status,
        string? trackingNumber, CancellationToken ct)
    {
        var client = CreateClient(credentials, storeUrl);

        // Woo has no first-class tracking field in core REST — tracking lives in
        // one of several plugins with incompatible schemas. Recording it as an
        // order note is the portable option and is visible to the customer.
        var wooStatus = status.ToLowerInvariant() switch
        {
            "shipped" or "fulfilled" => "completed",
            "cancelled" or "canceled" => "cancelled",
            "refunded" => "refunded",
            _ => status.ToLowerInvariant(),
        };

        using var response = await client.PutAsJsonAsync(
            $"orders/{externalOrderId}", new { status = wooStatus }, JsonOptions, ct);
        await EnsureSuccessAsync(response, "update order status", ct);

        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            using var noteResponse = await client.PostAsJsonAsync(
                $"orders/{externalOrderId}/notes",
                new { note = $"Tracking number: {trackingNumber}", customer_note = true },
                JsonOptions,
                ct);
            await EnsureSuccessAsync(noteResponse, "add tracking note", ct);
        }
    }

    public async Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct)
    {
        try
        {
            var client = CreateClient(credentials, storeUrl);
            using var response = await client.GetAsync("system_status", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "[WooCommerce] Connection test to {Store} failed", storeUrl);
            return false;
        }
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static ECommerceOrder MapOrder(WooOrder o)
    {
        var shipping = ParseDecimal(o.ShippingTotal) ?? 0m;
        var tax = ParseDecimal(o.TotalTax) ?? 0m;
        var total = ParseDecimal(o.Total) ?? 0m;
        var discount = ParseDecimal(o.DiscountTotal) ?? 0m;

        var name = $"{o.Billing?.FirstName} {o.Billing?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{o.Shipping?.FirstName} {o.Shipping?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = o.Billing?.Email ?? $"WooCommerce order {o.Id}";

        return new ECommerceOrder
        {
            ExternalId = o.Id.ToString(CultureInfo.InvariantCulture),
            OrderNumber = string.IsNullOrWhiteSpace(o.Number)
                ? o.Id.ToString(CultureInfo.InvariantCulture)
                : o.Number,
            // CustomerId 0 means a guest checkout — no durable identity, so the
            // buyer key falls back to the order itself rather than collapsing
            // every guest onto id "0".
            BuyerId = o.CustomerId is > 0
                ? o.CustomerId.Value.ToString(CultureInfo.InvariantCulture)
                : $"guest:{o.Id}",
            CustomerName = name,
            CustomerEmail = o.Billing?.Email ?? string.Empty,
            CustomerPhone = o.Billing?.Phone ?? o.Shipping?.Phone,
            Lines = (o.LineItems ?? []).Select(l => new ECommerceOrderLine
            {
                ExternalSku = l.Sku ?? string.Empty,
                ExternalListingId = l.ProductId?.ToString(CultureInfo.InvariantCulture),
                ProductName = l.Name ?? string.Empty,
                Quantity = l.Quantity,
                // Woo's `price` is already per-unit; `total` is the extended
                // amount net of line discount.
                UnitPrice = ParseDecimal(l.Price) ?? 0m,
                LineTotal = ParseDecimal(l.Total) ?? 0m,
                TaxAmount = ParseDecimal(l.TotalTax),
            }).ToList(),
            ShippingAddress = MapAddress(o.Shipping) ?? MapAddress(o.Billing) ?? new ECommerceAddress(),
            BillingAddress = MapAddress(o.Billing),
            SubtotalAmount = total - shipping - tax + discount,
            ShippingAmount = shipping,
            DiscountAmount = discount,
            TaxAmount = tax,
            TotalAmount = total,
            TaxCollectedBy = TaxCollectedBy.Seller,
            CurrencyCode = o.Currency ?? "USD",
            OrderDate = o.DateCreatedGmt is null
                ? DateTimeOffset.UnixEpoch
                : new DateTimeOffset(DateTime.SpecifyKind(o.DateCreatedGmt.Value, DateTimeKind.Utc)),
            PlatformStatus = o.Status,
            Notes = o.CustomerNote,
        };
    }

    private static ECommerceAddress? MapAddress(WooAddress? a)
    {
        if (a is null || string.IsNullOrWhiteSpace(a.Address1)) return null;

        return new ECommerceAddress
        {
            Name = $"{a.FirstName} {a.LastName}".Trim(),
            Line1 = a.Address1 ?? string.Empty,
            Line2 = a.Address2,
            City = a.City ?? string.Empty,
            State = a.State ?? string.Empty,
            PostalCode = a.Postcode ?? string.Empty,
            Country = a.Country ?? "US",
        };
    }

    // ── HTTP plumbing ────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpClient CreateClient(string credentials, string storeUrl)
    {
        if (string.IsNullOrWhiteSpace(credentials))
            throw new InvalidOperationException("WooCommerce integration has no consumer key/secret configured.");
        if (string.IsNullOrWhiteSpace(storeUrl))
            throw new InvalidOperationException("WooCommerce integration has no store URL configured.");

        if (!credentials.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "WooCommerce credentials must be in 'consumer_key:consumer_secret' form.");
        }

        var baseUrl = storeUrl.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseUrl = $"https://{baseUrl}";

        var client = httpClientFactory.CreateClient(nameof(WooCommerceECommerceService));
        client.BaseAddress = new Uri($"{baseUrl}/wp-json/wc/v3/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"WooCommerce request failed to {what}: {(int)response.StatusCode} {response.ReasonPhrase}. " +
            $"{(body.Length <= 500 ? body : body[..500] + "…")}");
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ── Wire shapes ──────────────────────────────────────────────────────────

    private sealed record WooOrder(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("number")] string? Number,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("date_created_gmt")] DateTime? DateCreatedGmt,
        [property: JsonPropertyName("discount_total")] string? DiscountTotal,
        [property: JsonPropertyName("shipping_total")] string? ShippingTotal,
        [property: JsonPropertyName("total_tax")] string? TotalTax,
        [property: JsonPropertyName("total")] string? Total,
        [property: JsonPropertyName("customer_id")] long? CustomerId,
        [property: JsonPropertyName("customer_note")] string? CustomerNote,
        [property: JsonPropertyName("billing")] WooAddress? Billing,
        [property: JsonPropertyName("shipping")] WooAddress? Shipping,
        [property: JsonPropertyName("line_items")] List<WooLineItem>? LineItems);

    private sealed record WooAddress(
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("address_1")] string? Address1,
        [property: JsonPropertyName("address_2")] string? Address2,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("postcode")] string? Postcode,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone);

    private sealed record WooLineItem(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("product_id")] long? ProductId,
        [property: JsonPropertyName("sku")] string? Sku,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("price")] string? Price,
        [property: JsonPropertyName("total")] string? Total,
        [property: JsonPropertyName("total_tax")] string? TotalTax);

    private sealed record WooProduct(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("sku")] string? Sku,
        [property: JsonPropertyName("price")] string? Price,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("stock_quantity")] int? StockQuantity);
}
