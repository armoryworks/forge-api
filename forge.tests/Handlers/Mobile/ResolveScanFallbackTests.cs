using FluentAssertions;
using Moq;

using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

[Collection(PostgresCollection.Name)]
public sealed class ResolveScanFallbackTests(PostgresFixture fixture)
{
    private readonly Mock<IBarcodeService> _barcodes = new();

    private static int UniqueNumber() => Random.Shared.Next(1_000_000, 9_999_999);

    private async Task<ScanResolveResponseModel> ResolveAsync(string code)
    {
        await using var db = fixture.CreateContext();
        var handler = new ResolveScanHandler(db, _barcodes.Object);
        return await handler.Handle(new ResolveScanQuery(code), CancellationToken.None);
    }

    private async Task<List<int>> SeedJobsAsync(params string[] jobNumbers)
    {
        await using var db = fixture.CreateContext();
        var track = new TrackType { Name = "Scan Track", Code = $"scan-{Guid.NewGuid():N}"[..24], IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();

        var jobs = jobNumbers
            .Select(n => new Job { JobNumber = n, Title = $"Scan {n}", TrackTypeId = track.Id, CurrentStageId = stage.Id })
            .ToList();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();
        return jobs.Select(j => j.Id).ToList();
    }

    [Fact]
    public async Task Job_lower_case_unpadded_input_matches_padded_job_number()
    {
        var n = UniqueNumber();
        var ids = await SeedJobsAsync($"JOB-0{n}");

        var result = await ResolveAsync($"job-{n}");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task Job_padded_input_matches_unpadded_job_number()
    {
        var n = UniqueNumber();
        var ids = await SeedJobsAsync($"JOB-{n}");

        var result = await ResolveAsync($"JOB-0{n}");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task Job_prefix_is_optional_on_the_stored_number()
    {
        var n = UniqueNumber();
        var ids = await SeedJobsAsync($"{n}");

        var result = await ResolveAsync($"Job-{n}");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task Job_exact_case_insensitive_match_wins_over_padding_variant()
    {
        var n = UniqueNumber();
        var ids = await SeedJobsAsync($"JOB-{n}", $"JOB-0{n}");

        var result = await ResolveAsync($"job-0{n}");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[1]);
    }

    [Fact]
    public async Task Job_ambiguous_padding_match_resolves_unknown()
    {
        var n = UniqueNumber();
        await SeedJobsAsync($"JOB-0{n}", $"JOB-00{n}");

        var result = await ResolveAsync($"job-{n}");

        result.Kind.Should().Be("unknown");
        result.Id.Should().BeNull();
    }

    [Fact]
    public async Task Job_like_wildcards_in_input_are_literal()
    {
        var n = UniqueNumber();
        await SeedJobsAsync($"JOB-{n}");

        var result = await ResolveAsync($"JOB-%{n}");

        result.Kind.Should().Be("unknown");
    }

    [Fact]
    public async Task Job_regex_characters_in_input_are_literal()
    {
        var n = UniqueNumber();
        await SeedJobsAsync($"JOB-X{n}");

        var result = await ResolveAsync($"JOB-.{n}");

        result.Kind.Should().Be("unknown");
    }

    [Fact]
    public async Task Job_suffix_match_does_not_cross_into_a_longer_number()
    {
        var stem = $"S{UniqueNumber()}-";
        var ids = await SeedJobsAsync($"{stem}7", $"{stem}17");

        var result = await ResolveAsync($"job-{stem.ToLowerInvariant()}07");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task Barcode_miss_retries_the_upper_cased_value()
    {
        var n = UniqueNumber();
        var ids = await SeedJobsAsync($"J-{n}");
        _barcodes.Setup(b => b.FindByValueAsync($"JOB-J-{n}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Barcode { Value = $"JOB-J-{n}", EntityType = BarcodeEntityType.Job, JobId = ids[0] });

        var result = await ResolveAsync($"job-j-{n}");

        result.Kind.Should().Be("job");
        result.Id.Should().Be(ids[0]);
        result.Label.Should().Be($"J-{n}");
    }

    [Fact]
    public async Task Part_number_matches_case_insensitively()
    {
        var number = $"Ab-{Guid.NewGuid():N}"[..14];
        int partId;
        await using (var db = fixture.CreateContext())
        {
            var part = new Part { PartNumber = number, Name = "Scan part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            partId = part.Id;
        }

        var result = await ResolveAsync($"prt-{number.ToLowerInvariant()}");

        result.Kind.Should().Be("part");
        result.Id.Should().Be(partId);
    }

    [Fact]
    public async Task Lot_number_matches_case_insensitively()
    {
        var number = $"LOT-Xy{UniqueNumber()}";
        int lotId;
        await using (var db = fixture.CreateContext())
        {
            var part = new Part { PartNumber = $"LOTP-{Guid.NewGuid():N}"[..16], Name = "Lot part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            var lot = new LotRecord { LotNumber = number, PartId = part.Id, Quantity = 1 };
            db.LotRecords.Add(lot);
            await db.SaveChangesAsync();
            lotId = lot.Id;
        }

        var result = await ResolveAsync(number.ToLowerInvariant());

        result.Kind.Should().Be("lot");
        result.Id.Should().Be(lotId);
    }

    [Fact]
    public async Task Employee_badge_matches_case_insensitively()
    {
        var badge = $"EMP-Qa{UniqueNumber()}";
        int userId;
        await using (var db = fixture.CreateContext())
        {
            var user = new ApplicationUser
            {
                UserName = $"scan-{Guid.NewGuid():N}",
                Email = $"scan-{Guid.NewGuid():N}@example.test",
                FirstName = "Ada",
                LastName = "Scanner",
                EmployeeBarcode = badge,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var result = await ResolveAsync(badge.ToLowerInvariant());

        result.Kind.Should().Be("badge");
        result.Id.Should().Be(userId);
        result.Label.Should().Be("Scanner, Ada");
    }
}
