namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a <c>PaymentTransmission</c> (electronic bank/ACH submission of a payment).
/// Queued → (Retrying)* → Succeeded | Failed. Failed/Cancelled rows can be manually re-queued
/// via the retry endpoint, which starts a fresh attempt cycle. A wire created while
/// banking.wire.manual-attestation is on starts at AwaitingAttestation instead of Queued: no job
/// or sweep submits it, and only a second user's attestation (or a void) moves it on.
/// </summary>
public enum PaymentTransmissionStatus
{
    Queued,
    Retrying,
    Succeeded,
    Failed,
    Cancelled,
    AwaitingAttestation,
}
