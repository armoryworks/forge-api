namespace Forge.Core.Enums;

/// <summary>
/// Wave 8 — channel an inbound communication arrived through. Drives which
/// capability gates the integration (CAP-EXT-EMAIL-SYNC vs CAP-EXT-VOIP-SYNC),
/// which provider adapters apply, and which match field on Lead/Contact is
/// the primary lookup (Email vs Phone).
/// </summary>
public enum CommunicationKind
{
    Email = 0,
    Voice = 1,
}
