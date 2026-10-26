namespace Forge.Api.Services.Fx;

/// <summary>
/// Thrown when a foreign-currency document needs a rate and none is on file for the pair on or before the
/// document date. The exception middleware maps it to 422 with problem code <c>fx-rate-missing</c> and the
/// pair and date, so the UI can show its own translated message.
/// </summary>
public class FxRateMissingException(string fromCode, string toCode, DateOnly date)
    : Exception($"No exchange rate for {fromCode} to {toCode} on or before {date:yyyy-MM-dd}. Add one under Admin > Currencies.")
{
    public const string Code = "fx-rate-missing";

    public string FromCode { get; } = fromCode;
    public string ToCode { get; } = toCode;
    public DateOnly Date { get; } = date;
}
