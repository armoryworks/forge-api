namespace Forge.Api.Services.Fx;

/// <summary>
/// Thrown when a currency can no longer change: a party with posted documents (<c>party-currency-locked</c>),
/// a document past Draft (<c>document-currency-locked</c>) or the base currency once anything is booked
/// (<c>base-currency-locked</c>). The exception middleware maps it to 409 with the code.
/// </summary>
public class CurrencyLockedException(string code, string message) : Exception(message)
{
    public const string PartyLocked = "party-currency-locked";
    public const string DocumentLocked = "document-currency-locked";
    public const string BaseLocked = "base-currency-locked";

    public string Code { get; } = code;
}
