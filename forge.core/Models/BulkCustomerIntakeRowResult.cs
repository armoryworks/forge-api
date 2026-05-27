using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>C2: per-row result. CreatedCustomerId is set on commit; MatchedCustomerId
/// points at the existing customer for a duplicate.</summary>
public record BulkCustomerIntakeRowResult(
    string? ExternalRowKey,
    BulkCustomerIntakeRowStatus Status,
    int? CreatedCustomerId,
    int? MatchedCustomerId,
    string? Message);
