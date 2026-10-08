using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class CreateJobInitialStageTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateJobHandler _handler;

    private Job? _created;
    private TrackType _production = null!;
    private TrackType _other = null!;

    public CreateJobInitialStageTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);

        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-0700");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => _created = job)
            .Returns(Task.CompletedTask);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobDetailResponseModel(
                1, "JOB-0700", "Test", null, 1, "Production",
                1, "Quote", "#94a3b8", null, null, null, null,
                "Normal", null, null, null, null, null, false, 1, 0, null,
                null, null, null, null, null, null, null, null, null, null, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        _handler = new CreateJobHandler(
            _jobRepo.Object, new TrackTypeRepository(_db), _mediator.Object, boardHub.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            new SalesOrderAcceptanceGate(_db, StubCapabilitySnapshotProvider.Off),
            Mock.Of<ICloudFolderAutoCreator>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            StubCapabilitySnapshotProvider.Off);
    }

    private async Task SeedTracksAsync()
    {
        _production = new TrackType { Name = "Production", Code = "production", SortOrder = 1 };
        _production.Stages.Add(new JobStage { Name = "Quote Requested", Code = "quote_requested", SortOrder = 1 });
        _production.Stages.Add(new JobStage { Name = "Quoted", Code = "quoted", SortOrder = 2 });
        _production.Stages.Add(new JobStage { Name = "Order Confirmed", Code = "order_confirmed", SortOrder = 3 });
        _production.Stages.Add(new JobStage { Name = "Materials Ordered", Code = "materials_ordered", SortOrder = 4 });
        _production.Stages.Add(new JobStage { Name = "Shipped", Code = "shipped", SortOrder = 5, IsMandatory = true });
        _production.Stages.Add(new JobStage { Name = "Invoiced/Sent", Code = "invoiced_sent", SortOrder = 6, IsMandatory = true });
        _production.Stages.Add(new JobStage { Name = "Payment Received", Code = "payment_received", SortOrder = 7 });
        _other = new TrackType { Name = "Maintenance", Code = "maintenance", SortOrder = 2 };
        _other.Stages.Add(new JobStage { Name = "Requested", Code = "requested", SortOrder = 1 });
        _other.Stages.Add(new JobStage { Name = "In Progress", Code = "in_progress", SortOrder = 2 });
        _other.Stages.Add(new JobStage { Name = "Complete", Code = "complete", SortOrder = 3 });
        _db.TrackTypes.AddRange(_production, _other);
        await _db.SaveChangesAsync();
    }

    private int StageId(TrackType track, string code) => track.Stages.Single(s => s.Code == code).Id;

    private async Task<int> SeedConfirmedLineAsync()
    {
        var so = new SalesOrder { OrderNumber = "SO-700", CustomerId = 42, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            Description = "Brackets",
            Quantity = 10m,
            UnitPrice = 2m,
            LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return line.Id;
    }

    private CreateJobCommand Command(int? initialStageId = null, int? salesOrderLineId = null) =>
        new("Bracket run", null, _production.Id, null, null, null, null,
            SalesOrderLineId: salesOrderLineId, InitialStageId: initialStageId);

    [Fact]
    public async Task Without_a_choice_the_job_starts_in_the_first_visible_status()
    {
        await SeedTracksAsync();

        await _handler.Handle(Command(), CancellationToken.None);

        _created!.CurrentStageId.Should().Be(StageId(_production, "quote_requested"));
    }

    [Fact]
    public async Task The_chosen_initial_status_is_honoured_and_the_board_position_follows_it()
    {
        await SeedTracksAsync();
        var materials = StageId(_production, "materials_ordered");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(materials, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        await _handler.Handle(Command(materials), CancellationToken.None);

        _created!.CurrentStageId.Should().Be(materials);
        _created.BoardPosition.Should().Be(5);
    }

    [Fact]
    public async Task A_status_from_another_order_type_is_rejected_on_initialStageId()
    {
        await SeedTracksAsync();

        var act = () => _handler.Handle(Command(StageId(_other, "requested")), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateJobCommand.InitialStageId));
        _created.Should().BeNull();
    }

    [Theory]
    [InlineData("shipped")]
    [InlineData("invoiced_sent")]
    [InlineData("payment_received")]
    public async Task A_status_at_or_past_the_first_required_status_is_rejected(string code)
    {
        await SeedTracksAsync();

        var act = () => _handler.Handle(Command(StageId(_production, code)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateJobCommand.InitialStageId));
        _created.Should().BeNull();
    }

    [Fact]
    public async Task The_final_status_is_rejected_on_an_order_type_without_required_statuses()
    {
        await SeedTracksAsync();
        var command = Command(StageId(_other, "complete")) with { TrackTypeId = _other.Id };

        var act = () => _handler.Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateJobCommand.InitialStageId));

        await _handler.Handle(Command(StageId(_other, "in_progress")) with { TrackTypeId = _other.Id }, CancellationToken.None);
        _created!.CurrentStageId.Should().Be(StageId(_other, "in_progress"));
    }

    [Fact]
    public async Task A_hidden_status_is_rejected()
    {
        await SeedTracksAsync();
        _production.Stages.Single(s => s.Code == "quoted").IsActive = false;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Command(StageId(_production, "quoted")), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_job_for_a_sales_order_line_defaults_to_order_confirmed()
    {
        await SeedTracksAsync();
        var lineId = await SeedConfirmedLineAsync();

        await _handler.Handle(Command(salesOrderLineId: lineId), CancellationToken.None);

        _created!.CurrentStageId.Should().Be(StageId(_production, "order_confirmed"));
    }

    [Fact]
    public async Task A_sales_order_line_job_falls_back_to_the_first_visible_status_without_order_confirmed()
    {
        await SeedTracksAsync();
        _production.Stages.Single(s => s.Code == "order_confirmed").IsActive = false;
        await _db.SaveChangesAsync();
        var lineId = await SeedConfirmedLineAsync();

        await _handler.Handle(Command(salesOrderLineId: lineId), CancellationToken.None);

        _created!.CurrentStageId.Should().Be(StageId(_production, "quote_requested"));
    }

    [Fact]
    public async Task With_the_quote_statuses_hidden_a_new_job_lands_in_order_confirmed()
    {
        await SeedTracksAsync();
        foreach (var stage in _production.Stages.Where(s => s.Code is "quote_requested" or "quoted"))
            stage.IsActive = false;
        await _db.SaveChangesAsync();

        await _handler.Handle(Command(), CancellationToken.None);

        _created!.CurrentStageId.Should().Be(StageId(_production, "order_confirmed"));
    }
}
