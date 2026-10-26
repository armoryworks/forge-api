namespace Forge.Api.Features.CustomerReturns;

/// <summary>Error codes the return resolve validator and handler answer with, mapped to messages by the UI.</summary>
public static class ReturnResolveCodes
{
    public const string LocationRequired = "return-location-required";
    public const string LotRequired = "return-lot-required";
    public const string QuantityRequired = "return-quantity-required";
    public const string QuantityExceedsReturned = "return-quantity-exceeds";
    public const string AlreadyDisposed = "return-already-disposed";
    public const string NoPart = "return-no-part";
    public const string CreditAmountRequired = "return-credit-amount-required";
}
