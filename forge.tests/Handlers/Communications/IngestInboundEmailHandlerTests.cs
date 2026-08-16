using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Communications;
using Forge.Api.Features.Communications.Extraction;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models;
using Forge.Core.Models.Extraction;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// The pipeline's guarantees: evidence is always stored, ingestion is idempotent,
/// nothing is committed without a human, and a failing extractor costs a draft's
/// contents but never the evidence.
/// </summary>
public class IngestInboundEmailHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly MockStorageService _storage;
    private readonly ArtifactStore _artifacts;
    private readonly PartyResolver _resolver;

    public IngestInboundEmailHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _storage = new MockStorageService(NullLogger<MockStorageService>.Instance);
        _artifacts = new ArtifactStore(
            _db, _storage,
            Options.Create(new MinioOptions { JobFilesBucket = "forge-files" }),
            new FixedClock(DateTimeOffset.Parse("2026-08-15T09:12:00Z")),
            NullLogger<ArtifactStore>.Instance);
        _resolver = new PartyResolver(_db, NullLogger<PartyResolver>.Instance);
    }

    private IngestInboundEmailHandler Handler(IOrderExtractor? extractor = null) =>
        new(_db, _resolver, _artifacts,
            extractor ?? new HeuristicOrderExtractor(NullLogger<HeuristicOrderExtractor>.Instance),
            NullLogger<IngestInboundEmailHandler>.Instance);

    private Customer SeedCustomerWithContact(string email = "bob@bobsparts.com")
    {
        var customer = new Customer { Name = "Bob's Parts", IsActive = true };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Bob", LastName = "Vance", Email = email,
        });
        _db.SaveChanges();
        return customer;
    }

    private static InboundEmail Email(
        string from = "bob@bobsparts.com",
        string externalId = "msg-001",
        string? body = "Please see PO Number: 8832\n500 ea PN-1234 @ $12.50\nNeed by 2026-09-30",
        IReadOnlyList<InboundEmailAttachment>? attachments = null,
        string? subject = "PO 8832") =>
        new(externalId, from, ["orders@ourshop.com"], subject, body,
            DateTimeOffset.Parse("2026-08-15T09:12:00Z"),
            Encoding.UTF8.GetBytes($"From: {from}\r\nSubject: PO 8832\r\n\r\n{body}"),
            attachments ?? []);

    // ── Evidence is always stored ──

    [Fact]
    public async Task StoresTheRawMessageEvenWhenTheSenderIsUnknown()
    {
        // No contact, no rule. The message is still evidence that someone wrote
        // in, and discarding it would lose the thing triage needs to act on.
        var result = await Handler().Handle(new IngestInboundEmailCommand(Email(from: "stranger@nowhere.test")), default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
        result.CommunicationId.Should().NotBeNull();

        var artifacts = _db.CommunicationArtifacts
            .Where(a => a.CommunicationId == result.CommunicationId).ToList();
        artifacts.Should().ContainSingle(a => a.Kind == CommunicationArtifactKind.Message);
        artifacts.Single().Sha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task StoresEveryAttachmentAsItsOwnHashedArtifact()
    {
        SeedCustomerWithContact();

        var result = await Handler().Handle(new IngestInboundEmailCommand(Email(attachments:
        [
            new InboundEmailAttachment("PO-8832.pdf", "application/pdf", [1, 2, 3]),
            new InboundEmailAttachment("MSA-signed.pdf", "application/pdf", [4, 5, 6]),
        ])), default);

        var artifacts = _db.CommunicationArtifacts
            .Where(a => a.CommunicationId == result.CommunicationId).ToList();

        artifacts.Should().HaveCount(3); // the .eml plus two attachments
        artifacts.Count(a => a.Kind == CommunicationArtifactKind.Attachment).Should().Be(2);

        // Each is hashed separately from the envelope that delivered it.
        artifacts.Select(a => a.Sha256).Distinct().Should().HaveCount(3);
    }

    // ── Idempotency ──

    [Fact]
    public async Task ReIngestingTheSameMessageIsANoOp()
    {
        SeedCustomerWithContact();
        var handler = Handler();

        var first = await handler.Handle(new IngestInboundEmailCommand(Email()), default);
        var second = await handler.Handle(new IngestInboundEmailCommand(Email()), default);

        second.WasAlreadyIngested.Should().BeTrue();
        second.CommunicationId.Should().Be(first.CommunicationId);

        _db.Communications.Count(c => c.ExternalId == "msg-001").Should().Be(1);
        // Critically, no second copy of the evidence.
        _db.CommunicationArtifacts.Count(a => a.CommunicationId == first.CommunicationId).Should().Be(1);
    }

    // ── Automated prompt, manual response ──

    [Fact]
    public async Task NeverCreatesASalesOrder()
    {
        SeedCustomerWithContact();

        var result = await Handler().Handle(new IngestInboundEmailCommand(Email()), default);

        // Everything lined up — exact sender, readable PO, quantity and price.
        result.EligibleForDraft.Should().BeTrue();

        // And still nothing was committed. There is no code path from an inbound
        // email to a live order without a person.
        _db.SalesOrders.Should().BeEmpty();
        _db.Attestations.Should().BeEmpty();
    }

    [Fact]
    public async Task DomainMatchIsFiledButNotDraftEligible()
    {
        var customer = new Customer { Name = "Bob's Parts", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Domain, Pattern = "bobsparts.com", IsEnabled = true,
            PartyType = CommunicationPartyType.Customer, PartyId = customer.Id,
        });
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(
            new IngestInboundEmailCommand(Email(from: "anyone@bobsparts.com")), default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Domain);
        result.Extraction!.FoundAnything.Should().BeTrue();

        // The extraction is perfectly good; the sender identity is not. Anyone at
        // the domain can send mail, so this can never propose an order.
        result.EligibleForDraft.Should().BeFalse();
    }

    [Fact]
    public async Task UnmatchedSenderIsNotDraftEligibleHoweverGoodTheExtraction()
    {
        var result = await Handler().Handle(
            new IngestInboundEmailCommand(Email(from: "stranger@nowhere.test")), default);

        result.Extraction!.FoundAnything.Should().BeTrue();
        result.EligibleForDraft.Should().BeFalse();
    }

    // ── Degrading, not failing ──

    [Fact]
    public async Task AFailingExtractorCostsTheDraftContentsButNotTheEvidence()
    {
        SeedCustomerWithContact();

        var result = await Handler(new ThrowingExtractor())
            .Handle(new IngestInboundEmailCommand(Email()), default);

        result.CommunicationId.Should().NotBeNull();
        result.Extraction!.FoundAnything.Should().BeFalse();
        result.Extraction.Warnings.Should().Contain(w => w.Contains("extractor failed"));
        result.EligibleForDraft.Should().BeFalse();

        // The message is stored and hashed regardless — that is the whole point
        // of hashing before anything else reads the bytes.
        _db.CommunicationArtifacts.Count(a => a.CommunicationId == result.CommunicationId).Should().Be(1);
    }

    [Fact]
    public async Task AnUnreadableMessageStillLandsWithABlankDraft()
    {
        SeedCustomerWithContact();

        var result = await Handler().Handle(
            new IngestInboundEmailCommand(
                Email(body: "Give me a call when you can.", subject: "Quick question")), default);

        result.CommunicationId.Should().NotBeNull();
        result.Extraction!.FoundAnything.Should().BeFalse();
        result.EligibleForDraft.Should().BeFalse();
    }

    // ── Filing ──

    [Fact]
    public async Task FilesTheMessageAgainstTheCustomer()
    {
        var customer = SeedCustomerWithContact();

        var result = await Handler().Handle(new IngestInboundEmailCommand(Email()), default);

        var link = _db.CommunicationLinks.Single(l => l.CommunicationId == result.CommunicationId);
        link.EntityType.Should().Be(CommunicationLink.Types.Customer);
        link.EntityId.Should().Be(customer.Id);
        link.PartyId.Should().NotBeNull();
    }

    [Fact]
    public async Task LeavesTheHandlingEmployeeUnsetOnAnAutomaticImport()
    {
        SeedCustomerWithContact();

        var result = await Handler().Handle(new IngestInboundEmailCommand(Email()), default);

        var comm = _db.Communications.Single(c => c.Id == result.CommunicationId);
        // Nobody handled it yet. Stamping a user would credit work nobody did.
        comm.HandledByUserId.Should().BeNull();
        comm.Flow.Should().Be(CommunicationFlow.Inbound);
        comm.Channel.Should().Be(CommunicationChannel.Email);
    }

    [Fact]
    public async Task ReadsTextAttachmentsAsExtractionSources()
    {
        SeedCustomerWithContact();

        var result = await Handler().Handle(new IngestInboundEmailCommand(Email(
            body: "See attached.",
            attachments: [new InboundEmailAttachment(
                "PO-8832.txt", "text/plain",
                Encoding.UTF8.GetBytes("PO Number: 8832\n250 ea PN-4471 @ $8.75"))])), default);

        result.Extraction!.CustomerPoNumber!.Value.Should().Be("8832");
        result.Extraction.Lines.Should().ContainSingle();
        result.Extraction.Lines[0].PartReference!.Value.Should().Be("PN-4471");
    }

    private sealed class ThrowingExtractor : IOrderExtractor
    {
        public string ExtractorId => "throwing-test";
        public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("model unavailable");
    }

    private sealed class FixedClock(DateTimeOffset now) : Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
