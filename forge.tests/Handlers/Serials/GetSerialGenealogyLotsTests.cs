using FluentAssertions;

using Forge.Api.Features.Serials;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Serials;

public class GetSerialGenealogyLotsTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task Genealogy_reaches_the_serials_own_lot_and_the_lots_issued_to_its_job()
    {
        _db.TrackTypes.Add(new TrackType { Id = 5, Name = "Prod", Code = "prod", IsActive = true });
        _db.JobStages.Add(new JobStage { Id = 51, TrackTypeId = 5, Name = "S1", Code = "s1", SortOrder = 1 });
        var finished = new Part { Id = 1, PartNumber = "FG-400", Description = "Assembly" };
        var bar = new Part { Id = 2, PartNumber = "RM-6061-B12", Description = "Bar" };
        var resin = new Part { Id = 3, PartNumber = "RS-PCABS-BLK", Description = "Resin" };
        _db.Parts.AddRange(finished, bar, resin);
        _db.Jobs.Add(new Job { Id = 40, JobNumber = "J-400", Title = "Build FG-400", TrackTypeId = 5, CurrentStageId = 51, PartId = finished.Id });
        _db.LotRecords.AddRange(
            new LotRecord { Id = 70, LotNumber = "LOT-FG-1", PartId = finished.Id, Quantity = 2m },
            new LotRecord { Id = 71, LotNumber = "H-55821", PartId = bar.Id, Quantity = 30m });
        var issuedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        _db.MaterialIssues.AddRange(
            new MaterialIssue { JobId = 40, PartId = bar.Id, Quantity = 2m, LotNumber = "H-55821", IssuedAt = issuedAt },
            new MaterialIssue { JobId = 40, PartId = bar.Id, Quantity = 1m, LotNumber = "H-55821", IssuedAt = issuedAt },
            new MaterialIssue { JobId = 40, PartId = resin.Id, Quantity = 4m, LotNumber = "R-26-117", IssuedAt = issuedAt },
            new MaterialIssue { JobId = 40, PartId = resin.Id, Quantity = 4m, LotNumber = null, IssuedAt = issuedAt },
            new MaterialIssue { JobId = 40, PartId = resin.Id, Quantity = 1m, LotNumber = "R-26-117", IssuedAt = issuedAt, IssueType = MaterialIssueType.Return });
        _db.SerialNumbers.Add(new SerialNumber { Id = 9, PartId = finished.Id, SerialValue = "SN-0002", JobId = 40, LotRecordId = 70 });
        await _db.SaveChangesAsync();

        var tree = await new GetSerialGenealogyHandler(_db).Handle(new GetSerialGenealogyQuery("SN-0002"), CancellationToken.None);

        tree.JobId.Should().Be(40);
        tree.JobNumber.Should().Be("J-400");
        tree.Lots.Should().BeEquivalentTo(new[]
        {
            new { LotId = (int?)70, LotNumber = "LOT-FG-1", PartNumber = "FG-400", Source = "Lot", Quantity = (decimal?)null },
            new { LotId = (int?)71, LotNumber = "H-55821", PartNumber = "RM-6061-B12", Source = "MaterialIssue", Quantity = (decimal?)3m },
            new { LotId = (int?)null, LotNumber = "R-26-117", PartNumber = "RS-PCABS-BLK", Source = "MaterialIssue", Quantity = (decimal?)4m },
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Genealogy_reaches_the_lots_issued_to_the_jobs_sub_assembly_jobs()
    {
        _db.TrackTypes.Add(new TrackType { Id = 5, Name = "Prod", Code = "prod", IsActive = true });
        _db.JobStages.Add(new JobStage { Id = 51, TrackTypeId = 5, Name = "S1", Code = "s1", SortOrder = 1 });
        var finished = new Part { Id = 1, PartNumber = "FG-400", Description = "Assembly" };
        var plate = new Part { Id = 2, PartNumber = "SA-220", Description = "Plate" };
        var bar = new Part { Id = 3, PartNumber = "RM-6061-B12", Description = "Bar" };
        _db.Parts.AddRange(finished, plate, bar);
        _db.Jobs.AddRange(
            new Job { Id = 40, JobNumber = "J-400", Title = "Build FG-400", TrackTypeId = 5, CurrentStageId = 51, PartId = finished.Id },
            new Job { Id = 41, JobNumber = "J-401", Title = "Plate", TrackTypeId = 5, CurrentStageId = 51, PartId = plate.Id, ParentJobId = 40 },
            new Job { Id = 42, JobNumber = "J-402", Title = "Plate blank", TrackTypeId = 5, CurrentStageId = 51, PartId = bar.Id, ParentJobId = 41 },
            new Job { Id = 43, JobNumber = "J-403", Title = "Other build", TrackTypeId = 5, CurrentStageId = 51, PartId = plate.Id });
        var issuedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        _db.MaterialIssues.AddRange(
            new MaterialIssue { JobId = 42, PartId = bar.Id, Quantity = 5m, LotNumber = "H-55821", IssuedAt = issuedAt },
            new MaterialIssue { JobId = 43, PartId = bar.Id, Quantity = 9m, LotNumber = "H-99999", IssuedAt = issuedAt });
        _db.SerialNumbers.Add(new SerialNumber { Id = 9, PartId = finished.Id, SerialValue = "SN-0002", JobId = 40 });
        await _db.SaveChangesAsync();

        var tree = await new GetSerialGenealogyHandler(_db).Handle(new GetSerialGenealogyQuery("SN-0002"), CancellationToken.None);

        tree.Lots.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { LotNumber = "H-55821", PartNumber = "RM-6061-B12", Source = "MaterialIssue", Quantity = (decimal?)5m });
    }
}
