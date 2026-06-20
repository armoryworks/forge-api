using Forge.Core.Entities;

namespace Forge.Api.Features.Documents;

/// <summary>
/// The general versioned-document store. Stores a generated file as the next version of a logical
/// document (identified by kind + a primary entity), end-dating + archiving the prior version, and
/// links the document to one or more entities. Reads the current (non-archived) version's bytes.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Store <paramref name="bytes"/> as a new version of the (kind, primary) document, archiving the prior one.</summary>
    Task<DocumentSetVersion> StoreAsync(
        string kind,
        DocumentLinkTarget primary,
        IReadOnlyCollection<DocumentLinkTarget> links,
        byte[] bytes,
        string fileName,
        string contentType,
        CancellationToken ct);

    /// <summary>Bytes of the current (active) version for the (kind, primary) document, or null if none exists.</summary>
    Task<byte[]?> ReadCurrentAsync(string kind, DocumentLinkTarget primary, CancellationToken ct);
}
