namespace Forge.Core.Models;

public record CreateCustomerReturnRequestModel(
    int CustomerId,
    int OriginalJobId,
    string Reason,
    string? Notes,
    DateTimeOffset ReturnDate,
    bool CreateReworkJob);
