using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// Voice lands the same way email does. These pin the parts that differ: a call
/// with no recording is normal, a transcript is hashed apart from the audio, and
/// the caller resolves by number rather than address.
/// </summary>
public class IngestVoiceCallHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly IngestVoiceCallHandler _handler;

    public IngestVoiceCallHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        var storage = new MockStorageService(NullLogger<MockStorageService>.Instance);
        var artifacts = new ArtifactStore(
            _db, storage,
            Options.Create(new MinioOptions { JobFilesBucket = "forge-files" }),
            new FixedClock(DateTimeOffset.Parse("2026-08-15T14:11:00Z")),
            NullLogger<ArtifactStore>.Instance);
        _handler = new IngestVoiceCallHandler(
            _db, new PartyResolver(_db, NullLogger<PartyResolver>.Instance),
            artifacts, NullLogger<IngestVoiceCallHandler>.Instance);
    }

    private Customer SeedCustomerWithPhone(string phone = "(503) 555-1212")
    {
        var customer = new Customer { Name = "Bob's Parts", IsActive = true };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Bob", LastName = "Vance", Phone = phone,
        });
        _db.SaveChanges();
        return customer;
    }

    private static InboundCall Call(
        string from = "+15035551212",
        string externalId = "call-001",
        string? transcript = null,
        byte[]? recording = null) =>
        new(externalId, from, "+15035559000", CommunicationFlow.Inbound,
            DateTimeOffset.Parse("2026-08-15T14:11:00Z"), 7, "completed", transcript, recording);

    [Fact]
    public async Task ResolvesTheCallerByNumberDespiteFormatting()
    {
        SeedCustomerWithPhone("(503) 555-1212");

        var result = await _handler.Handle(new IngestVoiceCallCommand(Call()), default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
        var comm = _db.Communications.Single(c => c.Id == result.CommunicationId);
        comm.Channel.Should().Be(CommunicationChannel.Voice);
        comm.Type.Should().Be(InteractionType.Call);
        comm.DurationMinutes.Should().Be(7);
    }

    [Fact]
    public async Task ACallWithNoRecordingIsNormalAndStillLands()
    {
        // Two-party-consent states make recording a legal decision, not a
        // feature toggle. An unrecorded call is the common case.
        SeedCustomerWithPhone();

        var result = await _handler.Handle(new IngestVoiceCallCommand(Call()), default);

        result.CommunicationId.Should().NotBeNull();
        _db.CommunicationArtifacts.Where(a => a.CommunicationId == result.CommunicationId)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task HashesTheTranscriptSeparatelyFromTheAudio()
    {
        SeedCustomerWithPhone();

        var result = await _handler.Handle(new IngestVoiceCallCommand(
            Call(transcript: "Go ahead and ship 500 of PN-1234.",
                 recording: Encoding.UTF8.GetBytes("fake-audio"))), default);

        var artifacts = _db.CommunicationArtifacts
            .Where(a => a.CommunicationId == result.CommunicationId).ToList();

        // Two artifacts, two hashes. A dispute needs to tell "the recording says
        // X" apart from "our transcription of it says X".
        artifacts.Should().HaveCount(2);
        artifacts.Select(a => a.Sha256).Distinct().Should().HaveCount(2);
        artifacts.Should().Contain(a => a.OriginalFilename!.EndsWith("-transcript.txt"));
    }

    [Fact]
    public async Task StoresTheTranscriptAsTheBodySoItIsSearchable()
    {
        SeedCustomerWithPhone();

        var result = await _handler.Handle(new IngestVoiceCallCommand(
            Call(transcript: "Go ahead and ship 500 of PN-1234.")), default);

        _db.Communications.Single(c => c.Id == result.CommunicationId)
            .Body.Should().Contain("PN-1234");
    }

    [Fact]
    public async Task ReIngestingTheSameCallIsANoOp()
    {
        SeedCustomerWithPhone();

        var first = await _handler.Handle(new IngestVoiceCallCommand(Call()), default);
        var second = await _handler.Handle(new IngestVoiceCallCommand(Call()), default);

        second.WasAlreadyIngested.Should().BeTrue();
        second.CommunicationId.Should().Be(first.CommunicationId);
        _db.Communications.Count(c => c.ExternalId == "call-001").Should().Be(1);
    }

    [Fact]
    public async Task AnUnknownCallerLandsInTriageWithTheNumberVisible()
    {
        var result = await _handler.Handle(
            new IngestVoiceCallCommand(Call(from: "+15035559999")), default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);

        var comm = _db.Communications.Single(c => c.Id == result.CommunicationId);
        comm.PartyId.Should().BeNull();
        // The number is the only handle triage has before a party is assigned.
        comm.Subject.Should().Contain("5035559999");
    }

    [Fact]
    public async Task OutboundCallResolvesTheCalleeNotTheCaller()
    {
        SeedCustomerWithPhone("(503) 555-7777");

        var result = await _handler.Handle(new IngestVoiceCallCommand(new InboundCall(
            "call-out-1", "+15035559000", "+15035557777", CommunicationFlow.Outbound,
            DateTimeOffset.Parse("2026-08-15T14:11:00Z"), 3)), default);

        // On an outbound call the external party is who we dialled.
        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
        _db.Communications.Single(c => c.Id == result.CommunicationId)
            .Flow.Should().Be(CommunicationFlow.Outbound);
    }

    private sealed class FixedClock(DateTimeOffset now) : Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
