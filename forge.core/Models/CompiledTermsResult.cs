namespace Forge.Core.Models;

/// <summary>
/// Output of terms compilation for a quote: the ordered sections
/// (company → customer → part, SortOrder within each group) plus a single
/// self-contained HTML rendering of every section body — the value persisted
/// on <c>QuoteTermsSnapshot.CompiledHtml</c>.
/// </summary>
public record CompiledTermsResult(
    List<CompiledTermsSection> Sections,
    string Html);
