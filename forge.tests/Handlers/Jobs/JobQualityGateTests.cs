using FluentAssertions;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class JobQualityGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private NonConformance AddNcr(int? jobId, NcrStatus status, string number, int? inspectionId = null)
    {
        var ncr = new NonConformance
        {
            NcrNumber = number,
            JobId = jobId,
            PartId = 1,
            DetectedById = 1,
            Status = status,
            QcInspectionId = inspectionId,
        };
        _db.NonConformances.Add(ncr);
        return ncr;
    }

    private QcInspection AddInspection(int jobId, string status, DateTimeOffset completedAt, int? templateId = 5)
    {
        var inspection = new QcInspection
        {
            JobId = jobId,
            TemplateId = templateId,
            Status = status,
            CompletedAt = completedAt,
        };
        _db.QcInspections.Add(inspection);
        return inspection;
    }

    private Task<Dictionary<int, string>> GateAsync(params int[] jobIds) =>
        JobQualityGate.FindBlockersAsync(_db, jobIds, CancellationToken.None);

    [Theory]
    [InlineData(NcrStatus.Open)]
    [InlineData(NcrStatus.UnderReview)]
    [InlineData(NcrStatus.Contained)]
    public async Task UnresolvedNcr_BlocksAndNamesNumber(NcrStatus status)
    {
        AddNcr(1, status, "NCR-0001");
        await _db.SaveChangesAsync();

        var blockers = await GateAsync(1);

        blockers.Should().ContainKey(1);
        blockers[1].Should().Contain("NCR-0001");
    }

    [Theory]
    [InlineData(NcrStatus.Dispositioned)]
    [InlineData(NcrStatus.Closed)]
    public async Task ResolvedNcr_DoesNotBlock(NcrStatus status)
    {
        AddNcr(1, status, "NCR-0001");
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedInspection_BlocksAndNamesId()
    {
        var failed = AddInspection(1, "Failed", T0);
        await _db.SaveChangesAsync();

        var blockers = await GateAsync(1);

        blockers[1].Should().Contain($"#{failed.Id}");
    }

    [Fact]
    public async Task LaterPassedReinspection_SameTemplate_Releases()
    {
        var failed = AddInspection(1, "Failed", T0);
        AddInspection(1, "Passed", T0.AddHours(2));
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().BeEmpty();
        _db.QcInspections.Find(failed.Id)!.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task LaterPassedReinspection_BothWithoutTemplate_Releases()
    {
        AddInspection(1, "Failed", T0, templateId: null);
        AddInspection(1, "Passed", T0.AddHours(2), templateId: null);
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task PassedInspection_DifferentTemplate_DoesNotRelease()
    {
        AddInspection(1, "Failed", T0, templateId: 5);
        AddInspection(1, "Passed", T0.AddHours(2), templateId: 6);
        AddInspection(1, "Passed", T0.AddHours(3), templateId: null);
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().ContainKey(1);
    }

    [Fact]
    public async Task PassedInspection_BeforeTheFailure_DoesNotRelease()
    {
        AddInspection(1, "Passed", T0);
        AddInspection(1, "Failed", T0.AddHours(1));
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().ContainKey(1);
    }

    [Fact]
    public async Task PassedInspection_OnAnotherJob_DoesNotRelease()
    {
        AddInspection(1, "Failed", T0);
        AddInspection(2, "Passed", T0.AddHours(1));
        await _db.SaveChangesAsync();

        (await GateAsync(1, 2)).Keys.Should().BeEquivalentTo([1]);
    }

    [Theory]
    [InlineData(NcrStatus.Dispositioned)]
    [InlineData(NcrStatus.Closed)]
    public async Task FailedInspection_WithResolvedLinkedNcr_Releases(NcrStatus status)
    {
        var failed = AddInspection(1, "Failed", T0);
        await _db.SaveChangesAsync();
        AddNcr(1, status, "NCR-0002", failed.Id);
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedInspection_WithUnresolvedLinkedNcr_BlocksOnBoth()
    {
        var failed = AddInspection(1, "Failed", T0);
        await _db.SaveChangesAsync();
        AddNcr(1, NcrStatus.Contained, "NCR-0003", failed.Id);
        await _db.SaveChangesAsync();

        var blockers = await GateAsync(1);

        blockers[1].Should().Contain("NCR-0003").And.Contain($"#{failed.Id}");
    }

    [Fact]
    public async Task InProgressInspection_DoesNotBlock()
    {
        _db.QcInspections.Add(new QcInspection { JobId = 1, Status = "InProgress" });
        await _db.SaveChangesAsync();

        (await GateAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public void BlockedMessage_IncludesBlockersAndRemedy()
    {
        var message = JobQualityGate.BlockedMessage("unresolved NCR NCR-0001");

        message.Should().Contain("non-conformance").And.Contain("NCR-0001").And.Contain("re-inspect");
    }
}
