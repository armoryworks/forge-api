using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class UpsShippingService(
    IHttpClientFactory httpClientFactory,
    IOptions<UpsOptions> options,
    ILogger<UpsShippingService> logger) : IShippingCarrierService
{
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public string CarrierId => "ups";
    public string CarrierName => "UPS";
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.ClientId) && !string.IsNullOrEmpty(options.Value.ClientSecret);

    public async Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var opts = options.Value;
        var token = await GetAccessTokenAsync(ct);
        if (token is null) return [];

        var client = CreateClient(token);
        var payload = BuildRateRequest(request, opts);
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{opts.BaseUrl}/rating/v2403/rate", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[UPS] GetRates failed: {Status} {Body}", response.StatusCode, body);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var rates = new List<ShippingRate>();
        if (doc.RootElement.TryGetProperty("RateResponse", out var rr) &&
            rr.TryGetProperty("RatedShipment", out var shipments))
        {
            foreach (var s in shipments.EnumerateArray())
            {
                var serviceCode = s.TryGetProperty("Service", out var svc)
                    ? svc.TryGetProperty("Code", out var code) ? code.GetString() ?? "03" : "03"
                    : "03";
                var serviceName = MapUpsServiceCode(serviceCode);
                var amount = s.TryGetProperty("TotalCharges", out var charges) &&
                             charges.TryGetProperty("MonetaryValue", out var mv)
                    ? decimal.Parse(mv.GetString() ?? "0")
                    : 0m;
                var days = s.TryGetProperty("GuaranteedDelivery", out var gd) &&
                           gd.TryGetProperty("BusinessDaysInTransit", out var daysEl)
                    ? int.TryParse(daysEl.GetString(), out var d) ? d : 5 : 5;
                rates.Add(new ShippingRate($"ups-{serviceCode}", "UPS", serviceName, amount, days));
            }
        }

        logger.LogInformation("[UPS] GetRates — returned {Count} rate(s)", rates.Count);
        return rates;
    }

    public async Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("UPS is not configured");
        var opts = options.Value;
        var token = await GetAccessTokenAsync(ct);
        if (token is null) throw new InvalidOperationException("UPS authentication failed");

        // Extract service code from carrierId (e.g., "ups-03" → "03")
        var serviceCode = carrierId.StartsWith("ups-") ? carrierId[4..] : "03";

        var client = CreateClient(token);
        var payload = BuildShipRequest(request, serviceCode, opts);
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{opts.BaseUrl}/shipments/v2403/ship", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[UPS] CreateLabel failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"UPS label creation failed: {response.StatusCode}");
        }

        var doc = JsonDocument.Parse(body);
        var shipResp = doc.RootElement.GetProperty("ShipmentResponse").GetProperty("ShipmentResults");
        var tracking = shipResp.GetProperty("ShipmentIdentificationNumber").GetString()!;
        var labelData = shipResp
            .GetProperty("PackageResults")
            .EnumerateArray().First()
            .GetProperty("ShippingLabel")
            .GetProperty("GraphicImage")
            .GetString()!;

        logger.LogInformation("[UPS] CreateLabel — tracking {Tracking}", tracking);
        return new ShippingLabel(tracking, $"data:image/png;base64,{labelData}", "UPS");
    }

    public async Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var opts = options.Value;
        var token = await GetAccessTokenAsync(ct);
        if (token is null) return null;

        var client = CreateClient(token);
        var response = await client.GetAsync($"{opts.BaseUrl}/track/v1/details/{trackingNumber}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[UPS] GetTracking({Tracking}) — {Status}", trackingNumber, response.StatusCode);
            return null;
        }

        var doc = JsonDocument.Parse(body);
        var events = new List<TrackingEvent>();
        string status = "Unknown";
        DateTimeOffset? estimatedDelivery = null;

        if (doc.RootElement.TryGetProperty("trackResponse", out var tr) &&
            tr.TryGetProperty("shipment", out var shipmentArr))
        {
            var shipment = shipmentArr.EnumerateArray().FirstOrDefault();
            if (shipment.ValueKind != JsonValueKind.Undefined)
            {
                if (shipment.TryGetProperty("package", out var packages))
                {
                    var pkg = packages.EnumerateArray().FirstOrDefault();
                    if (pkg.ValueKind != JsonValueKind.Undefined)
                    {
                        if (pkg.TryGetProperty("currentStatus", out var cs))
                            status = cs.TryGetProperty("description", out var desc) ? desc.GetString() ?? "Unknown" : "Unknown";

                        if (pkg.TryGetProperty("deliveryDate", out var dd))
                        {
                            foreach (var dateEl in dd.EnumerateArray())
                            {
                                if (dateEl.TryGetProperty("date", out var dv))
                                {
                                    if (DateTimeOffset.TryParse(dv.GetString(), out var dt))
                                        estimatedDelivery = dt;
                                    break;
                                }
                            }
                        }

                        if (pkg.TryGetProperty("activity", out var activities))
                        {
                            foreach (var act in activities.EnumerateArray())
                            {
                                var loc = act.TryGetProperty("location", out var l) &&
                                          l.TryGetProperty("address", out var a) &&
                                          a.TryGetProperty("city", out var city)
                                    ? city.GetString() ?? string.Empty : string.Empty;
                                var actDesc = act.TryGetProperty("status", out var st) &&
                                              st.TryGetProperty("description", out var sdesc)
                                    ? sdesc.GetString() ?? string.Empty : string.Empty;
                                var dateStr = act.TryGetProperty("date", out var actDate) ? actDate.GetString() : null;
                                var timeStr = act.TryGetProperty("time", out var actTime) ? actTime.GetString() : null;
                                DateTimeOffset.TryParse($"{dateStr} {timeStr}", out var eventTime);
                                events.Add(new TrackingEvent(eventTime, loc, actDesc));
                            }
                        }
                    }
                }
            }
        }

        return new ShipmentTracking(trackingNumber, status, estimatedDelivery, events);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct) =>
        GetAccessTokenAsync(ct).ContinueWith(t => t.Result is not null, ct);

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        var opts = options.Value;
        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);
        var response = await client.PostAsync(opts.TokenUrl, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[UPS] Token request failed: {Status}", response.StatusCode);
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
        client.DefaultRequestHeaders.Add("transId", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("transactionSrc", "Forge");
        return client;
    }

    private static object BuildRateRequest(ShipmentRequest request, UpsOptions opts) => new
    {
        RateRequest = new
        {
            Request = new { RequestOption = "Shop" },
            Shipment = new
            {
                Shipper = new
                {
                    ShipperNumber = opts.AccountNumber,
                    Address = MapAddress(request.FromAddress),
                },
                ShipTo = new { Address = MapAddress(request.ToAddress) },
                ShipFrom = new { Address = MapAddress(request.FromAddress) },
                Package = request.Packages.Select(p => new
                {
                    PackagingType = new { Code = "02" }, // Customer Supplied Package
                    Dimensions = new
                    {
                        UnitOfMeasurement = new { Code = "IN" },
                        Length = ((int)p.LengthIn).ToString(),
                        Width = ((int)p.WidthIn).ToString(),
                        Height = ((int)p.HeightIn).ToString(),
                    },
                    PackageWeight = new
                    {
                        UnitOfMeasurement = new { Code = "LBS" },
                        Weight = p.WeightLbs.ToString("F1"),
                    },
                }).ToArray(),
            },
        },
    };

    private static object BuildShipRequest(ShipmentRequest request, string serviceCode, UpsOptions opts)
    {
        var pkg = request.Packages.FirstOrDefault() ?? new ShippingPackage(1, 12, 12, 6);
        return new
        {
            ShipmentRequest = new
            {
                Shipment = new
                {
                    Description = "Shipment",
                    Shipper = new
                    {
                        ShipperNumber = opts.AccountNumber,
                        Address = MapAddress(request.FromAddress),
                    },
                    ShipTo = new
                    {
                        Name = request.ToAddress.Name,
                        Address = MapAddress(request.ToAddress),
                    },
                    ShipFrom = new
                    {
                        Name = request.FromAddress.Name,
                        Address = MapAddress(request.FromAddress),
                    },
                    Service = new { Code = serviceCode },
                    Package = new
                    {
                        Packaging = new { Code = "02" },
                        Dimensions = new
                        {
                            UnitOfMeasurement = new { Code = "IN" },
                            Length = ((int)pkg.LengthIn).ToString(),
                            Width = ((int)pkg.WidthIn).ToString(),
                            Height = ((int)pkg.HeightIn).ToString(),
                        },
                        PackageWeight = new
                        {
                            UnitOfMeasurement = new { Code = "LBS" },
                            Weight = pkg.WeightLbs.ToString("F1"),
                        },
                    },
                },
                LabelSpecification = new
                {
                    LabelImageFormat = new { Code = "PNG" },
                    LabelStockSize = new { Height = "6", Width = "4" },
                },
            },
        };
    }

    private static object MapAddress(ShippingAddress a) => new
    {
        AddressLine = new[] { a.Street },
        City = a.City,
        StateProvinceCode = a.State,
        PostalCode = a.Zip,
        CountryCode = string.IsNullOrEmpty(a.Country) ? "US" : a.Country,
    };

    private static string MapUpsServiceCode(string code) => code switch
    {
        "01" => "Next Day Air",
        "02" => "2nd Day Air",
        "03" => "Ground",
        "12" => "3 Day Select",
        "13" => "Next Day Air Saver",
        "14" => "Next Day Air Early",
        "59" => "2nd Day Air A.M.",
        _ => $"Service {code}",
    };
}
