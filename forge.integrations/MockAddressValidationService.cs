using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockAddressValidationService : IAddressValidationService
{
    private readonly ILogger<MockAddressValidationService> _logger;

    public MockAddressValidationService(ILogger<MockAddressValidationService> logger)
    {
        _logger = logger;
    }

    private static readonly HashSet<string> ValidUsStates =
    [
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY",
        "LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND",
        "OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC",
        "AS","GU","MH","FM","MP","PW","PR","VI",
    ];

    public Task<AddressValidationResponseModel> ValidateAsync(ValidateAddressRequestModel request, CancellationToken ct)
    {
        _logger.LogInformation("[MockAddressValidation] Validate for {City}, {State} {Zip}",
            request.City, request.State, request.Zip);

        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Street))
            messages.Add("Street address is required.");

        if (string.IsNullOrWhiteSpace(request.City))
            messages.Add("City is required.");

        if (string.IsNullOrWhiteSpace(request.State))
            messages.Add("State is required.");
        else if (string.Equals(request.Country, "US", StringComparison.OrdinalIgnoreCase)
                 && !ValidUsStates.Contains(request.State.ToUpperInvariant()))
            messages.Add($"'{request.State}' is not a valid US state code.");

        if (string.IsNullOrWhiteSpace(request.Zip))
            messages.Add("ZIP / postal code is required.");
        else if (string.Equals(request.Country, "US", StringComparison.OrdinalIgnoreCase)
                 && !Regex.IsMatch(request.Zip, @"^\d{5}(-\d{4})?$"))
            messages.Add($"'{request.Zip}' is not a valid US ZIP code (expected XXXXX or XXXXX-XXXX).");

        var isValid = messages.Count == 0;

        var correctedStreet = isValid
            ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(request.Street.ToLowerInvariant())
            : request.Street;
        var correctedCity = isValid
            ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(request.City.ToLowerInvariant())
            : request.City;
        var correctedState = isValid ? request.State.ToUpperInvariant() : request.State;

        if (isValid)
            messages.Add("Format check only — connect USPS or a shipping provider for full address verification.");

        var result = new AddressValidationResponseModel(
            isValid,
            correctedStreet,
            correctedCity,
            correctedState,
            request.Zip,
            request.Country,
            messages);

        return Task.FromResult(result);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAddressValidation] TestConnection — returning true");
        return Task.FromResult(true);
    }
}
