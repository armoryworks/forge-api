using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ExchangeRate : BaseEntity
{
    public int FromCurrencyId { get; set; }
    public int ToCurrencyId { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public ExchangeRateSource Source { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }

    public Currency FromCurrency { get; set; } = null!;
    public Currency ToCurrency { get; set; } = null!;
}
