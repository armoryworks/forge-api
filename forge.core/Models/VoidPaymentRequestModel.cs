namespace Forge.Core.Models;

/// <summary>P06-5: payload for voiding (reversing) a recorded payment. Reason is required for audit.</summary>
public record VoidPaymentRequestModel(string Reason);
