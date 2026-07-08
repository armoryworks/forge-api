namespace Forge.Core.Models;

/// <summary>
/// One ordered section of a quote's compiled terms &amp; conditions.
/// <paramref name="Blurb"/> is the author-controlled Summary when present,
/// otherwise a 400-character truncation of the body text — it's what emails
/// show inline before the "view full terms" link.
/// </summary>
public record CompiledTermsSection(
    int TermsDocumentId,
    string Scope,
    int Version,
    string Title,
    string Blurb,
    string BodyMarkdown);
