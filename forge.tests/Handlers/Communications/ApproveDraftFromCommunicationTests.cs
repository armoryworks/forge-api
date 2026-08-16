using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// The commit point. These pin that an order can only cite evidence from the
/// message being approved, that the attestation links order to document to
/// message, and that approving reads the reviewer's values rather than the
/// extraction's.
/// </summary>
public class ApproveDraftFromCommunicationTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly ArtifactStore _artifacts;
    private readonly ApproveDraftFromCommunicationHandler _handler;

    public ApproveDraftFromCommunicationTests()
    {
        _db = TestDbContextFactory.Create();
        var storage = new MockStorageService(NullLogger<MockStorageService>.Instance);
        _artifacts = new ArtifactStore(
            _db, storage, Options.Create(new MinioOptions { JobFilesBucket = "f" }),
            new FixedClock(DateTimeOffset.Parse("2026-08-15T14:30:00Z")),
            NullLogger<ArtifactStore>.Instance);

        _handler = new ApproveDraftFromCommunicationHandler(
            _db, new SalesOrderRepository(_db), new NoopBarcodeService(),
            new FixedClock(DateTimeOffset.Parse("2026-08-15T14:30:00Z")));
    }

    private async Task<(Customer customer, Communication comm, CommunicationArtifact artifact)> SeedAsync()
    {
        var customer = new Customer { Name = "Bob's Parts", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var comm = new Communication
        {
            Channel = CommunicationChannel.Email,
            Flow = CommunicationFlow.Inbound,
            Type = InteractionType.Email,
            Subject = "PO 8832",
            FromAddress = "bob@bobsparts.com",
            OccurredAt = DateTimeOffset.Parse("2026-08-15T09:12:00Z"),
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
            MatchConfidence = CommunicationMatchConfidence.Exact,
        };
        _db.Communications.Add(comm);
        await _db.SaveChangesAsync();

        var artifact = await _artifacts.StoreAsync(
            comm.Id, CommunicationArtifactKind.Attachment,
            new MemoryStream(Encoding.UTF8.GetBytes("PO 8832 content")),
            "application/pdf", "PO-8832.pdf", default);

        return (customer, comm, artifact);
    }

    private static List<CreateSalesOrderLineModel> Lines() =>
        [new CreateSalesOrderLineModel(null, "PN-1234", 500m, 12.50m, null)];

    [Fact]
    public async Task CreatesADraftOrderLinkedToItsAuthorization()
    {
        var (customer, comm, artifact) = await SeedAsync();

        var result = await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, "8832", null, 0.06m, Lines()), default);

        var order = _db.SalesOrders.Single(o => o.Id == result.SalesOrderId);

        // Draft, not Confirmed. Approving the read of a message is not the same
        // as releasing the order to production.
        order.Status.Should().Be(SalesOrderStatus.Draft);
        order.AuthorizingAttestationId.Should().Be(result.AttestationId);
        order.CustomerPO.Should().Be("8832");

        var attestation = _db.Attestations.Single(a => a.Id == result.AttestationId);
        attestation.ArtifactId.Should().Be(artifact.Id);
        attestation.CommunicationId.Should().Be(comm.Id);
        attestation.StatementType.Should().Be(AttestationStatementType.PurchaseOrder);
        attestation.Status.Should().Be(AcceptanceStatus.Accepted);
    }

    [Fact]
    public async Task CapturedAtIsWhenTheCustomerSentItNotWhenWeApproved()
    {
        var (customer, comm, artifact) = await SeedAsync();

        var result = await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, "8832", null, 0m, Lines()), default);

        var attestation = _db.Attestations.Single(a => a.Id == result.AttestationId);

        // The Authorized-by line quotes the former. For an emailed PO the two
        // differ by hours.
        attestation.CapturedAt.Should().Be(DateTimeOffset.Parse("2026-08-15T09:12:00Z"));
        attestation.AcceptedAt.Should().Be(DateTimeOffset.Parse("2026-08-15T14:30:00Z"));
    }

    [Fact]
    public async Task RefusesEvidenceFromADifferentConversation()
    {
        var (customer, comm, _) = await SeedAsync();

        // An artifact from an unrelated message. Allowing it would let an order
        // cite a document that arrived in a conversation it has nothing to do with.
        var other = new Communication
        {
            Channel = CommunicationChannel.Email, Flow = CommunicationFlow.Inbound,
            Type = InteractionType.Email, Subject = "Unrelated",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        _db.Communications.Add(other);
        await _db.SaveChangesAsync();

        var foreignArtifact = await _artifacts.StoreAsync(
            other.Id, CommunicationArtifactKind.Attachment,
            new MemoryStream([1, 2, 3]), "application/pdf", "elsewhere.pdf", default);

        var act = () => _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, foreignArtifact.Id, null, null, 0m, Lines()), default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different communication*");
    }

    [Fact]
    public async Task FilesTheMessageAgainstTheOrderItProduced()
    {
        var (customer, comm, artifact) = await SeedAsync();

        var result = await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, null, null, 0m, Lines()), default);

        _db.CommunicationLinks.Should().Contain(l =>
            l.CommunicationId == comm.Id
            && l.EntityType == CommunicationLink.Types.SalesOrder
            && l.EntityId == result.SalesOrderId);
    }

    [Fact]
    public async Task MarksTheCommunicationTriagedSoItLeavesTheQueue()
    {
        var (customer, comm, artifact) = await SeedAsync();

        await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, null, null, 0m, Lines()), default);

        _db.Communications.Single(c => c.Id == comm.Id).IsTriaged.Should().BeTrue();
    }

    [Fact]
    public async Task RecordsTheSupportingAgreementWhenTheReviewerIdentifiedOne()
    {
        var (customer, comm, artifact) = await SeedAsync();

        // The master agreement: party-scoped, no order.
        var msa = new Attestation
        {
            SalesOrderId = null,
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
            StatementType = AttestationStatementType.MasterAgreement,
            Status = AcceptanceStatus.Accepted,
            Method = AcceptanceMethod.ManualUpload,
        };
        _db.Attestations.Add(msa);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, "8832", null, 0m, Lines(),
            SupportedByAttestationId: msa.Id), default);

        _db.Attestations.Single(a => a.Id == result.AttestationId)
            .SupportedByAttestationId.Should().Be(msa.Id);
    }

    [Fact]
    public async Task UsesTheReviewersLineValues()
    {
        var (customer, comm, artifact) = await SeedAsync();

        // The reviewer corrected the quantity on screen. What they approve is
        // what gets saved — otherwise the review is cosmetic.
        var corrected = new List<CreateSalesOrderLineModel>
        {
            new(null, "PN-1234", 250m, 12.50m, null),
        };

        var result = await _handler.Handle(new ApproveDraftFromCommunicationCommand(
            comm.Id, customer.Id, artifact.Id, null, null, 0m, corrected), default);

        _db.SalesOrderLines.Single(l => l.SalesOrderId == result.SalesOrderId)
            .Quantity.Should().Be(250m);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class NoopBarcodeService : IBarcodeService
    {
        public Task<Barcode> CreateBarcodeAsync(
            BarcodeEntityType entityType, int entityId, string naturalIdentifier, CancellationToken ct = default) =>
            Task.FromResult(new Barcode());

        public Task<Barcode?> FindByValueAsync(string value, CancellationToken ct = default) =>
            Task.FromResult<Barcode?>(null);

        public Task RefreshPartBarcodeAsync(int partId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
