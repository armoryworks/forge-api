namespace Forge.Core.Models;

public record AddressValidationResponseModel(
    bool IsValid,
    string? Street,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    List<string> Messages);
