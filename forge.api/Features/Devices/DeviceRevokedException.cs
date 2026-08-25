namespace Forge.Api.Features.Devices;

/// <summary>
/// Thrown when a revoked device presents a credential. The exception
/// middleware maps this to 401 with problem code "device-revoked" — the
/// app's contract to wipe this instance's local data and return to first-run.
/// </summary>
public class DeviceRevokedException(string message) : Exception(message);
