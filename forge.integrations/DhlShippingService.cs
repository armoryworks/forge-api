using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class DhlShippingService(
    IHttpClientFactory httpClientFactory,
    IOptions<DhlOptions> options,
    ILogger<DhlShippingService> logger) : IShippingCarrierService
{
    public string CarrierId => "dhl";
    public string CarrierName => "DHL Express";
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.ApiKey) && !string.IsNullOrEmpty(options.Value.ApiSecret);

    public async Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var opts = options.Value;
        var client = CreateClient(opts);
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);

        var queryString = $"?accountNumber={opts.AccountNumber}" +
                          $"&originCountryCode={GetCountry(request.FromAddress)}" +
                          $"&originPostalCode={request.FromAddress.Zip}" +
                          $"&destinationCountryCode={GetCountry(request.ToAddress)}" +
                          $"&destinationPostalCode={request.ToAddress.Zip}" +
                          $"&weight={pkg.WeightLbs:F2}" +
                          $"&length={pkg.LengthIn:F0}" +
                          $"&width={pkg.WidthIn:F0}" +
                          $"&height={pkg.HeightIn:F0}" +
                          $"&plannedShippingDateAndTime={DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}" +
                          $"&isCustomsDeclarable=false&unitOfMeasurement=imperial";

        var response = await client.GetAsync($"{opts.BaseUrl}/rates{queryString}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[DHL] GetRates failed: {Status} {Body}", response.StatusCode, body);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var rates = new List<ShippingRate>();
        if (doc.RootElement.TryGetProperty("products", out var products))
        {
            foreach (var product in products.EnumerateArray())
            {
                var productCode = product.TryGetProperty("productCode", out var pc) ? pc.GetString() ?? "" : "";
                var productName = product.TryGetProperty("productName", out var pn) ? pn.GetString() ?? productCode : productCode;
                decimal amount = 0;
                if (product.TryGetProperty("totalPrice", out var tp))
                {
                    foreach (var price in tp.EnumerateArray())
                    {
                        if (price.TryGetProperty("currencyType", out var ct2) && ct2.GetString() == "BILLC" &&
                            price.TryGetProperty("price", out var pr))
                        {
                            amount = pr.GetDecimal();
                            break;
                        }
                    }
                }

                var days = product.TryGetProperty("deliveryCapabilities", out var dc) &&
                           dc.TryGetProperty("estimatedDeliveryDateAndTime", out var edd) &&
                           DateTimeOffset.TryParse(edd.GetString(), out var deliveryDt)
                    ? (int)Math.Ceiling((deliveryDt - DateTimeOffset.UtcNow).TotalDays) : 5;

                if (amount > 0)
                    rates.Add(new ShippingRate($"dhl-{productCode.ToLowerInvariant()}", "DHL Express", productName, amount, Math.Max(1, days)));
            }
        }

        logger.LogInformation("[DHL] GetRates — returned {Count} rate(s)", rates.Count);
        return rates;
    }

    public async Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("DHL is not configured");
        var opts = options.Value;
        var productCode = carrierId.StartsWith("dhl-") ? carrierId[4..].ToUpperInvariant() : "P";
        var client = CreateClient(opts);
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);

        var payload = new
        {
            plannedShippingDateAndTime = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}",
            pickup = new { isRequested = false },
            productCode,
            accounts = new[] { new { typeCode = "shipper", number = opts.AccountNumber } },
            customerDetails = new
            {
                shipperDetails = MapParty(request.FromAddress),
                receiverDetails = MapParty(request.ToAddress),
            },
            content = new
            {
                packages = request.Packages.Select(p => new
                {
                    weight = p.WeightLbs,
                    dimensions = new { length = (int)p.LengthIn, width = (int)p.WidthIn, height = (int)p.HeightIn },
                }).ToArray(),
                isCustomsDeclarable = false,
                declaredValue = 0,
                declaredValueCurrency = "USD",
                description = "Shipment",
                incoterm = "DAP",
                unitOfMeasurement = "imperial",
            },
            outputImageProperties = new
            {
                printerDPI = 300,
                encodingFormat = "pdf",
                imageOptions = new[] { new { typeCode = "label", templateName = "ECOM26_84_001", isRequested = true } },
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{opts.BaseUrl}/shipments", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[DHL] CreateLabel failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"DHL label creation failed: {response.StatusCode}");
        }

        var doc = JsonDocument.Parse(body);
        var tracking = doc.RootElement.GetProperty("shipmentTrackingNumber").GetString()!;
        string? labelData = null;
        if (doc.RootElement.TryGetProperty("documents", out var docs))
        {
            foreach (var docEl in docs.EnumerateArray())
            {
                if (docEl.TryGetProperty("content", out var contentEl))
                {
                    labelData = contentEl.GetString();
                    break;
                }
            }
        }

        logger.LogInformation("[DHL] CreateLabel — tracking {Tracking}", tracking);
        return new ShippingLabel(tracking, $"data:application/pdf;base64,{labelData ?? string.Empty}", "DHL Express");
    }

    public async Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var opts = options.Value;
        var client = CreateClient(opts);

        var response = await client.GetAsync($"{opts.BaseUrl}/shipments/{trackingNumber}/tracking?trackingView=all-checkpoints", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[DHL] GetTracking({Tracking}) — {Status}", trackingNumber, response.StatusCode);
            return null;
        }

        var doc = JsonDocument.Parse(body);
        var events = new List<TrackingEvent>();
        string status = "Unknown";
        DateTimeOffset? estimatedDelivery = null;

        if (doc.RootElement.TryGetProperty("shipments", out var shipments))
        {
            var shipment = shipments.EnumerateArray().FirstOrDefault();
            if (shipment.ValueKind != JsonValueKind.Undefined)
            {
                if (shipment.TryGetProperty("status", out var st))
                    status = st.TryGetProperty("description", out var desc) ? desc.GetString() ?? "Unknown" : "Unknown";

                if (shipment.TryGetProperty("estimatedTimeOfDelivery", out var etd))
                {
                    if (DateTimeOffset.TryParse(etd.GetString(), out var parsedDt))
                        estimatedDelivery = parsedDt;
                }

                if (shipment.TryGetProperty("events", out var evts))
                {
                    foreach (var evt in evts.EnumerateArray())
                    {
                        var loc = evt.TryGetProperty("location", out var l) &&
                                  l.TryGetProperty("address", out var a) &&
                                  a.TryGetProperty("addressLocality", out var city)
                            ? city.GetString() ?? "" : "";
                        var evtDesc = evt.TryGetProperty("description", out var ed) ? ed.GetString() ?? "" : "";
                        var dateStr = evt.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null;
                        DateTimeOffset.TryParse(dateStr, out var eventTime);
                        events.Add(new TrackingEvent(eventTime, loc, evtDesc));
                    }
                }
            }
        }

        return new ShipmentTracking(trackingNumber, status, estimatedDelivery, events);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured) return false;
        var client = CreateClient(opts);
        var testUrl = $"{opts.BaseUrl}/rates?originCountryCode=US&originPostalCode=10001" +
                      $"&destinationCountryCode=US&destinationPostalCode=90210" +
                      $"&weight=1&length=12&width=12&height=6" +
                      $"&plannedShippingDateAndTime={DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}" +
                      $"&isCustomsDeclarable=false&unitOfMeasurement=imperial";
        try
        {
            var response = await client.GetAsync(testUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private HttpClient CreateClient(DhlOptions opts)
    {
        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ApiKey}:{opts.ApiSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static object MapParty(ShippingAddress a) => new
    {
        postalAddress = new
        {
            postalCode = a.Zip,
            cityName = a.City,
            countryCode = GetCountry(a),
            addressLine1 = a.Street,
        },
        contactInformation = new
        {
            fullName = a.Name,
            phone = "5555555555",
            email = "shipping@example.com",
            companyName = a.Name,
        },
    };

    private static string GetCountry(ShippingAddress a) =>
        string.IsNullOrEmpty(a.Country) ? "US" : a.Country;
}
