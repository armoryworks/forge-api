namespace Forge.Core.Enums;

/// <summary>What a stored artifact is a copy of.</summary>
public enum CommunicationArtifactKind
{
    /// <summary>The raw RFC 5322 message (.eml) exactly as received. At most one per communication.</summary>
    Message,

    /// <summary>One file carried by the message. Hashed separately from the envelope that delivered it.</summary>
    Attachment,
}
