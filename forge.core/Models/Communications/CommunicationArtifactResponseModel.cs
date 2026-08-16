namespace Forge.Core.Models.Communications;

/// <summary>
/// One stored artifact. The hash is the point of the record, so it is returned
/// in full rather than truncated — the UI shortens it for display, but a
/// reviewer copying it to verify needs all 64 characters.
/// </summary>
public record CommunicationArtifactResponseModel(
    int Id,
    string Kind,
    string Sha256,
    string ContentType,
    long ByteSize,
    string? OriginalFilename,
    DateTimeOffset IngestedAt);
