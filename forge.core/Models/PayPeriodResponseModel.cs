namespace Forge.Core.Models;

public record PayPeriodResponseModel(
    string Type,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int DaysRemaining);
