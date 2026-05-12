using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PayStubDeductionResponseModel(
    int Id,
    PayStubDeductionCategory Category,
    string Description,
    decimal Amount);
