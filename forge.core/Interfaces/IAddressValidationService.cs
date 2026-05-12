using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAddressValidationService
{
    Task<AddressValidationResponseModel> ValidateAsync(ValidateAddressRequestModel request, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);
}
