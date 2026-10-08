using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class AdvancePastHiddenStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly List<int> _moves = [];
    private int _currentStageId;
    private int _trackTypeId;

    private JobDetailResponseModel Detail() => new(
        1, "JOB-1", "Test", null, _trackTypeId, "Production",
        _currentStageId, "Current", "#94a3b8", null, null, null, null,
        "Normal", null, null, null, null, null, false, 1, 0, null,
        null, null, null, null, null, null, null, null, null, null, 0,
        Now, Now);

    private async Task<List<JobStage>> SeedAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsActive = true };
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();
        _trackTypeId = track.Id;
        var stages = new List<JobStage>
        {
            new() { TrackTypeId = track.Id, Name = "In Production", Code = "in_production", SortOrder = 1, IsShopFloor = true },
            new() { TrackTypeId = track.Id, Name = "Deburr", Code = "deburr", SortOrder = 2, IsShopFloor = true, IsActive = false },
            new() { TrackTypeId = track.Id, Name = "QC/Review", Code = "qc", SortOrder = 3, IsShopFloor = true },
        };
        _db.JobStages.AddRange(stages);
        await _db.SaveChangesAsync();
        _currentStageId = stages[0].Id;

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var statusHandler = new GetJobStatusHandler(
            _db, _mediator.Object, clock.Object, new Mock<IAccountingProviderFactory>().Object);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Detail());
        _mediator.Setup(m => m.Send(It.IsAny<GetJobActivityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<JobStatusResponseModel> q, CancellationToken ct) => statusHandler.Handle((GetJobStatusQuery)q, ct));
        _mediator.Setup(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<JobDetailResponseModel> c, CancellationToken _) =>
            {
                var move = (MoveJobStageCommand)c;
                _moves.Add(move.StageId);
                _currentStageId = move.StageId;
            })
            .ReturnsAsync(() => Detail());

        return stages;
    }

    [Fact]
    public async Task A_scan_advance_moves_past_a_hidden_status_to_the_next_visible_one()
    {
        var stages = await SeedAsync();

        var result = await new AdvanceJobHandler(_mediator.Object, new Mock<IScanCollapseService>().Object)
            .Handle(new AdvanceJobCommand(1, "device-1", null), CancellationToken.None);

        _moves.Should().Equal(stages[2].Id);
        result.PreviousStageId.Should().Be(stages[0].Id);
    }

    [Fact]
    public async Task A_kiosk_advance_moves_past_a_hidden_status_to_the_next_visible_one()
    {
        var stages = await SeedAsync();

        await new AdvanceShopFloorJobHandler(_mediator.Object)
            .Handle(new AdvanceShopFloorJobCommand(1), CancellationToken.None);

        _moves.Should().Equal(stages[2].Id);
    }

    [Fact]
    public async Task Completing_on_the_floor_moves_past_a_hidden_status_to_the_next_visible_one()
    {
        var stages = await SeedAsync();

        var result = await new CompleteJobHandler(_mediator.Object)
            .Handle(new CompleteJobCommand(1), CancellationToken.None);

        _moves.Should().Equal(stages[2].Id);
        result.StageName.Should().Be("QC/Review");
    }
}
