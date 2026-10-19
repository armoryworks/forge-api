using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Ai;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

/// <summary>
/// The recurring document-index job is a no-op while CAP-EXT-AI-ASSISTANT is off: it neither probes the AI
/// host nor sends any index command.
/// </summary>
public class DocumentIndexJobCapabilityTests
{
    private const string AiCapability = "CAP-EXT-AI-ASSISTANT";

    private static async Task<(DocumentIndexJob Job, Mock<IAiService> Ai, Mock<IMediator> Mediator, int PartId)> CreateAsync(
        StubCapabilitySnapshotProvider capabilities)
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "BRK-100", Name = "Bracket", Description = "Bracket" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IndexDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        mediator.Setup(m => m.Send(It.IsAny<IndexDocumentationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var job = new DocumentIndexJob(db, mediator.Object, ai.Object, new SystemClock(),
            NullLogger<DocumentIndexJob>.Instance, capabilities);
        return (job, ai, mediator, part.Id);
    }

    [Fact]
    public async Task Capability_off_never_reaches_the_ai_host_or_indexes()
    {
        var (job, ai, mediator, _) = await CreateAsync(StubCapabilitySnapshotProvider.Off);

        await job.IndexRecentlyUpdatedAsync();
        await job.IndexDocumentationAsync();

        ai.Verify(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()), Times.Never);
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Capability_on_indexes_the_recently_updated_part()
    {
        var (job, _, mediator, partId) = await CreateAsync(new StubCapabilitySnapshotProvider(AiCapability));

        await job.IndexRecentlyUpdatedAsync();
        await job.IndexDocumentationAsync();

        mediator.Verify(m => m.Send(It.Is<IndexDocumentCommand>(c => c.EntityType == "Part" && c.EntityId == partId), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(It.IsAny<IndexDocumentationCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
