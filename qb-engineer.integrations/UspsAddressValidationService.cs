using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Core.Settings;

namespace QBEngineer.Integrations;

/// <summary>
/// Real implementation of <see cref="IAddressValidationService"/> backed by
/// the USPS Addresses API v3 (OAuth client-credentials).
///
/// Phase 1m: ConsumerKey + ConsumerSecret read live from
/// <see cref="ISettingsService"/> at request time. The cached access
/// token is invalidated whenever the credential pair changes — admins
/// rotating keys in /admin/integrations don't have to wait for the
/// existing token to expire.
/// </summary>
public class UspsAddressValidationService : IAddressValidationService
{
    private const string TokenUrl = "https://apis.usps.com/oauth2/v3/token";
    private const string AddressUrl = "https://apis.usps.com/addresses/v3/address";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<UspsAddressValidationService> _logger;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    /// <summary>Cached credential pair the current access token was minted
    /// against. When the live (key, secret) differ — admin rotated the
    /// creds — we discard the token and re-auth.</summary>
    private (string? Key, string? Secret) _tokenCreds;

    public UspsAddressValidationService(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<UspsAddressValidationService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AddressValidationResponseModel> ValidateAsync(ValidateAddressRequestModel request, CancellationToken ct)
    {
        _logger.LogInformation("[USPS] ValidateAddress for {City}, {State} {Zip}",
            request.City, request.State, request.Zip);

        try
        {
            await EnsureAccessTokenAsync(ct);

            var queryParams = new List<string>
            {
                $"streetAddress={Uri.EscapeDataString(request.Street)}",
                $"state={Uri.EscapeDataString(request.State)}",
            };

            if (!string.IsNullOrWhiteSpace(request.City))
                queryParams.Add($"city={Uri.EscapeDataString(request.City)}");

            var zip5 = ExtractZip5(request.Zip);
            var zip4 = ExtractZip4(request.Zip);
            if (!string.IsNullOrEmpty(zip5))
                queryParams.Add($"ZIPCode={Uri.EscapeDataString(zip5)}");
            if (!string.IsNullOrEmpty(zip4))
                queryParams.Add($"ZIPPlus4={Uri.EscapeDataString(zip4)}");

            var url = $"{AddressUrl}?{string.Join("&", queryParams)}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var errorMessage = ParseErrorMessage(errorBody, response.StatusCode);

                _logger.LogWarning("[USPS] Address validation returned {StatusCode}: {Error}",
                    (int)response.StatusCode, errorMessage);

                return new AddressValidationResponseModel(false, null, null, null, null, request.Country, [errorMessage]);
            }

            var result = await response.Content.ReadFromJsonAsync<UspsAddressResponse>(JsonOptions, ct);
            if (result == null)
            {
                return new AddressValidationResponseModel(false, null, null, null, null, request.Country,
                    ["Unexpected response from USPS API"]);
            }

            var street = result.Address?.StreetAddress;
            var city = result.Address?.City;
            var state = result.Address?.State;
            var zipCode = result.Address?.ZIPCode;
            var zipPlus4 = result.Address?.ZIPPlus4;
            var zip = !string.IsNullOrEmpty(zipPlus4) ? $"{zipCode}-{zipPlus4}" : zipCode;

            var messages = new List<string>();

            var dpv = result.AdditionalInfo?.DPVConfirmation;
            var isValid = dpv switch
            {
                "Y" => true,
                "D" => true,
                "S" => false,
                "N" => false,
                _ => true,
            };

            if (dpv == "D")
                messages.Add("Address confirmed but apartment/suite number may be missing.");
            if (dpv == "S")
                messages.Add("Address found but apartment/suite number could not be confirmed.");

            if (result.Corrections is { Count: > 0 })
            {
                foreach (var correction in result.Corrections)
                {
                    if (!string.IsNullOrEmpty(correction.Text))
                        messages.Add(correction.Text);
                }
            }

            if (result.Warnings is { Count: > 0 })
                messages.AddRange(result.Warnings);

            if (isValid && messages.Count == 0)
                _logger.LogInformation("[USPS] Address verified: {Street}, {City}, {State} {Zip}",
                    street, city, state, zip);

            return new AddressValidationResponseModel(isValid, street, city, state, zip, request.Country, messages);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[USPS] HTTP request failed");
            return new AddressValidationResponseModel(false, null, null, null, null, null,
                ["USPS address verification service unavailable. Please try again later."]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[USPS] Address validation failed");
            return new AddressValidationResponseModel(false, null, null, null, null, null,
                [$"Address validation error: {ex.Message}"]);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var result = await ValidateAsync(new ValidateAddressRequestModel(
                "1600 Pennsylvania Ave NW",
                "Washington",
                "DC",
                "20500",
                "US"), ct);

            return result.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[USPS] Connection test failed");
            return false;
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken ct)
    {
        var consumerKey = await _settings.GetStringAsync(UspsSettings.KeyConsumerKey, ct);
        var consumerSecret = await _settings.GetStringAsync(UspsSettings.KeyConsumerSecret, ct);

        if (string.IsNullOrEmpty(consumerKey) || string.IsNullOrEmpty(consumerSecret))
        {
            throw new InvalidOperationException(
                "USPS credentials are not configured. Set Consumer Key + Consumer Secret under Admin → Integrations → USPS.");
        }

        // Token cache is keyed by the credential pair so admin rotation
        // immediately invalidates a stale token.
        var liveCreds = (consumerKey, consumerSecret);
        if (_accessToken != null
            && DateTimeOffset.UtcNow < _tokenExpiry
            && _tokenCreds == liveCreds)
        {
            return;
        }

        _logger.LogInformation("[USPS] Requesting OAuth access token");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = consumerKey,
            ["client_secret"] = consumerSecret,
            ["scope"] = "addresses",
        });

        var response = await _httpClient.PostAsync(TokenUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<UspsTokenResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse USPS token response");

        _accessToken = tokenResponse.AccessToken;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
        _tokenCreds = liveCreds;

        _logger.LogInformation("[USPS] OAuth token acquired, expires in {ExpiresIn}s", tokenResponse.ExpiresIn);
    }

    private static string ParseErrorMessage(string errorBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            var error = doc.RootElement.GetProperty("error");
            return error.GetProperty("message").GetString() ?? $"USPS API error ({(int)statusCode})";
        }
        catch
        {
            return statusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "Invalid address information provided.",
                System.Net.HttpStatusCode.NotFound => "Address not found.",
                System.Net.HttpStatusCode.Unauthorized => "USPS API authentication failed — check credentials.",
                System.Net.HttpStatusCode.Forbidden => "USPS API access denied.",
                System.Net.HttpStatusCode.TooManyRequests => "Too many requests — please try again later.",
                _ => $"USPS API error ({(int)statusCode})",
            };
        }
    }

    private static string ExtractZip5(string zip)
    {
        var clean = zip.Replace("-", "").Trim();
        return clean.Length >= 5 ? clean[..5] : clean;
    }

    private static string ExtractZip4(string zip)
    {
        if (zip.Contains('-'))
        {
            var parts = zip.Split('-', 2);
            return parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }

        var clean = zip.Replace(" ", "").Trim();
        return clean.Length > 5 ? clean[5..] : string.Empty;
    }

    private sealed class UspsTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class UspsAddressResponse
    {
        public string? Firm { get; set; }
        public UspsAddress? Address { get; set; }
        public UspsAdditionalInfo? AdditionalInfo { get; set; }
        public List<UspsCorrection>? Corrections { get; set; }
        public List<UspsCorrection>? Matches { get; set; }
        public List<string>? Warnings { get; set; }
    }

    private sealed class UspsAddress
    {
        public string? StreetAddress { get; set; }
        public string? StreetAddressAbbreviation { get; set; }
        public string? SecondaryAddress { get; set; }
        public string? City { get; set; }
        public string? CityAbbreviation { get; set; }
        public string? State { get; set; }
        public string? ZIPCode { get; set; }
        public string? ZIPPlus4 { get; set; }
        public string? Urbanization { get; set; }
    }

    private sealed class UspsAdditionalInfo
    {
        public string? DeliveryPoint { get; set; }
        public string? CarrierRoute { get; set; }
        public string? DPVConfirmation { get; set; }
        public string? DPVCMRA { get; set; }
        public string? Business { get; set; }
        public string? CentralDeliveryPoint { get; set; }
        public string? Vacant { get; set; }
    }

    private sealed class UspsCorrection
    {
        public string? Code { get; set; }
        public string? Text { get; set; }
    }
}
