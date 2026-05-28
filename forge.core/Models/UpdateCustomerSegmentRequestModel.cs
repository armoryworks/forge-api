namespace Forge.Core.Models;

/// <summary>C3: payload for editing a customer segment.</summary>
public record UpdateCustomerSegmentRequestModel(
    string Name,
    string? Description,
    string? FilterCriteria,
    bool IsActive);
