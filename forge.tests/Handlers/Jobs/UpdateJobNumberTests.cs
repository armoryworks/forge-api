using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobNumberTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateJobHandler _handler;

    public UpdateJobNumberTests()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _handler = new UpdateJobHandler(
            _repo.Object,
            Mock.Of<IActivityLogRepository>(),
            Mock.Of<IMediator>(),
            _boardHub.Object,
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _settings.Object,
            _identifiers.Object,
            _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("jobs.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "jobs.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoFor(Job job) =>
        _repo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

    private static UpdateJobCommand WithJobNumber(int id, string jobNumber) =>
        new(id, null, null, null, null, null, null, null, null, JobNumber: jobNumber);

    [Fact]
    public async Task Renames_the_job_number_when_manual_numbers_allowed_and_unique()
    {
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test" };
        SetupRepoFor(job);
        AllowManualNumbers(true);
        _repo.Setup(r => r.JobNumberExistsAsync("ACME-J", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithJobNumber(1, "ACME-J"), CancellationToken.None);

        job.JobNumber.Should().Be("ACME-J");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Job, 1, "ACME-J", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_job_number()
    {
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test" };
        SetupRepoFor(job);
        AllowManualNumbers(true);
        _repo.Setup(r => r.JobNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithJobNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        job.JobNumber.Should().Be("J-1");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test" };
        SetupRepoFor(job);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithJobNumber(1, "ACME-J"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        job.JobNumber.Should().Be("J-1");
    }

    [Fact]
    public async Task Rejects_a_rename_once_the_job_is_disposed()
    {
        // Lifecycle gate — the job number is editable only while the job is not yet disposed.
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test", Disposition = JobDisposition.Scrap };
        SetupRepoFor(job);
        AllowManualNumbers(true);
        _repo.Setup(r => r.JobNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(WithJobNumber(1, "ACME-J"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disposed");
        job.JobNumber.Should().Be("J-1");
    }
}
