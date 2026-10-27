using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public sealed class JobSpcCharacteristicsTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IMediator _mediator;

    public JobSpcCharacteristicsTests()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetSpcCharacteristicsQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetSpcCharacteristicsQuery q, CancellationToken ct) => new GetSpcCharacteristicsHandler(_db).Handle(q, ct));
        _mediator = mediator.Object;
    }

    public void Dispose() => _db.Dispose();

    private GetJobSpcCharacteristicsHandler Handler() => new(_db, _mediator);

    [Fact]
    public async Task ListsTheActiveCharacteristicsOfTheJobsPart()
    {
        var shaft = new Part { PartNumber = "SHAFT-10", Name = "Shaft" };
        var other = new Part { PartNumber = "COVER-2", Name = "Cover" };
        _db.Parts.AddRange(shaft, other);
        await _db.SaveChangesAsync();
        var turn = new Operation { PartId = shaft.Id, StepNumber = 20, Title = "Turn" };
        _db.Operations.Add(turn);
        await _db.SaveChangesAsync();
        _db.SpcCharacteristics.AddRange(
            new SpcCharacteristic { PartId = shaft.Id, OperationId = turn.Id, Name = "OD", UpperSpecLimit = 10.05m, LowerSpecLimit = 9.95m },
            new SpcCharacteristic { PartId = shaft.Id, Name = "Length", UpperSpecLimit = 50.1m, LowerSpecLimit = 49.9m },
            new SpcCharacteristic { PartId = shaft.Id, Name = "Retired", IsActive = false },
            new SpcCharacteristic { PartId = other.Id, Name = "Flatness" });
        var job = new Job { JobNumber = "J-1", Title = "Shafts", TrackTypeId = 1, CurrentStageId = 1, PartId = shaft.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new GetJobSpcCharacteristicsQuery(job.Id), CancellationToken.None);

        result.Select(c => c.Name).Should().BeEquivalentTo(["OD", "Length"]);
        result.Single(c => c.Name == "OD").OperationId.Should().Be(turn.Id);
    }

    [Fact]
    public async Task AJobWithoutAPart_HasNoCharacteristics()
    {
        var job = new Job { JobNumber = "J-2", Title = "Service", TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        (await Handler().Handle(new GetJobSpcCharacteristicsQuery(job.Id), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownJob_IsNotFound()
    {
        var act = () => Handler().Handle(new GetJobSpcCharacteristicsQuery(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
