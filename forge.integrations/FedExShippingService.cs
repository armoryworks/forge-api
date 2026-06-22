using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class FedExShippingService(
    IHttpClientFactory httpClientFactory,
    IOptions<FedExOptions> options,
    ILogger<FedExShippingService> logger,
    ICarrierCredentialProvider? credentialProvider = null) : IShippingCarrierService
{
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public string CarrierId => "fedex";
    public string CarrierName => "FedEx";
    public bool IsConfigured
    {
        get { var o = EffectiveOptions(); return !string.IsNullOrEmpty(o.ClientId) && !string.IsNullOrEmpty(o.ClientSecret); }
    }

    // UI-entered (decrypted) credentials override appsettings/env when present.
    private FedExOptions EffectiveOptions()
    {
        var stored = credentialProvider?.Resolve(CarrierId);
        return stored is null ? options.Value : new FedExOptions
        {
            ClientId = stored.ClientId,
            ClientSecret = stored.Secret,
            AccountNumber = stored.AccountNumber ?? string.Empty,
            Environment = stored.Environment,
        };
    }

    public async Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var opts = EffectiveOptions();
        var token = await GetAccessTokenAsync(opts, ct);
        if (token is null) return [];

        var client = CreateClient(token);
        var payload = new
        {
            accountNumber = new { value = opts.AccountNumber },
            requestedShipment = new
            {
                shipper = new { address = MapAddress(request.FromAddress) },
                recipient = new { address = MapAddress(request.ToAddress) },
                pickupType = "DROPOFF_AT_FEDEX_LOCATION",
                rateRequestType = new[] { "ACCOUNT", "LIST" },
                requestedPackageLineItems = request.Packages.Select(p => new
                {
                    weight = new { units = "LB", value = p.WeightLbs },
                    dimensions = new { length = (int)p.LengthIn, width = (int)p.WidthIn, height = (int)p.HeightIn, units = "IN" },
                }).ToArray(),
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{opts.BaseUrl}/rate/v1/rates/quotes", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Detail → logs (for us); the user gets a plain, carrier-attributed message.
            logger.LogError("[FedEx] GetRates failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Something went wrong on {CarrierName}'s end getting rates.");
        }

        var doc = JsonDocument.Parse(body);
        var rates = new List<ShippingRate>();
        if (doc.RootElement.TryGetProperty("output", out var output) &&
            output.TryGetProperty("rateReplyDetails", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                var serviceType = detail.TryGetProperty("serviceType", out var st) ? st.GetString() ?? "" : "";
                var serviceName = MapFedExServiceType(serviceType);
                var deliveryDays = detail.TryGetProperty("transit", out var transit) &&
                                   transit.TryGetProperty("transitDays", out var td)
                    ? MapTransitDays(td.GetString()) : 5;

                decimal amount = 0;
                if (detail.TryGetProperty("ratedShipmentDetails", out var rsd))
                {
                    var first = rsd.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind != JsonValueKind.Undefined &&
                        first.TryGetProperty("totalNetCharge", out var charge))
                        amount = charge.GetDecimal();
                }

                if (amount > 0)
                    rates.Add(new ShippingRate($"fedex-{serviceType.ToLowerInvariant()}", "FedEx", serviceName, amount, deliveryDays));
            }
        }

        logger.LogInformation("[FedEx] GetRates — returned {Count} rate(s)", rates.Count);
        return rates;
    }

    public async Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("FedEx is not configured");
        var opts = EffectiveOptions();
        var token = await GetAccessTokenAsync(opts, ct);
        if (token is null) throw new InvalidOperationException("FedEx authentication failed");

        var serviceType = carrierId.StartsWith("fedex-") ? carrierId[6..].ToUpperInvariant() : "FEDEX_GROUND";
        var client = CreateClient(token);
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);

        var payload = new
        {
            // LABEL (not URL_ONLY): FedEx returns the label image base64-encoded inline so we can compose
            // it into the combined ship document without a second authenticated fetch of a hosted URL.
            labelResponseOptions = "LABEL",
            requestedShipment = new
            {
                shipper = new
                {
                    contact = new { personName = request.FromAddress.Name, phoneNumber = "5555555555" },
                    address = MapAddress(request.FromAddress),
                },
                recipients = new[]
                {
                    new
                    {
                        contact = new { personName = request.ToAddress.Name, phoneNumber = "5555555555" },
                        address = MapAddress(request.ToAddress),
                    },
                },
                serviceType,
                packagingType = "YOUR_PACKAGING",
                pickupType = "DROPOFF_AT_FEDEX_LOCATION",
                requestedPackageLineItems = new[]
                {
                    new
                    {
                        weight = new { units = "LB", value = pkg.WeightLbs },
                        dimensions = new { length = (int)pkg.LengthIn, width = (int)pkg.WidthIn, height = (int)pkg.HeightIn, units = "IN" },
                    },
                },
                shippingChargesPayment = new
                {
                    paymentType = "SENDER",
                    payor = new { responsibleParty = new { accountNumber = new { value = opts.AccountNumber } } },
                },
                labelSpecification = new { imageType = "PNG", labelStockType = "PAPER_4X6" },
            },
            accountNumber = new { value = opts.AccountNumber },
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        HttpResponseMessage response;
        string body;
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            response = await client.PostAsync($"{opts.BaseUrl}/ship/v1/shipments", content, ct);
            body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) break;

            // FedEx's sandbox /ship endpoint frequently returns a transient 503/429 (while /rate stays up).
            // Those mean "not processed — try again", so retrying a couple of times won't double-mint a label.
            var retryable = (int)response.StatusCode is 503 or 429;
            if (!retryable || attempt >= 3)
            {
                logger.LogError("[FedEx] CreateLabel failed (attempt {Attempt}): {Status} {Body} ({Detail})",
                    attempt, response.StatusCode, body, DescribeFedExError(response.StatusCode, body));
                throw new InvalidOperationException($"Something went wrong on {CarrierName}'s end creating the label.");
            }
            logger.LogWarning("[FedEx] CreateLabel transient {Status} (attempt {Attempt}) — retrying", response.StatusCode, attempt);
            await Task.Delay(TimeSpan.FromMilliseconds(600 * attempt), ct);
        }

        var doc = JsonDocument.Parse(body);
        var outputEl = doc.RootElement.GetProperty("output").GetProperty("transactionShipments")
            .EnumerateArray().First();
        var tracking = outputEl.GetProperty("masterTrackingNumber").GetString()!;
        var pkgDoc = outputEl.GetProperty("pieceResponses").EnumerateArray().First()
            .GetProperty("packageDocuments").EnumerateArray().First();

        // LABEL mode → encodedLabel (base64 PNG). Fall back to a hosted url (URL_ONLY responses / stubs).
        byte[]? labelBytes = null;
        var labelUrl = $"fedex://label/{tracking}";
        if (pkgDoc.TryGetProperty("encodedLabel", out var enc) && enc.GetString() is { Length: > 0 } b64)
            labelBytes = Convert.FromBase64String(b64);
        else if (pkgDoc.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
            labelUrl = url;

        logger.LogInformation("[FedEx] CreateLabel — tracking {Tracking} ({Bytes} label bytes)", tracking, labelBytes?.Length ?? 0);
        return new ShippingLabel(tracking, labelUrl, "FedEx", labelBytes);
    }

    public async Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var opts = EffectiveOptions();
        var token = await GetAccessTokenAsync(opts, ct);
        if (token is null) return null;

        var client = CreateClient(token);
        var payload = new
        {
            includeDetailedScans = true,
            trackingInfo = new[] { new { trackingNumberInfo = new { trackingNumber } } },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{opts.BaseUrl}/track/v1/trackingnumbers", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[FedEx] GetTracking({Tracking}) — {Status}", trackingNumber, response.StatusCode);
            return null;
        }

        var doc = JsonDocument.Parse(body);
        var events = new List<TrackingEvent>();
        string status = "Unknown";
        DateTimeOffset? estimatedDelivery = null;

        if (doc.RootElement.TryGetProperty("output", out var output) &&
            output.TryGetProperty("completeTrackResults", out var results))
        {
            var result = results.EnumerateArray().FirstOrDefault();
            if (result.ValueKind != JsonValueKind.Undefined &&
                result.TryGetProperty("trackResults", out var trackResults))
            {
                var tr = trackResults.EnumerateArray().FirstOrDefault();
                if (tr.ValueKind != JsonValueKind.Undefined)
                {
                    if (tr.TryGetProperty("latestStatusDetail", out var lsd))
                        status = lsd.TryGetProperty("description", out var desc) ? desc.GetString() ?? "Unknown" : "Unknown";

                    if (tr.TryGetProperty("estimatedDeliveryTimeWindow", out var edtw) &&
                        edtw.TryGetProperty("window", out var window) &&
                        window.TryGetProperty("ends", out var endDt))
                    {
                        if (DateTimeOffset.TryParse(endDt.GetString(), out var dt))
                            estimatedDelivery = dt;
                    }

                    if (tr.TryGetProperty("scanEvents", out var scans))
                    {
                        foreach (var scan in scans.EnumerateArray())
                        {
                            var loc = scan.TryGetProperty("scanLocation", out var sl) &&
                                      sl.TryGetProperty("city", out var city)
                                ? city.GetString() ?? string.Empty : string.Empty;
                            var evtDesc = scan.TryGetProperty("eventDescription", out var ed) ? ed.GetString() ?? string.Empty : string.Empty;
                            var dateStr = scan.TryGetProperty("date", out var d) ? d.GetString() : null;
                            DateTimeOffset.TryParse(dateStr, out var eventTime);
                            events.Add(new TrackingEvent(eventTime, loc, evtDesc));
                        }
                    }
                }
            }
        }

        return new ShipmentTracking(trackingNumber, status, estimatedDelivery, events);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct) =>
        GetAccessTokenAsync(EffectiveOptions(), ct).ContinueWith(t => t.Result is not null, ct);

    private async Task<string?> GetAccessTokenAsync(FedExOptions opts, CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        var client = httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", opts.ClientId),
            new KeyValuePair<string, string>("client_secret", opts.ClientSecret),
        ]);

        var response = await client.PostAsync($"{opts.BaseUrl}/oauth/token", form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[FedEx] Token request failed: {Status}", response.StatusCode);
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
        client.DefaultRequestHeaders.Add("X-locale", "en_US");
        return client;
    }

    private static object MapAddress(ShippingAddress a) => new
    {
        streetLines = new[] { a.Street },
        city = a.City,
        stateOrProvinceCode = a.State,
        postalCode = a.Zip,
        countryCode = string.IsNullOrEmpty(a.Country) ? "US" : a.Country,
    };

    // Pull FedEx's actual error out of the response body (errors[0].message/code) so the user sees the
    // real reason instead of just the HTTP status. Falls back to the status when the body isn't JSON.
    private static string DescribeFedExError(System.Net.HttpStatusCode status, string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                var msg = first.TryGetProperty("message", out var m) ? m.GetString() : null;
                var code = first.TryGetProperty("code", out var c) ? c.GetString() : null;
                if (!string.IsNullOrWhiteSpace(msg))
                    return string.IsNullOrWhiteSpace(code) ? msg! : $"{msg} ({code})";
            }
        }
        catch (JsonException) { /* non-JSON body (e.g. an HTML 503 page) — fall through to the status */ }
        return status.ToString();
    }

    private static string MapFedExServiceType(string type) => type switch
    {
        "FEDEX_GROUND" => "Ground",
        "FEDEX_2_DAY" => "2 Day",
        "FEDEX_2_DAY_AM" => "2 Day A.M.",
        "FEDEX_EXPRESS_SAVER" => "Express Saver",
        "STANDARD_OVERNIGHT" => "Standard Overnight",
        "FIRST_OVERNIGHT" => "First Overnight",
        "PRIORITY_OVERNIGHT" => "Priority Overnight",
        "FEDEX_HOME_DELIVERY" => "Home Delivery",
        "SMART_POST" => "SmartPost",
        _ => type,
    };

    private static int MapTransitDays(string? days) => days switch
    {
        "ONE_DAY" => 1,
        "TWO_DAYS" => 2,
        "THREE_DAYS" => 3,
        "FOUR_DAYS" => 4,
        "FIVE_DAYS" => 5,
        _ => 7,
    };
}
