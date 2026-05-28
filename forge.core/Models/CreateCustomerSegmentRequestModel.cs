namespace Forge.Core.Models;

/// <summary>C3: payload for creating a customer segment.</summary>
public record CreateCustomerSegmentRequestModel(
    string Name,
    string? Description,
    string? FilterCriteria);
