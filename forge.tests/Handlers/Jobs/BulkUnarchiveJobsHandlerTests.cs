using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs.Bulk;
using Forge.Api.Features.StatusTracking;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class BulkUnarchiveJobsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly BulkUnarchiveJobsHandler _handler;

    public BulkUnarchiveJobsHandlerTests()
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(hubClients.Object);

        _handler = new BulkUnarchiveJobsHandler(
            _jobRepo.Object,
            new ActivityLogRepository(_db),
            boardHub.Object,
            Mock.Of<ISystemAuditWriter>(),
            new SystemClock(),
            _db);
    }

    [Fact]
    public async Task Unarchive_ClosesTheActiveArchivedWorkflowStatus()
    {
        var job = new Job
        {
            JobNumber = "J-UNARCH-1",
            Title = "Status-archived job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            IsArchived = true,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _jobRepo.Setup(r => r.FindMultipleAsync(It.IsAny<List<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => _db.SaveChangesAsync(CancellationToken.None));
        _db.StatusEntries.Add(new StatusEntry
        {
            EntityType = "job",
            EntityId = job.Id,
            StatusCode = SetWorkflowStatusHandler.JobArchivedStatusCode,
            StatusLabel = "Archived",
            Category = "workflow",
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new BulkUnarchiveJobsCommand([job.Id]), CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
        _db.StatusEntries.Single(s => s.EntityId == job.Id).EndedAt.Should().NotBeNull();
    }
}
