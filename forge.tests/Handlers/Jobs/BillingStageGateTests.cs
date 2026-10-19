using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Bulk;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class BillingStageGateTests
{
    private const int TrackTypeId = 1;

    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private readonly List<JobStage> _stages =
    [
        new JobStage { Id = 6, TrackTypeId = TrackTypeId, Name = "In Production", SortOrder = 6, IsShopFloor = true },
        new JobStage { Id = 7, TrackTypeId = TrackTypeId, Name = "QC/Review", SortOrder = 7, IsShopFloor = true },
        new JobStage { Id = 8, TrackTypeId = TrackTypeId, Name = "Shipped", SortOrder = 8, IsShopFloor = true, AccountingDocumentType = AccountingDocumentType.Invoice },
        new JobStage { Id = 9, TrackTypeId = TrackTypeId, Name = "Invoiced/Sent", SortOrder = 9, AccountingDocumentType = AccountingDocumentType.Invoice, IsIrreversible = true },
        new JobStage { Id = 10, TrackTypeId = TrackTypeId, Name = "Payment Received", SortOrder = 10, AccountingDocumentType = AccountingDocumentType.Payment, IsIrreversible = true },
    ];

    public BillingStageGateTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        _boardHub.Setup(h => h.Clients).Returns(clients.Object);

        foreach (var stage in _stages)
            _trackRepo.Setup(r => r.FindStageAsync(stage.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stage);
        _trackRepo.Setup(r => r.GetStagesByTrackTypeAsync(TrackTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(_stages);
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobDetailResponseModel(
                1, "JOB-0001", "Test", null, TrackTypeId, "Production",
                0, "Stage", "#22c55e", null, null, null, null,
                "Normal", null, null, null, null, null, false, 1, 0, null,
                null, null, null, null, null, null, null, null, null, null, 0,
                DateTime.UtcNow, DateTime.UtcNow));
    }

    private static IHttpContextAccessor Caller(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "4") };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        });
        return accessor.Object;
    }

    private MoveJobStageHandler MoveHandler(IHttpContextAccessor caller) => new(
        _jobRepo.Object,
        _trackRepo.Object,
        Mock.Of<IActivityLogRepository>(),
        Mock.Of<ICustomerRepository>(),
        _db,
        Mock.Of<IAccountingProviderFactory>(),
        Mock.Of<ISyncQueueRepository>(),
        Mock.Of<IWorkCenterContext>(),
        _mediator.Object,
        _boardHub.Object,
        caller,
        new SystemClock(),
        Mock.Of<ILogger<MoveJobStageHandler>>());

    private Job JobAt(int stageId)
    {
        var job = new Job
        {
            Id = 1, JobNumber = "JOB-0001", TrackTypeId = TrackTypeId, CurrentStageId = stageId,
            CurrentStage = _stages.Single(s => s.Id == stageId),
        };
        _jobRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _jobRepo.Setup(r => r.FindMultipleAsync(It.IsAny<List<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([job]);
        return job;
    }

    [Theory]
    [InlineData(7, 8)]
    [InlineData(8, 9)]
    [InlineData(9, 10)]
    public async Task ProductionWorker_cannot_move_a_job_into_a_billing_status(int from, int to)
    {
        var job = JobAt(from);

        var act = () => MoveHandler(Caller("ProductionWorker")).Handle(new MoveJobStageCommand(1, to), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain(_stages.Single(s => s.Id == to).Name);
        job.CurrentStageId.Should().Be(from);
    }

    [Fact]
    public async Task ProductionWorker_can_still_move_a_job_between_production_statuses()
    {
        var job = JobAt(6);

        await MoveHandler(Caller("ProductionWorker")).Handle(new MoveJobStageCommand(1, 7), CancellationToken.None);

        job.CurrentStageId.Should().Be(7);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    public async Task Office_roles_can_move_a_job_into_a_billing_status(string role)
    {
        var job = JobAt(7);

        await MoveHandler(Caller(role)).Handle(new MoveJobStageCommand(1, 8), CancellationToken.None);

        job.CurrentStageId.Should().Be(8);
    }

    [Fact]
    public async Task A_system_move_without_a_signed_in_caller_is_not_gated()
    {
        var job = JobAt(7);

        await MoveHandler(Mock.Of<IHttpContextAccessor>()).Handle(new MoveJobStageCommand(1, 8), CancellationToken.None);

        job.CurrentStageId.Should().Be(8);
    }

    [Fact]
    public async Task Bulk_move_into_a_billing_status_is_refused_for_a_production_worker()
    {
        var job = JobAt(7);
        var handler = new BulkMoveJobStageHandler(
            _jobRepo.Object, _trackRepo.Object, Mock.Of<IActivityLogRepository>(), Mock.Of<IWorkCenterContext>(),
            _db, _boardHub.Object, Caller("ProductionWorker"));

        var act = () => handler.Handle(new BulkMoveJobStageCommand([1], 8), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        job.CurrentStageId.Should().Be(7);
    }
}
