namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a <c>PaymentTransmission</c> (electronic bank/ACH submission of a payment).
/// Queued → (Retrying)* → Succeeded | Failed. Failed/Cancelled rows can be manually re-queued
/// via the retry endpoint, which starts a fresh attempt cycle.
/// </summary>
public enum PaymentTransmissionStatus
{
    Queued,
    Retrying,
    Succeeded,
    Failed,
    Cancelled,
}
