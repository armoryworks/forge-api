using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class CompleteJobHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<IMediator> _mediator = new();
    private readonly CompleteJobHandler _handler;

    public CompleteJobHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CompleteJobHandler(_mediator.Object);
    }

    private async Task<(JobStage InProduction, JobStage Qc, JobStage Invoiced, JobStage Paid)> SeedProductionTrackAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsActive = true };
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();

        var inProduction = new JobStage { TrackTypeId = track.Id, Name = "In Production", Code = "in_production", SortOrder = 6, IsShopFloor = true };
        var qc = new JobStage { TrackTypeId = track.Id, Name = "QC/Review", Code = "qc_review", SortOrder = 7, IsShopFloor = true };
        var invoiced = new JobStage { TrackTypeId = track.Id, Name = "Invoiced/Sent", Code = "invoiced_sent", SortOrder = 9, IsShopFloor = false };
        var paid = new JobStage { TrackTypeId = track.Id, Name = "Payment Received", Code = "payment_received", SortOrder = 11, IsIrreversible = true };
        _db.JobStages.AddRange(inProduction, qc, invoiced, paid);
        await _db.SaveChangesAsync();
        return (inProduction, qc, invoiced, paid);
    }

    private void GivenStatus(int jobId, JobStage current, JobStage? next) =>
        _mediator
            .Setup(m => m.Send(It.Is<GetJobStatusQuery>(q => q.JobId == jobId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobStatusResponseModel(
                jobId, "JOB-0001", "Bracket", null,
                current.Id, current.Name, current.Color,
                null, false,
                next?.Id, next?.Name, null, null,
                0, new List<ActivityResponseModel>())
            {
                NextStageIsShopFloor = next?.IsShopFloor ?? false,
            });

    [Fact]
    public async Task Handle_ShopFloorNextStage_MovesOneStatusThroughMoveJobStage()
    {
        var (inProduction, qc, _, paid) = await SeedProductionTrackAsync();
        GivenStatus(42, inProduction, qc);

        var result = await _handler.Handle(new CompleteJobCommand(42), CancellationToken.None);

        result.StageName.Should().Be("QC/Review");
        _mediator.Verify(m => m.Send(
            It.Is<MoveJobStageCommand>(c => c.JobId == 42 && c.StageId == qc.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(
            It.Is<MoveJobStageCommand>(c => c.StageId == paid.Id),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OfficeNextStage_RefusesAndDoesNotMove()
    {
        var (_, qc, invoiced, _) = await SeedProductionTrackAsync();
        GivenStatus(42, qc, invoiced);

        var act = () => _handler.Handle(new CompleteJobCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The next status, Invoiced/Sent, is an office status. Move it from the board.");
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoNextStage_RefusesAndDoesNotMove()
    {
        var (_, _, _, paid) = await SeedProductionTrackAsync();
        GivenStatus(42, paid, null);

        var act = () => _handler.Handle(new CompleteJobCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonExistentJob_PropagatesKeyNotFound()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Job 99999 not found"));

        var act = () => _handler.Handle(new CompleteJobCommand(99999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*99999*");
    }
}
