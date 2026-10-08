using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class GetJobStatusNextStageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<JobStatusResponseModel> StatusAtAsync(int currentStageId, int trackTypeId)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobDetailResponseModel(
                1, "JOB-1", "Test", null, trackTypeId, "Production",
                currentStageId, "Current", "#94a3b8", null, null, null, null,
                "Normal", null, null, null, null, null, false, 1, 0, null,
                null, null, null, null, null, null, null, null, null, null, 0,
                Now, Now));
        mediator.Setup(m => m.Send(It.IsAny<GetJobActivityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);

        return await new GetJobStatusHandler(_db, mediator.Object, clock.Object)
            .Handle(new GetJobStatusQuery(1), CancellationToken.None);
    }

    private async Task<List<JobStage>> SeedTrackAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsActive = true };
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();
        var stages = new List<JobStage>
        {
            new() { TrackTypeId = track.Id, Name = "QC/Review", Code = "qc", SortOrder = 1 },
            new() { TrackTypeId = track.Id, Name = "Shipped", Code = "shipped", SortOrder = 2, AccountingDocumentType = AccountingDocumentType.Invoice },
            new() { TrackTypeId = track.Id, Name = "Invoiced/Sent", Code = "invoiced_sent", SortOrder = 3, AccountingDocumentType = AccountingDocumentType.Invoice, IsIrreversible = true },
        };
        _db.JobStages.AddRange(stages);
        await _db.SaveChangesAsync();
        return stages;
    }

    [Fact]
    public async Task Reports_that_the_next_status_is_irreversible_and_creates_an_invoice()
    {
        var stages = await SeedTrackAsync();

        var status = await StatusAtAsync(stages[1].Id, stages[1].TrackTypeId);

        status.NextStageName.Should().Be("Invoiced/Sent");
        status.NextStageIsIrreversible.Should().BeTrue();
        status.NextStageAccountingDocument.Should().Be(AccountingDocumentType.Invoice);
    }

    [Fact]
    public async Task An_ordinary_next_status_carries_no_flags()
    {
        var stages = await SeedTrackAsync();
        var machining = new JobStage { TrackTypeId = stages[0].TrackTypeId, Name = "Machining", Code = "machining", SortOrder = 0 };
        _db.JobStages.Add(machining);
        await _db.SaveChangesAsync();

        var status = await StatusAtAsync(machining.Id, machining.TrackTypeId);

        status.NextStageName.Should().Be("QC/Review");
        status.NextStageIsIrreversible.Should().BeFalse();
        status.NextStageAccountingDocument.Should().BeNull();
    }
}
