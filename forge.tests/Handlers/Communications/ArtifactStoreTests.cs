using System.Security.Cryptography;
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
/// The hash is the whole claim. These pin that it is taken over exactly the
/// bytes that reach storage, that it survives a round trip, and that a sender's
/// filename can never influence where those bytes land.
/// </summary>
public class ArtifactStoreTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly MockStorageService _storage;
    private readonly ArtifactStore _store;

    public ArtifactStoreTests()
    {
        _db = TestDbContextFactory.Create();
        _storage = new MockStorageService(NullLogger<MockStorageService>.Instance);
        _store = new ArtifactStore(
            _db,
            _storage,
            Options.Create(new MinioOptions { JobFilesBucket = "forge-files" }),
            new FixedClock(DateTimeOffset.Parse("2026-08-15T09:12:00Z")),
            NullLogger<ArtifactStore>.Instance);
    }

    private async Task<int> SeedCommunicationAsync()
    {
        var comm = new Communication
        {
            Subject = "PO 8832",
            OccurredAt = DateTimeOffset.Parse("2026-08-15T09:12:00Z"),
            Channel = CommunicationChannel.Email,
            Flow = CommunicationFlow.Inbound,
        };
        _db.Communications.Add(comm);
        await _db.SaveChangesAsync();
        return comm.Id;
    }

    private static string ExpectedHash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task Store_HashesExactlyTheBytesGiven()
    {
        var id = await SeedCommunicationAsync();
        var bytes = Encoding.UTF8.GetBytes("From: bob@bobsparts.com\r\nSubject: PO 8832\r\n\r\nGo ahead.");

        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Message, new MemoryStream(bytes),
            "message/rfc822", "PO-8832.eml", default);

        artifact.Sha256.Should().Be(ExpectedHash(bytes));
        artifact.Sha256.Should().HaveLength(64);
        artifact.ByteSize.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task Store_WritesTheSameBytesItHashed()
    {
        var id = await SeedCommunicationAsync();
        var bytes = Encoding.UTF8.GetBytes("attachment payload");

        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream(bytes),
            "application/pdf", "PO-8832.pdf", default);

        await using var round = await _store.OpenAsync(artifact, default);
        using var buffer = new MemoryStream();
        await round.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(bytes);
        (await _store.VerifyAsync(artifact, default)).Should().BeTrue();
    }

    [Fact]
    public async Task Verify_FailsWhenStoredBytesNoLongerMatch()
    {
        var id = await SeedCommunicationAsync();
        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream(Encoding.UTF8.GetBytes("original")),
            "text/plain", "note.txt", default);

        // Overwrite the object behind the store's back. The row is immutable in
        // production; the bucket is what this proves we can still check.
        await _storage.UploadAsync(
            artifact.BucketName, artifact.ObjectKey,
            new MemoryStream(Encoding.UTF8.GetBytes("tampered")), "text/plain", default);

        (await _store.VerifyAsync(artifact, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Store_NeverPutsTheSenderFilenameInTheStorageKey()
    {
        var id = await SeedCommunicationAsync();

        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream([1, 2, 3]),
            "application/octet-stream", "../../../etc/passwd", default);

        // The key is built from ids and the digest. A hostile filename is
        // display text and nothing more.
        artifact.ObjectKey.Should().NotContain("..");
        artifact.ObjectKey.Should().NotContain("passwd");
        artifact.ObjectKey.Should().Be($"communications/{id}/{artifact.Sha256}");

        // It is still retained verbatim for the reviewer to see.
        artifact.OriginalFilename.Should().Be("../../../etc/passwd");
    }

    [Fact]
    public async Task Store_KeepsAKnownExtensionForBrowsability()
    {
        var id = await SeedCommunicationAsync();

        var pdf = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream([9]),
            "application/pdf", "PO-8832.pdf", default);
        pdf.ObjectKey.Should().EndWith(".pdf");

        var eml = await _store.StoreAsync(
            id, CommunicationArtifactKind.Message, new MemoryStream([8]),
            "message/rfc822", null, default);
        eml.ObjectKey.Should().EndWith(".eml");
    }

    [Fact]
    public async Task Store_IsContentAddressed_SoIdenticalBytesShareAKey()
    {
        var id = await SeedCommunicationAsync();
        var bytes = Encoding.UTF8.GetBytes("identical");

        var first = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream(bytes), "text/plain", "a.txt", default);
        var second = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream(bytes), "text/plain", "b.txt", default);

        first.Sha256.Should().Be(second.Sha256);
        first.ObjectKey.Should().Be(second.ObjectKey);

        // Two rows, one object: the same document forwarded twice is two pieces
        // of evidence about two messages, not one deduplicated fact.
        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public async Task Store_HandlesEmptyContentWithoutInventingAHash()
    {
        var id = await SeedCommunicationAsync();

        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream([]),
            "application/octet-stream", "empty.bin", default);

        artifact.ByteSize.Should().Be(0);
        // SHA-256 of the empty input is a real, well-known digest.
        artifact.Sha256.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task Store_DefaultsAMissingContentType()
    {
        var id = await SeedCommunicationAsync();

        var artifact = await _store.StoreAsync(
            id, CommunicationArtifactKind.Attachment, new MemoryStream([1]),
            "", "x.bin", default);

        artifact.ContentType.Should().Be("application/octet-stream");
    }

    private sealed class FixedClock(DateTimeOffset now) : Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
