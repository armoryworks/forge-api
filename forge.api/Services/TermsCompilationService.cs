using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// S3 — compiles the terms-and-conditions sections that apply to a quote.
/// Ordering contract: company-scope docs (by SortOrder), then customer-scope,
/// then part-scope (for the quote's line parts), deduped by document id and
/// filtered to active + effective-dated rows. <see cref="Compile"/> is pure
/// (no I/O) so the ordering/filtering/truncation rules are unit-testable;
/// <see cref="CompileForQuoteAsync"/> is the convenience overload that loads
/// the candidate documents for a quote and delegates to it.
///
/// Markdown rendering: no markdown package (Markdig etc.) is referenced by
/// any project in this solution, and the "no new NuGet" rule applies — so the
/// HTML output is safe-paragraph rendering: the markdown text is HTML-encoded
/// verbatim, blank lines become paragraph breaks, single newlines become
/// &lt;br/&gt;. Swapping in a real markdown renderer later only touches
/// <see cref="RenderMarkdownAsSafeHtml"/>.
/// </summary>
public interface ITermsCompilationService
{
    /// <summary>Pure compilation over pre-loaded documents.</summary>
    CompiledTermsResult Compile(
        IReadOnlyList<TermsDocument> companyDocs,
        IReadOnlyList<TermsDocument> customerDocs,
        IReadOnlyList<TermsDocument> partDocs,
        DateTimeOffset now);

    /// <summary>
    /// Loads the candidate documents for a quote (company scope + the quote's
    /// customer + its line parts) and compiles them as of <c>IClock.UtcNow</c>.
    /// </summary>
    Task<CompiledTermsResult> CompileForQuoteAsync(
        int customerId,
        IReadOnlyCollection<int> partIds,
        CancellationToken ct);
}

public class TermsCompilationService(AppDbContext db, IClock clock) : ITermsCompilationService
{
    /// <summary>Max characters of body text used when a doc has no author Summary.</summary>
    public const int BlurbFallbackLength = 400;

    public CompiledTermsResult Compile(
        IReadOnlyList<TermsDocument> companyDocs,
        IReadOnlyList<TermsDocument> customerDocs,
        IReadOnlyList<TermsDocument> partDocs,
        DateTimeOffset now)
    {
        var seen = new HashSet<int>();
        var sections = new List<CompiledTermsSection>();

        foreach (var group in new[] { companyDocs, customerDocs, partDocs })
        {
            var ordered = group
                .Where(d => IsEffective(d, now))
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.Id);

            foreach (var doc in ordered)
            {
                if (!seen.Add(doc.Id))
                    continue;

                sections.Add(new CompiledTermsSection(
                    TermsDocumentId: doc.Id,
                    Scope: doc.Scope.ToString(),
                    Version: doc.Version,
                    Title: doc.Title,
                    Blurb: BuildBlurb(doc),
                    BodyMarkdown: doc.BodyMarkdown));
            }
        }

        return new CompiledTermsResult(sections, RenderHtml(sections));
    }

    public async Task<CompiledTermsResult> CompileForQuoteAsync(
        int customerId,
        IReadOnlyCollection<int> partIds,
        CancellationToken ct)
    {
        var docs = await db.TermsDocuments
            .AsNoTracking()
            .Where(d =>
                d.Scope == TermsScope.Company
                || (d.Scope == TermsScope.Customer && d.CustomerId == customerId)
                || (d.Scope == TermsScope.Part && d.PartId != null && partIds.Contains(d.PartId.Value)))
            .ToListAsync(ct);

        return Compile(
            companyDocs: docs.Where(d => d.Scope == TermsScope.Company).ToList(),
            customerDocs: docs.Where(d => d.Scope == TermsScope.Customer).ToList(),
            partDocs: docs.Where(d => d.Scope == TermsScope.Part).ToList(),
            now: clock.UtcNow);
    }

    private static bool IsEffective(TermsDocument doc, DateTimeOffset now)
        => doc.IsActive
           && doc.EffectiveFrom <= now
           && (doc.EffectiveTo == null || doc.EffectiveTo > now);

    private static string BuildBlurb(TermsDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Summary))
            return doc.Summary.Trim();

        var body = doc.BodyMarkdown.Trim();
        if (body.Length <= BlurbFallbackLength)
            return body;

        // Cut at the last whitespace before the limit so we never split a word.
        var cut = body.LastIndexOf(' ', BlurbFallbackLength);
        if (cut <= 0) cut = BlurbFallbackLength;
        return body[..cut].TrimEnd() + "…";
    }

    private static string RenderHtml(IReadOnlyList<CompiledTermsSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var section in sections)
        {
            sb.Append("<section>");
            sb.Append("<h2>").Append(WebUtility.HtmlEncode(section.Title)).Append("</h2>");
            sb.Append(RenderMarkdownAsSafeHtml(section.BodyMarkdown));
            sb.Append("</section>\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Pre-safe paragraph rendering (see class doc): encode everything, blank
    /// lines → paragraphs, single newlines → &lt;br/&gt;. Never emits raw
    /// author markup, so the compiled HTML is XSS-safe by construction.
    /// </summary>
    private static string RenderMarkdownAsSafeHtml(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0)
            return string.Empty;

        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            sb.Append("<p>")
              .Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br/>"))
              .Append("</p>");
        }
        return sb.ToString();
    }
}
