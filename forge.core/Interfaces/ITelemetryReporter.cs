namespace Forge.Core.Interfaces;

/// <summary>
/// Talks to Armory Works on behalf of an opted-in install: enrolls it, collects the
/// decision, and reports health on a timer.
///
/// Every method is a no-op unless the operator has accepted the agreement AND the
/// endpoint is configured. That check lives inside the implementation rather than at
/// the call sites so there is exactly one place where "are we allowed to send
/// anything" is decided — a second opinion elsewhere is how consent gates get bypassed
/// by accident.
/// </summary>
public interface ITelemetryReporter
{
    /// <summary>
    /// One cycle: enroll if needed, poll for the decision if pending, send a heartbeat
    /// if accepted. Safe to call on a schedule; does nothing when opted out.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
