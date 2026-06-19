using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class UspsShippingService(
    IHttpClientFactory httpClientFactory,
    IOptions<UspsOptions> options,
    ILogger<UspsShippingService> logger,
    ICarrierCredentialProvider? credentialProvider = null) : IShippingCarrierService
{
    private const string BaseUrl = "https://api.usps.com";
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public string CarrierId => "usps";
    public string CarrierName => "USPS";
    public bool IsConfigured
    {
        get { var o = EffectiveOptions(); return !string.IsNullOrEmpty(o.ConsumerKey) && !string.IsNullOrEmpty(o.ConsumerSecret); }
    }

    // UI-entered (decrypted) credentials override appsettings when present.
    private UspsOptions EffectiveOptions()
    {
        var stored = credentialProvider?.Resolve(CarrierId);
        return stored is null ? options.Value
            : new UspsOptions { ConsumerKey = stored.ClientId, ConsumerSecret = stored.Secret };
    }

    public async Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var token = await GetAccessTokenAsync(EffectiveOptions(), ct);
        if (token is null) return [];

        var client = CreateClient(token);
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);
        var payload = new
        {
            originZIPCode = request.FromAddress.Zip,
            destinationZIPCode = request.ToAddress.Zip,
            weight = pkg.WeightLbs,
            length = pkg.LengthIn,
            width = pkg.WidthIn,
            height = pkg.HeightIn,
            mailClasses = new[] { "USPS_CONNECT_LOCAL", "PRIORITY_MAIL", "FIRST-CLASS_PACKAGE_SERVICE", "PARCEL_SELECT", "PRIORITY_MAIL_EXPRESS" },
            priceType = "RETAIL",
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{BaseUrl}/prices/v3/total-rates/search", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[USPS] GetRates failed: {Status} {Body}", response.StatusCode, body);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var rates = new List<ShippingRate>();
        if (doc.RootElement.TryGetProperty("totalRates", out var totalRates))
        {
            foreach (var rate in totalRates.EnumerateArray())
            {
                var mailClass = rate.TryGetProperty("mailClass", out var mc) ? mc.GetString() ?? "" : "";
                var description = rate.TryGetProperty("description", out var desc) ? desc.GetString() ?? mailClass : mailClass;
                var amount = rate.TryGetProperty("totalBasePrice", out var tbp) ? tbp.GetDecimal() : 0m;
                var days = rate.TryGetProperty("commitment", out var commit) &&
                           commit.TryGetProperty("name", out var cname)
                    ? ParseUspsDays(cname.GetString()) : 5;
                if (amount > 0)
                    rates.Add(new ShippingRate($"usps-{mailClass.ToLowerInvariant().Replace(' ', '-')}", "USPS", description, amount, days));
            }
        }

        logger.LogInformation("[USPS] GetRates — returned {Count} rate(s)", rates.Count);
        return rates;
    }

    public async Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("USPS is not configured");
        var token = await GetAccessTokenAsync(EffectiveOptions(), ct);
        if (token is null) throw new InvalidOperationException("USPS authentication failed");

        var mailClass = carrierId.StartsWith("usps-") ? carrierId[5..].ToUpperInvariant().Replace('-', '_') : "PRIORITY_MAIL";
        var client = CreateClient(token);
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);

        var payload = new
        {
            imageInfo = new { imageType = "PDF", labelType = "4X6LABEL" },
            toAddress = MapAddress(request.ToAddress),
            fromAddress = MapAddress(request.FromAddress),
            packageDescription = new
            {
                mailClass,
                weight = pkg.WeightLbs,
                length = pkg.LengthIn,
                height = pkg.HeightIn,
                width = pkg.WidthIn,
                processingCategory = "MACHINABLE",
                destinationEntryFacilityType = "NONE",
                rateIndicator = "DR",
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{BaseUrl}/labels/v3/label", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[USPS] CreateLabel failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"USPS label creation failed: {response.StatusCode}");
        }

        var doc = JsonDocument.Parse(body);
        var tracking = doc.RootElement.GetProperty("trackingNumber").GetString()!;
        var labelImage = doc.RootElement.TryGetProperty("labelImage", out var li) ? li.GetString() : null;

        logger.LogInformation("[USPS] CreateLabel — tracking {Tracking}", tracking);
        return new ShippingLabel(tracking, $"data:application/pdf;base64,{labelImage ?? string.Empty}", "USPS");
    }

    public async Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var token = await GetAccessTokenAsync(EffectiveOptions(), ct);
        if (token is null) return null;

        var client = CreateClient(token);
        var response = await client.GetAsync($"{BaseUrl}/tracking/v3/tracking/{trackingNumber}?expand=DETAIL", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[USPS] GetTracking({Tracking}) — {Status}", trackingNumber, response.StatusCode);
            return null;
        }

        var doc = JsonDocument.Parse(body);
        var status = doc.RootElement.TryGetProperty("statusSummary", out var ss) ? ss.GetString() ?? "Unknown" : "Unknown";
        DateTimeOffset? estimatedDelivery = null;

        if (doc.RootElement.TryGetProperty("expectedDeliveryTimeStamp", out var edt))
        {
            if (DateTimeOffset.TryParse(edt.GetString(), out var parsedDt))
                estimatedDelivery = parsedDt;
        }

        // USPS Tracking 3.0 (api.usps.com) returns a camelCase trackingEvents[] array — NOT the legacy
        // Web Tools "TrackSummary"/"EventCity" PascalCase shape. Parse the modern fields.
        var events = new List<TrackingEvent>();
        if (doc.RootElement.TryGetProperty("trackingEvents", out var trackingEvents)
            && trackingEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (var evt in trackingEvents.EnumerateArray())
            {
                var loc = evt.TryGetProperty("eventCity", out var city) ? city.GetString() ?? "" : "";
                var evtDesc = evt.TryGetProperty("eventDescription", out var ed) ? ed.GetString() ?? ""
                    : evt.TryGetProperty("eventType", out var etype) ? etype.GetString() ?? "" : "";
                var tsStr = evt.TryGetProperty("eventTimestamp", out var ts) ? ts.GetString() : null;
                DateTimeOffset.TryParse(tsStr, out var evtTime);
                events.Add(new TrackingEvent(evtTime, loc, evtDesc));
            }
        }

        return new ShipmentTracking(trackingNumber, status, estimatedDelivery, events);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct) =>
        GetAccessTokenAsync(EffectiveOptions(), ct).ContinueWith(t => t.Result is not null, ct);

    private async Task<string?> GetAccessTokenAsync(UspsOptions opts, CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        var client = httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", opts.ConsumerKey),
            new KeyValuePair<string, string>("client_secret", opts.ConsumerSecret),
            new KeyValuePair<string, string>("scope", "addresses labels prices tracking"),
        ]);

        var response = await client.PostAsync($"{BaseUrl}/oauth2/v3/token", form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[USPS] Token request failed: {Status}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        _cachedToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
        return _cachedToken;
    }

    private HttpClient CreateClient(string token)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object MapAddress(ShippingAddress a) => new
    {
        firstName = a.Name.Split(' ').FirstOrDefault() ?? a.Name,
        lastName = a.Name.Contains(' ') ? string.Join(' ', a.Name.Split(' ').Skip(1)) : "",
        streetAddress = a.Street,
        city = a.City,
        state = a.State,
        ZIPCode = a.Zip,
    };

    private static int ParseUspsDays(string? commitName) => commitName switch
    {
        var s when s is not null && s.Contains("1-Day") => 1,
        var s when s is not null && s.Contains("Next Day") => 1,
        var s when s is not null && s.Contains("2-Day") => 2,
        var s when s is not null && s.Contains("3-Day") => 3,
        _ => 5,
    };
}
