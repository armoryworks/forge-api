using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Communications.Extraction;
using Forge.Core.Models.Extraction;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// The extractor's contract, not its cleverness: it reads labelled fields, it
/// reports ambiguity rather than resolving it, and it returns an empty result
/// instead of throwing or guessing.
/// </summary>
public class HeuristicOrderExtractorTests
{
    private readonly HeuristicOrderExtractor _extractor =
        new(NullLogger<HeuristicOrderExtractor>.Instance);

    private Task<ExtractionResult> Extract(string body, string? subject = null) =>
        _extractor.ExtractAsync(
            new ExtractionRequest(
                [new ExtractionSource(ExtractionSourceKind.MessageBody, body)],
                Subject: subject),
            default);

    // ── Purchase-order number ──

    [Theory]
    [InlineData("PO Number: 8832", "8832")]
    [InlineData("P.O. # 8832", "8832")]
    [InlineData("Purchase Order No. ACME-8832", "ACME-8832")]
    [InlineData("our po 8832 refers", "8832")]
    [InlineData("Customer PO: 22/8832-B", "22/8832-B")]
    public async Task ReadsLabelledPoNumber(string body, string expected)
    {
        var result = await Extract(body);

        result.CustomerPoNumber.Should().NotBeNull();
        result.CustomerPoNumber!.Value.Should().Be(expected);
        result.CustomerPoNumber.Confidence.Should().Be(ExtractionConfidence.High);
        result.CustomerPoNumber.Evidence.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FallsBackToSubjectAtLowerConfidence()
    {
        var result = await Extract("See attached.", subject: "PO 8832 — please confirm");

        result.CustomerPoNumber!.Value.Should().Be("8832");
        // A subject survives forwarding and re-forwarding, so it earns less
        // trust than a labelled field inside the document.
        result.CustomerPoNumber.Confidence.Should().Be(ExtractionConfidence.Medium);
    }

    [Fact]
    public async Task ReportsConflictingPoNumbersRatherThanPickingSilently()
    {
        var result = await Extract("Supersedes PO 8830.\nNew PO Number: 8832");

        result.Warnings.Should().NotBeNull();
        result.Warnings!.Should().ContainSingle(w => w.Contains("More than one purchase-order number"));
        result.Warnings!.Single().Should().Contain("8830").And.Contain("8832");
    }

    // ── Need-by date ──

    [Theory]
    [InlineData("Need by 2026-09-15")]
    [InlineData("Needed by: 09/15/2026")]
    [InlineData("Delivery date: September 15, 2026")]
    [InlineData("Ship by 15 Sep 2026")]
    public async Task ReadsLabelledNeedByDate(string body)
    {
        var result = await Extract(body);

        result.NeedByDate.Should().NotBeNull();
        result.NeedByDate!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 9, 15));
    }

    [Fact]
    public async Task WarnsWhenADateLabelIsPresentButUnreadable()
    {
        var result = await Extract("Need by whenever you can manage");

        result.NeedByDate.Should().BeNull();
        result.Warnings.Should().NotBeNull();
        result.Warnings!.Should().Contain(w => w.Contains("could not read it as a date"));
    }

    [Fact]
    public async Task ParsesAmbiguousSlashDatesAsUsOrder()
    {
        // 03/04 is genuinely ambiguous. Picking a documented default beats
        // guessing silently, and the reviewer sees the evidence string either way.
        var result = await Extract("Need by 03/04/2026");

        result.NeedByDate!.Value.UtcDateTime.Month.Should().Be(3);
        result.NeedByDate.Value.UtcDateTime.Day.Should().Be(4);
    }

    // ── Line items ──

    [Theory]
    [InlineData("500 ea PN-1234")]
    [InlineData("500 x PN-1234")]
    [InlineData("Qty 500 PN-1234")]
    [InlineData("500 of PN-1234")]
    [InlineData("500 pcs PN-1234")]
    public async Task ReadsQuantityAndPartFromALine(string body)
    {
        var result = await Extract(body);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Quantity!.Value.Should().Be(500m);
        result.Lines[0].PartReference!.Value.Should().Be("PN-1234");
    }

    [Fact]
    public async Task ReadsThousandsSeparatedQuantities()
    {
        var result = await Extract("Qty 1,500 ea PN-1234");

        result.Lines[0].Quantity!.Value.Should().Be(1500m);
    }

    [Theory]
    [InlineData("500 ea PN-1234 @ $12.50", 12.50)]
    [InlineData("500 ea PN-1234 at 12.50", 12.50)]
    [InlineData("500 ea PN-1234 unit price: $1,250.00", 1250.00)]
    public async Task ReadsUnitPriceWhenPresent(string body, double expected)
    {
        var result = await Extract(body);

        result.Lines[0].UnitPrice.Should().NotBeNull();
        result.Lines[0].UnitPrice!.Value.Should().Be((decimal)expected);
    }

    [Fact]
    public async Task LeavesUnitPriceNullWhenTheLineHasNone()
    {
        var result = await Extract("500 ea PN-1234");

        result.Lines[0].UnitPrice.Should().BeNull();
    }

    [Theory]
    [InlineData("Thanks for the 3 quick answers")]
    [InlineData("I have 2 questions about the order")]
    [InlineData("call me back in 10 minutes")]
    public async Task DoesNotTreatOrdinaryProseAsALineItem(string body)
    {
        // The part reference must contain a digit, which is what keeps counted
        // nouns in a sentence out of the results.
        var result = await Extract(body);

        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task PrefersTheAttachmentOverTheEmailBodyRestatingIt()
    {
        var result = await _extractor.ExtractAsync(
            new ExtractionRequest([
                new ExtractionSource(ExtractionSourceKind.MessageBody, "As discussed, 500 ea PN-9999"),
                new ExtractionSource(ExtractionSourceKind.Attachment, "500 ea PN-1234 @ $12.50",
                    ArtifactId: 42, Filename: "PO-8832.pdf"),
            ]),
            default);

        // The PO document is what the customer authored on purpose; the email
        // around it is usually a paraphrase. Taking both would double the order.
        result.Lines.Should().ContainSingle();
        result.Lines[0].PartReference!.Value.Should().Be("PN-1234");
        result.Lines[0].PartReference!.ArtifactId.Should().Be(42);
    }

    // ── Degrading, not failing ──

    [Fact]
    public async Task ReturnsEmptyRatherThanThrowingOnUnreadableText()
    {
        var result = await Extract("Hi — can you give me a call when you get a sec? Thanks!");

        result.FoundAnything.Should().BeFalse();
        result.Lines.Should().BeEmpty();
        result.ExtractorId.Should().Be("heuristic-v1");
    }

    [Fact]
    public async Task ReturnsEmptyWithAWarningWhenThereIsNoTextAtAll()
    {
        var result = await _extractor.ExtractAsync(new ExtractionRequest([]), default);

        result.FoundAnything.Should().BeFalse();
        result.Warnings.Should().NotBeNull();
        result.Warnings!.Should().Contain(w => w.Contains("No readable text"));
    }

    [Fact]
    public async Task StampsTheExtractorIdSoAnImplementationSwapStaysTraceable()
    {
        var result = await Extract("PO Number: 8832");

        result.ExtractorId.Should().Be("heuristic-v1");
    }

    [Fact]
    public async Task ReadsAWholeRealisticPurchaseOrder()
    {
        var result = await Extract(
            """
            Hi Dan,

            Please see our PO Number: 8832 attached.

            250 ea PN-4471 @ $8.75
            100 ea PN-4472 @ $22.00

            Need by 2026-09-30. Same terms as last time.

            Thanks,
            Bob
            """,
            subject: "PO 8832");

        result.CustomerPoNumber!.Value.Should().Be("8832");
        result.NeedByDate!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 9, 30));
        result.Lines.Should().HaveCount(2);
        result.Lines[0].PartReference!.Value.Should().Be("PN-4471");
        result.Lines[0].UnitPrice!.Value.Should().Be(8.75m);
        result.Lines[1].PartReference!.Value.Should().Be("PN-4472");
        result.Lines[1].UnitPrice!.Value.Should().Be(22.00m);
    }
}
