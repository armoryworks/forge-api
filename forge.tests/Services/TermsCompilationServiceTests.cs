using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class TermsCompilationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 07, 12, 0, 0, TimeSpan.Zero);

    private readonly TermsCompilationService _service =
        new(TestDbContextFactory.Create(), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static TermsDocument Doc(
        int id,
        TermsScope scope,
        int sortOrder = 0,
        string? title = null,
        string body = "Body text.",
        string? summary = null,
        bool isActive = true,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null) => new()
    {
        Id = id,
        Scope = scope,
        Title = title ?? $"Doc {id}",
        Summary = summary,
        BodyMarkdown = body,
        Version = 1,
        IsActive = isActive,
        SortOrder = sortOrder,
        EffectiveFrom = effectiveFrom ?? Now.AddDays(-30),
        EffectiveTo = effectiveTo,
    };

    [Fact]
    public void Compile_OrdersCompanyThenCustomerThenPart_AndBySortOrderWithinGroup()
    {
        var result = _service.Compile(
            companyDocs: [Doc(1, TermsScope.Company, sortOrder: 2), Doc(2, TermsScope.Company, sortOrder: 1)],
            customerDocs: [Doc(3, TermsScope.Customer, sortOrder: 9)],
            partDocs: [Doc(4, TermsScope.Part, sortOrder: 0)],
            now: Now);

        result.Sections.Select(s => s.TermsDocumentId).Should().Equal(2, 1, 3, 4);
        result.Sections.Select(s => s.Scope).Should().Equal("Company", "Company", "Customer", "Part");
    }

    [Fact]
    public void Compile_FiltersInactiveAndOutOfEffectiveWindowDocs()
    {
        var result = _service.Compile(
            companyDocs:
            [
                Doc(1, TermsScope.Company),                                          // effective — kept
                Doc(2, TermsScope.Company, isActive: false),                         // inactive
                Doc(3, TermsScope.Company, effectiveFrom: Now.AddDays(1)),           // not yet effective
                Doc(4, TermsScope.Company, effectiveTo: Now.AddDays(-1)),            // expired
                Doc(5, TermsScope.Company, effectiveTo: Now),                        // EffectiveTo must be > now
                Doc(6, TermsScope.Company, effectiveFrom: Now),                      // EffectiveFrom <= now — kept
            ],
            customerDocs: [],
            partDocs: [],
            now: Now);

        result.Sections.Select(s => s.TermsDocumentId).Should().Equal(1, 6);
    }

    [Fact]
    public void Compile_DeduplicatesByDocumentId()
    {
        var doc = Doc(7, TermsScope.Customer);

        var result = _service.Compile(
            companyDocs: [],
            customerDocs: [doc],
            partDocs: [doc],
            now: Now);

        result.Sections.Should().HaveCount(1);
    }

    [Fact]
    public void Compile_UsesAuthorSummaryAsBlurb_WhenPresent()
    {
        var result = _service.Compile(
            [Doc(1, TermsScope.Company, summary: "Author blurb.", body: new string('x', 900))],
            [], [], Now);

        result.Sections.Single().Blurb.Should().Be("Author blurb.");
    }

    [Fact]
    public void Compile_TruncatesBodyForBlurb_WhenNoSummary()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 200)); // ~1000 chars
        var result = _service.Compile(
            [Doc(1, TermsScope.Company, body: body)],
            [], [], Now);

        var blurb = result.Sections.Single().Blurb;
        blurb.Should().EndWith("…");
        blurb.Length.Should().BeLessThanOrEqualTo(TermsCompilationService.BlurbFallbackLength + 1);
        blurb.Should().NotContain("word…w"); // never splits mid-word
    }

    [Fact]
    public void Compile_ShortBodyWithoutSummary_IsUsedVerbatimAsBlurb()
    {
        var result = _service.Compile(
            [Doc(1, TermsScope.Company, body: "Short body.")],
            [], [], Now);

        result.Sections.Single().Blurb.Should().Be("Short body.");
    }

    [Fact]
    public void Compile_HtmlEncodesAuthorContent_AndRendersParagraphs()
    {
        var result = _service.Compile(
            [Doc(1, TermsScope.Company,
                title: "Warranty <terms>",
                body: "First <script>alert(1)</script> paragraph.\n\nSecond line one\nline two.")],
            [], [], Now);

        result.Html.Should().NotContain("<script>");
        result.Html.Should().Contain("&lt;script&gt;");
        result.Html.Should().Contain("<h2>Warranty &lt;terms&gt;</h2>");
        result.Html.Should().Contain("<p>First &lt;script&gt;alert(1)&lt;/script&gt; paragraph.</p>");
        result.Html.Should().Contain("<p>Second line one<br/>line two.</p>");
    }

    [Fact]
    public async Task CompileForQuoteAsync_LoadsCompanyPlusMatchingCustomerAndPartDocs()
    {
        var db = TestDbContextFactory.Create();
        var service = new TermsCompilationService(db, new FixedClock(Now));

        db.TermsDocuments.AddRange(
            new TermsDocument { Scope = TermsScope.Company, Title = "Company", BodyMarkdown = "c", EffectiveFrom = Now.AddDays(-1) },
            new TermsDocument { Scope = TermsScope.Customer, CustomerId = 10, Title = "Cust 10", BodyMarkdown = "c", EffectiveFrom = Now.AddDays(-1) },
            new TermsDocument { Scope = TermsScope.Customer, CustomerId = 99, Title = "Cust 99", BodyMarkdown = "c", EffectiveFrom = Now.AddDays(-1) },
            new TermsDocument { Scope = TermsScope.Part, PartId = 5, Title = "Part 5", BodyMarkdown = "c", EffectiveFrom = Now.AddDays(-1) },
            new TermsDocument { Scope = TermsScope.Part, PartId = 6, Title = "Part 6", BodyMarkdown = "c", EffectiveFrom = Now.AddDays(-1) });
        await db.SaveChangesAsync();

        var result = await service.CompileForQuoteAsync(customerId: 10, partIds: [5], CancellationToken.None);

        result.Sections.Select(s => s.Title).Should().Equal("Company", "Cust 10", "Part 5");
    }
}
