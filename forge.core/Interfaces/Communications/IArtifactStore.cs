using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Core.Interfaces.Communications;

/// <summary>
/// Takes Forge's own immutable copy of something a communication carried, and
/// hashes it on the way past.
///
/// <para><b>The hash is computed while the bytes stream to storage</b> — one
/// pass, at the boundary, before any parser, extractor or virus scanner has
/// touched them. Hashing later would attest to whatever the pipeline had
/// already done to the file, which is not the thing the customer sent.</para>
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Stream <paramref name="content"/> into storage, hashing as it goes, and
    /// return the persisted artifact row.
    ///
    /// <para>The caller supplies the sender's filename for display only. It is
    /// never used to build the storage key — an attachment named
    /// <c>../../etc/passwd</c> is a filename, not a path.</para>
    /// </summary>
    Task<CommunicationArtifact> StoreAsync(
        int communicationId,
        CommunicationArtifactKind kind,
        Stream content,
        string contentType,
        string? originalFilename,
        CancellationToken ct);

    /// <summary>Read an artifact's bytes back for viewing or re-verification.</summary>
    Task<Stream> OpenAsync(CommunicationArtifact artifact, CancellationToken ct);

    /// <summary>
    /// Re-hash the stored bytes and compare against the recorded digest.
    ///
    /// <para>Exists so the audit line can be proven rather than merely
    /// displayed: the hash on screen means nothing if nobody can check it still
    /// matches what is in the bucket.</para>
    /// </summary>
    Task<bool> VerifyAsync(CommunicationArtifact artifact, CancellationToken ct);
}
