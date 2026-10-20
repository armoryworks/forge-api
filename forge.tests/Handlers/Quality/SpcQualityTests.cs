using System.Security.Claims;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Quality;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public class SpcQualityTests : IDisposable
{
    private const int UserId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IHttpContextAccessor _accessor;
    private readonly SpcService _spc;

    public SpcQualityTests()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        _accessor = accessor.Object;
        _spc = new SpcService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Part> PartAsync()
    {
        var part = new Part { PartNumber = "SHAFT-10", Name = "Shaft" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Gage> GageAsync(GageStatus status, DateOnly? nextDue)
    {
        var gage = new Gage
        {
            GageNumber = $"CAL-{_db.Gages.Count() + 1}",
            Description = "Micrometer",
            Status = status,
            NextCalibrationDue = nextDue,
        };
        _db.Gages.Add(gage);
        await _db.SaveChangesAsync();
        return gage;
    }

    private async Task<SpcCharacteristic> CharacteristicAsync(int? gageId = null)
    {
        var part = await PartAsync();
        var characteristic = new SpcCharacteristic
        {
            PartId = part.Id,
            Name = "OD",
            NominalValue = 10m,
            UpperSpecLimit = 10.05m,
            LowerSpecLimit = 9.95m,
            SampleSize = 5,
            GageId = gageId,
        };
        _db.SpcCharacteristics.Add(characteristic);
        await _db.SaveChangesAsync();
        return characteristic;
    }

    private RecordSpcMeasurementsHandler RecordHandler() => new(_db, _spc, _accessor);

    private static RecordSpcMeasurementsCommand Record(int characteristicId, params decimal[][] subgroups)
        => new(new RecordSpcMeasurementRequestModel
        {
            CharacteristicId = characteristicId,
            Subgroups = subgroups.Select(v => new SpcSubgroupEntry { Values = v }).ToList(),
        });

    private static CreateSpcCharacteristicCommand Create(int partId, int? gageId) => new(new CreateSpcCharacteristicRequestModel
    {
        PartId = partId,
        Name = "Bore",
        NominalValue = 5m,
        UpperSpecLimit = 5.1m,
        LowerSpecLimit = 4.9m,
        GageId = gageId,
    });

    private static UpdateSpcCharacteristicCommand Update(SpcCharacteristic c, int? gageId, string name = "OD") => new(c.Id,
        new UpdateSpcCharacteristicRequestModel
        {
            Name = name,
            MeasurementType = c.MeasurementType,
            NominalValue = c.NominalValue,
            UpperSpecLimit = c.UpperSpecLimit,
            LowerSpecLimit = c.LowerSpecLimit,
            DecimalPlaces = c.DecimalPlaces,
            SampleSize = c.SampleSize,
            GageId = gageId,
            IsActive = true,
            NotifyOnOoc = true,
        });

    [Fact]
    public async Task ProcessCapability_UsesWithinSubgroupSigmaForCpAndCpk_AndOverallSigmaForPpAndPpk()
    {
        var characteristic = await CharacteristicAsync();
        await RecordHandler().Handle(Record(characteristic.Id,
            [10.00m, 10.01m, 10.00m, 9.99m, 10.00m],
            [10.02m, 10.03m, 10.02m, 10.01m, 10.02m],
            [9.98m, 9.99m, 9.98m, 9.97m, 9.98m],
            [10.01m, 10.02m, 10.01m, 10.00m, 10.01m]), CancellationToken.None);

        var report = await new GetProcessCapabilityHandler(_db, _spc).Handle(
            new GetProcessCapabilityQuery(characteristic.Id), CancellationToken.None);

        var measurements = await _db.SpcMeasurements.AsNoTracking().ToListAsync();
        var rBar = measurements.Average(m => m.Range);
        var xDoubleBar = measurements.Average(m => m.Mean);
        var withinSigma = rBar / 2.326m;
        var values = measurements.SelectMany(m => JsonSerializer.Deserialize<decimal[]>(m.ValuesJson)!).ToList();
        var mean = values.Average();
        var overallSigma = (decimal)Math.Sqrt((double)values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));

        report.Cp.Should().BeApproximately(0.1m / (6m * withinSigma), 0.0001m);
        report.Cpk.Should().BeApproximately(
            Math.Min(10.05m - xDoubleBar, xDoubleBar - 9.95m) / (3m * withinSigma), 0.0001m);
        report.Pp.Should().BeApproximately(0.1m / (6m * overallSigma), 0.0001m);
        report.Cp.Should().BeGreaterThan(report.Pp);

        var limits = await _spc.CalculateControlLimitsAsync(characteristic.Id, null, null, CancellationToken.None);
        report.Cp.Should().Be(limits.Cp);
        report.Cpk.Should().Be(limits.Cpk);
        report.Pp.Should().Be(limits.Pp);
        report.Ppk.Should().Be(limits.Ppk);
    }

    [Fact]
    public async Task ControlLimits_StillRefuseFewerThanTwoSubgroups_AndHonourTheSubgroupWindow()
    {
        var characteristic = await CharacteristicAsync();
        await RecordHandler().Handle(Record(characteristic.Id,
            [10.00m, 10.01m, 10.00m, 9.99m, 10.00m],
            [10.02m, 10.03m, 10.02m, 10.01m, 10.02m],
            [9.90m, 10.10m, 9.95m, 10.05m, 10.00m]), CancellationToken.None);

        var tooFew = () => _spc.CalculateControlLimitsAsync(characteristic.Id, 3, null, CancellationToken.None);
        await tooFew.Should().ThrowAsync<InvalidOperationException>().WithMessage("At least 2 subgroups*");

        var window = await _spc.CalculateControlLimitsAsync(characteristic.Id, 1, 2, CancellationToken.None);
        window.SampleCount.Should().Be(2);
        window.RangeCenterLine.Should().Be(0.02m);
    }

    [Theory]
    [InlineData(GageStatus.OutOfService)]
    [InlineData(GageStatus.OutForCalibration)]
    [InlineData(GageStatus.Retired)]
    public async Task CreateCharacteristic_RefusesAGageThatIsNotInService(GageStatus status)
    {
        var part = await PartAsync();
        var gage = await GageAsync(status, Today.AddDays(30));

        var act = () => new CreateSpcCharacteristicHandler(_db, _clock).Handle(Create(part.Id, gage.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"Gage {gage.GageNumber} *");
        (await _db.SpcCharacteristics.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreateCharacteristic_RefusesAnOverdueGage_ButAcceptsOneThatIsOnlyDue()
    {
        var part = await PartAsync();
        var overdue = await GageAsync(GageStatus.InService, Today.AddDays(-1));
        var due = await GageAsync(GageStatus.DueForCalibration, Today.AddDays(3));
        var handler = new CreateSpcCharacteristicHandler(_db, _clock);

        var act = () => handler.Handle(Create(part.Id, overdue.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*was due for calibration*");

        var created = await handler.Handle(Create(part.Id, due.Id), CancellationToken.None);
        created.GageId.Should().Be(due.Id);
    }

    [Fact]
    public async Task UpdateCharacteristic_ChecksTheGageOnlyWhenItChanges()
    {
        var gage = await GageAsync(GageStatus.InService, Today.AddDays(30));
        var characteristic = await CharacteristicAsync(gage.Id);
        gage.NextCalibrationDue = Today.AddDays(-5);
        await _db.SaveChangesAsync();
        var handler = new UpdateSpcCharacteristicHandler(_db, _spc, _clock);

        var renamed = await handler.Handle(Update(characteristic, gage.Id, "Outside diameter"), CancellationToken.None);
        renamed.Name.Should().Be("Outside diameter");

        var broken = await GageAsync(GageStatus.OutOfService, Today.AddDays(30));
        var act = () => handler.Handle(Update(characteristic, broken.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*out of service*");
    }

    [Fact]
    public async Task RecordMeasurements_IsRefused_OnceTheGageGoesOutOfService()
    {
        var gage = await GageAsync(GageStatus.InService, null);
        var characteristic = await CharacteristicAsync(gage.Id);
        await RecordHandler().Handle(Record(characteristic.Id, [10m, 10m, 10m, 10m, 10m]), CancellationToken.None);

        gage.Status = GageStatus.OutOfService;
        await _db.SaveChangesAsync();

        var act = () => RecordHandler().Handle(Record(characteristic.Id, [10m, 10m, 10m, 10m, 10m]), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*out of service*");
        (await _db.SpcMeasurements.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateCapaFromOoc_CreatesALinkedCapaOwnedByTheCaller_Once()
    {
        var characteristic = await CharacteristicAsync();
        var measurement = new SpcMeasurement { CharacteristicId = characteristic.Id, SubgroupNumber = 1, MeasuredAt = Now };
        _db.SpcMeasurements.Add(measurement);
        await _db.SaveChangesAsync();
        var ooc = new SpcOocEvent
        {
            CharacteristicId = characteristic.Id,
            MeasurementId = measurement.Id,
            DetectedAt = Now,
            RuleName = "WE1_BeyondLimit",
            Description = "Point #1 is beyond control limits",
            Severity = SpcOocSeverity.OutOfControl,
        };
        _db.SpcOocEvents.Add(ooc);
        await _db.SaveChangesAsync();
        var handler = new CreateCapaFromOocHandler(_db, new NcrCapaService(_db, _clock), _clock, _accessor);

        var result = await handler.Handle(new CreateCapaFromOocCommand(ooc.Id), CancellationToken.None);

        var capa = await _db.CorrectiveActions.AsNoTracking().SingleAsync();
        result.CapaId.Should().Be(capa.Id);
        result.CapaNumber.Should().Be(capa.CapaNumber);
        result.Status.Should().Be(nameof(SpcOocStatus.CapaCreated));
        capa.SourceType.Should().Be(CapaSourceType.SpcOoc);
        capa.SourceEntityType.Should().Be("SpcOocEvent");
        capa.SourceEntityId.Should().Be(ooc.Id);
        capa.OwnerId.Should().Be(UserId);
        capa.Status.Should().Be(CapaStatus.Open);
        capa.ProblemDescription.Should().Be("Point #1 is beyond control limits");
        (await _db.SpcOocEvents.AsNoTracking().SingleAsync()).CapaId.Should().Be(capa.Id);

        var again = await handler.Handle(new CreateCapaFromOocCommand(ooc.Id), CancellationToken.None);
        again.CapaId.Should().Be(capa.Id);
        (await _db.CorrectiveActions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RecalculateLimits_ReflagsStoredSubgroups_WithoutTouchingOocEvents()
    {
        var characteristic = await CharacteristicAsync();
        var means = new[] { 10.00m, 10.01m, 9.99m, 10.00m, 10.02m, 9.98m, 10.00m, 10.30m };
        for (var i = 0; i < means.Length; i++)
        {
            _db.SpcMeasurements.Add(new SpcMeasurement
            {
                CharacteristicId = characteristic.Id,
                SubgroupNumber = i + 1,
                MeasuredAt = Now,
                ValuesJson = JsonSerializer.Serialize(new[] { means[i] - 0.01m, means[i], means[i] + 0.01m, means[i], means[i] }),
                Mean = means[i],
                StdDev = 0.007m,
                Range = 0.02m,
                IsOutOfControl = i == 2,
                OocRuleViolated = i == 2 ? "Stale" : null,
            });
        }
        await _db.SaveChangesAsync();

        await new RecalculateControlLimitsHandler(_db, _spc)
            .Handle(new RecalculateControlLimitsCommand(characteristic.Id, 1, 7), CancellationToken.None);

        var stored = await _db.SpcMeasurements.AsNoTracking().OrderBy(m => m.SubgroupNumber).ToListAsync();
        stored.Last().IsOutOfControl.Should().BeTrue("subgroup 8 sits far beyond the recalculated limits");
        stored.Last().OocRuleViolated.Should().NotBeNullOrEmpty();
        stored[2].IsOutOfControl.Should().BeFalse("a stale flag inside the new limits is cleared");
        stored[2].OocRuleViolated.Should().BeNull();
        (await _db.SpcOocEvents.CountAsync()).Should().Be(0, "recalculating only re-flags the chart");
    }
}
