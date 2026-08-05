using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs.Bulk;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// Parity coverage for the bulk stage move: the bulk path must enforce the
/// same guards as the single-card MoveJobStageHandler — track type,
/// irreversible backward, mandatory-skip, and the F-JQ1 NCR/QC final-stage
/// gate — with per-job partial-success reporting.
/// </summary>
public class BulkMoveJobStageHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IActivityLogRepository> _actRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly BulkMoveJobStageHandler _handler;

    public BulkMoveJobStageHandlerTests()
    {
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _handler = new BulkMoveJobStageHandler(
            _jobRepo.Object,
            _trackRepo.Object,
            _actRepo.Object,
            Mock.Of<IWorkCenterContext>(),
            _db,
            boardHub.Object);
    }

    /// <summary>Production-track tail: QC(7) → Shipped(8, mandatory) → Invoiced/Sent(9, mandatory, irreversible) → Payment Received(10, irreversible).</summary>
    private static List<JobStage> ProductionTailStages(int trackTypeId) =>
    [
        new JobStage { Id = 7, TrackTypeId = trackTypeId, Name = "QC/Review", SortOrder = 7 },
        new JobStage { Id = 8, TrackTypeId = trackTypeId, Name = "Shipped", SortOrder = 8, IsMandatory = true },
        new JobStage { Id = 9, TrackTypeId = trackTypeId, Name = "Invoiced/Sent", SortOrder = 9, IsMandatory = true, IsIrreversible = true },
        new JobStage { Id = 10, TrackTypeId = trackTypeId, Name = "Payment Received", SortOrder = 10, IsIrreversible = true },
    ];

    private static Job JobAt(int id, List<JobStage> stages, int stageId, int trackTypeId = 1) => new()
    {
        Id = id,
        JobNumber = $"JOB-{id:D4}",
        TrackTypeId = trackTypeId,
        CurrentStageId = stageId,
        CurrentStage = stages.First(s => s.Id == stageId),
    };

    private void Setup(List<JobStage> stages, int targetStageId, params Job[] jobs)
    {
        var target = stages.First(s => s.Id == targetStageId);
        _trackRepo.Setup(r => r.FindStageAsync(targetStageId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        _trackRepo.Setup(r => r.GetStagesByTrackTypeAsync(target.TrackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stages);
        _jobRepo.Setup(r => r.FindMultipleAsync(It.IsAny<List<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs.ToList());
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(targetStageId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
    }

    [Fact]
    public async Task Handle_JobOnDifferentTrackType_ReportsPerJobError()
    {
        var stages = ProductionTailStages(1);
        var otherTrackStage = new JobStage { Id = 20, TrackTypeId = 2, Name = "Concept", SortOrder = 1 };
        var job = new Job { Id = 1, JobNumber = "JOB-0001", TrackTypeId = 2, CurrentStageId = 20, CurrentStage = otherTrackStage };
        Setup(stages, 8, job);

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1], 8), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.Errors.Single().Message.Should().Contain("different track type");
        job.CurrentStageId.Should().Be(20);
    }

    [Fact]
    public async Task Handle_BackwardMoveOutOfIrreversibleStage_ReportsPerJobError()
    {
        var stages = ProductionTailStages(1);
        var job = JobAt(1, stages, stageId: 9); // Invoiced/Sent (irreversible)
        Setup(stages, 7, job);                  // back to QC/Review

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1], 7), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.Errors.Single().Message.Should().Contain("irreversible").And.Contain("'Invoiced/Sent'");
        job.CurrentStageId.Should().Be(9);
    }

    [Fact]
    public async Task Handle_ForwardMoveSkippingMandatoryStages_ReportsPerJobError()
    {
        var stages = ProductionTailStages(1);
        var job = JobAt(1, stages, stageId: 7); // QC/Review
        Setup(stages, 10, job);                 // straight to Payment Received

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1], 10), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.Errors.Single().Message.Should()
            .Contain("'Shipped'").And.Contain("'Invoiced/Sent'").And.Contain("mandatory");
        job.CurrentStageId.Should().Be(7);
    }

    [Fact]
    public async Task Handle_MoveToFinalStageWithOpenNcr_ReportsPerJobError()
    {
        var stages = ProductionTailStages(1);
        var job = JobAt(1, stages, stageId: 9); // Invoiced/Sent → Payment Received (final)
        Setup(stages, 10, job);

        _db.NonConformances.Add(new NonConformance { JobId = 1, PartId = 1, DetectedById = 1, Status = NcrStatus.Open });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1], 10), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.Errors.Single().Message.Should().Contain("non-conformance");
        job.CurrentStageId.Should().Be(9);
    }

    [Fact]
    public async Task Handle_MoveToFinalStageWithFailedQcInspection_ReportsPerJobError()
    {
        var stages = ProductionTailStages(1);
        var job = JobAt(1, stages, stageId: 9);
        Setup(stages, 10, job);

        _db.QcInspections.Add(new QcInspection { JobId = 1, Status = "Failed" });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1], 10), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.Errors.Single().Message.Should().Contain("failed QC");
        job.CurrentStageId.Should().Be(9);
    }

    [Fact]
    public async Task Handle_MixedBatch_MovesValidJobsAndReportsInvalidOnes()
    {
        var stages = ProductionTailStages(1);
        var validJob = JobAt(1, stages, stageId: 7);   // QC → Shipped: adjacent, allowed
        var invalidJob = JobAt(2, stages, stageId: 9); // Invoiced/Sent → Shipped: irreversible backward
        Setup(stages, 8, validJob, invalidJob);

        var result = await _handler.Handle(new BulkMoveJobStageCommand([1, 2], 8), CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(1);
        result.Errors.Single().JobId.Should().Be(2);
        validJob.CurrentStageId.Should().Be(8);
        invalidJob.CurrentStageId.Should().Be(9);
        _actRepo.Verify(r => r.AddAsync(
            It.Is<JobActivityLog>(l => l.JobId == 1 && l.Action == ActivityAction.StageMoved),
            It.IsAny<CancellationToken>()), Times.Once);
        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingJobIds_ReportedAsNotFound()
    {
        var stages = ProductionTailStages(1);
        Setup(stages, 8);

        var result = await _handler.Handle(new BulkMoveJobStageCommand([999], 8), CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.Errors.Single().Message.Should().Contain("999");
    }
}
