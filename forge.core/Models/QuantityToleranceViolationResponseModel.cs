namespace Forge.Core.Models;

public record QuantityToleranceViolationResponseModel(
    string Kind,
    int LineId,
    decimal Ordered,
    decimal AlreadyDone,
    decimal Requested,
    decimal MaxAccepted,
    decimal OverPercent,
    decimal UnderPercent,
    bool CanOverride);
