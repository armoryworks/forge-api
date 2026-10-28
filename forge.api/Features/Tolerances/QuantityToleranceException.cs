using Forge.Core.Models;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// Thrown when a receipt, receive-back or shipment would go above its over-quantity tolerance without an
/// accepted override. The exception middleware maps it to 409 with problem code "quantity-tolerance" and the
/// details in "tolerance"; the client shows them and, when the caller may override, resends with
/// ConfirmOverTolerance=true and an OverToleranceReason.
/// </summary>
public class QuantityToleranceException(string message, QuantityToleranceViolationResponseModel violation)
    : InvalidOperationException(message)
{
    public const string Code = "quantity-tolerance";

    public QuantityToleranceViolationResponseModel Violation { get; } = violation;
}
