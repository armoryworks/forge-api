namespace Forge.Core.Enums;

/// <summary>
/// How a carrier is wired to Forge. <see cref="Manual"/> covers custom / "shadow" shippers and
/// known carriers used without a live API (the user records tracking by hand); <see cref="Api"/>
/// is a carrier with a configured rate/label/tracking integration (UPS, FedEx, USPS, DHL). Drives
/// the label source and delivery automation — NOT whether the scan-to-ship gate applies, which is
/// the per-carrier <c>RequiresScanToShip</c> flag.
/// </summary>
public enum CarrierIntegrationKind
{
    Manual,
    Api,
}
