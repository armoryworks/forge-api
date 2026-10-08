using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs.Bulk;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class BulkArchiveJobsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly BulkArchiveJobsHandler _handler;

    public BulkArchiveJobsHandlerTests()
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(hubClients.Object);

        _handler = new BulkArchiveJobsHandler(_jobRepo.Object, new ActivityLogRepository(_db), boardHub.Object);
    }

    [Fact]
    public async Task Archive_DescribesTheJobByNumber()
    {
        var job = new Job { JobNumber = "J-3615", Title = "Bulk archive job", TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _jobRepo.Setup(r => r.FindMultipleAsync(It.IsAny<List<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => _db.SaveChangesAsync(CancellationToken.None));

        await _handler.Handle(new BulkArchiveJobsCommand([job.Id]), CancellationToken.None);

        _db.JobActivityLogs.Should().ContainSingle()
            .Which.Description.Should().Be("Archived J-3615 (bulk).");
    }
}
