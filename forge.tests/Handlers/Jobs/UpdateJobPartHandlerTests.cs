using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Parts;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobPartHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly DefaultHttpContext _http = new();
    private readonly UpdateJobPartHandler _handler;

    public UpdateJobPartHandlerTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_proxy.Object);
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);
        SignInAs("Manager");

        _handler = new UpdateJobPartHandler(
            _db,
            new HttpContextAccessor { HttpContext = _http },
            boardHub.Object,
            Mock.Of<IMediator>());
    }

    private void SignInAs(string role) =>
        _http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    private async Task<Job> SeedAsync()
    {
        _db.Parts.AddRange(
            new Part { Id = 800, PartNumber = "40-1700M", Description = "Clutch weight" },
            new Part { Id = 801, PartNumber = "40-1800M", Description = "Spacer" });
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test", TrackTypeId = 1, CurrentStageId = 1, PartId = 800 };
        job.JobParts.Add(new JobPart { Id = 10, PartId = 800, Quantity = 1m });
        job.JobParts.Add(new JobPart { Id = 11, PartId = 801, Quantity = 2m });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Changing_the_build_parts_quantity_is_logged_as_the_job_quantity()
    {
        await SeedAsync();
        _db.CurrentUserId = 17;

        await _handler.Handle(new UpdateJobPartCommand(1, 10, 300m, null), CancellationToken.None);

        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == 1);
        log.Action.Should().Be(ActivityAction.FieldChanged);
        log.FieldName.Should().Be("Quantity");
        log.OldValue.Should().Be("1");
        log.NewValue.Should().Be("300");
        log.UserId.Should().Be(17);
    }

    [Fact]
    public async Task Changing_another_parts_quantity_names_that_part()
    {
        await SeedAsync();

        await _handler.Handle(new UpdateJobPartCommand(1, 11, 5m, null), CancellationToken.None);

        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == 1);
        log.FieldName.Should().BeNull();
        log.Description.Should().Contain("40-1800M");
    }

    [Fact]
    public async Task A_notes_only_edit_logs_nothing()
    {
        await SeedAsync();

        await _handler.Handle(new UpdateJobPartCommand(1, 10, 1m, "Rush"), CancellationToken.None);

        (await _db.JobActivityLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Changing_the_build_quantity_tells_the_board()
    {
        await SeedAsync();

        await _handler.Handle(new UpdateJobPartCommand(1, 10, 300m, null), CancellationToken.None);

        _proxy.Verify(p => p.SendCoreAsync("jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Only_a_supervisor_can_change_the_build_quantity()
    {
        await SeedAsync();
        SignInAs("ProductionWorker");

        var act = () => _handler.Handle(new UpdateJobPartCommand(1, 10, 300m, null), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await _db.JobParts.SingleAsync(jp => jp.Id == 10)).Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task Any_job_role_can_still_change_another_parts_quantity()
    {
        await SeedAsync();
        SignInAs("ProductionWorker");

        await _handler.Handle(new UpdateJobPartCommand(1, 11, 5m, null), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.Id == 11)).Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task A_finished_job_keeps_its_build_quantity()
    {
        var job = await SeedAsync();
        job.CompletedDate = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(new UpdateJobPartCommand(1, 10, 300m, null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.QuantityLockedMessage);
    }
}
