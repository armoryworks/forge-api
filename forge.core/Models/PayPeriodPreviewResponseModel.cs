namespace Forge.Core.Models;

public record PayPeriodPreviewResponseModel(DateOnly Start, DateOnly End, bool IsCurrent);
