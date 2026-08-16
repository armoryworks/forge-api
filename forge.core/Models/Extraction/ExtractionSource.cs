namespace Forge.Core.Models.Extraction;

/// <summary>
/// One body of text an extractor may read, tagged with where it came from.
///
/// <para>Provenance matters to the reviewer: a quantity read out of the PO PDF
/// carries different weight from one read out of a chatty email body, and the
/// review screen shows which. It also matters to the extractor, which can weight
/// a structured attachment above prose.</para>
/// </summary>
public sealed record ExtractionSource(
    ExtractionSourceKind Kind,
    string Text,
    /// <summary>Artifact this text came from, when it came from one. Null for the message body itself.</summary>
    int? ArtifactId = null,
    string? Filename = null);

public enum ExtractionSourceKind
{
    /// <summary>The email body.</summary>
    MessageBody,

    /// <summary>Text pulled from an attachment — usually the customer's PO PDF.</summary>
    Attachment,

    /// <summary>A call transcript. Same contract; voice is just another channel.</summary>
    Transcript,
}
