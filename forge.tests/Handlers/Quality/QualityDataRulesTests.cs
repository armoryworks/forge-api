using System.Reflection;
using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Concurrency;
using Forge.Api.Controllers;
using Forge.Api.Features.Eco;
using Forge.Api.Features.Quality;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

/// <summary>
/// Quality records keep honest data: calibrations name who did them and cannot be dated ahead,
/// a failed calibration does not push the due date out, new gages start due unless a certificate
/// date is given, CAPA advances say what is missing, NCR costs total what was stored, and ECO,
/// QC and recall inputs are checked before they reach the database.
/// </summary>
public sealed class QualityDataRulesTests : IDisposable
{
    private const int UserId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IHttpContextAccessor _accessor;

    public QualityDataRulesTests()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        _accessor = accessor.Object;
    }

    public void Dispose() => _db.Dispose();

    private async Task<Gage> GageAsync(int intervalDays = 365)
    {
        var gage = new Gage
        {
            GageNumber = $"GAG-{_db.Gages.Count() + 1:D5}",
            Description = "Caliper",
            CalibrationIntervalDays = intervalDays,
            Status = GageStatus.InService,
            NextCalibrationDue = Today.AddDays(30),
        };
        _db.Gages.Add(gage);
        await _db.SaveChangesAsync();
        return gage;
    }

    private Task<CalibrationRecordResponseModel> CalibrateAsync(int gageId, CalibrationResult result, DateTimeOffset at) =>
        new CreateCalibrationRecordHandler(_db, _clock, _accessor).Handle(
            new CreateCalibrationRecordCommand(gageId, new CreateCalibrationRecordRequestModel { CalibratedAt = at, Result = result }),
            CancellationToken.None);

    [Fact]
    public async Task Calibration_records_the_signed_in_user()
    {
        var gage = await GageAsync();

        var record = await CalibrateAsync(gage.Id, CalibrationResult.Pass, Now.AddDays(-1));

        record.CalibratedById.Should().Be(UserId);
        record.NextCalibrationDue.Should().Be(Today.AddDays(-1).AddDays(365));
    }

    [Fact]
    public void Calibration_dated_in_the_future_is_rejected()
    {
        var validator = new CreateCalibrationRecordValidator(_clock);

        var future = validator.Validate(new CreateCalibrationRecordCommand(1,
            new CreateCalibrationRecordRequestModel { CalibratedAt = Now.AddDays(1), Result = CalibrationResult.Pass }));
        var past = validator.Validate(new CreateCalibrationRecordCommand(1,
            new CreateCalibrationRecordRequestModel { CalibratedAt = Now.AddHours(-1), Result = CalibrationResult.Pass }));

        future.IsValid.Should().BeFalse();
        future.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("future"));
        past.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_calibration_takes_the_gage_out_of_service_and_due_today()
    {
        var gage = await GageAsync();

        var record = await CalibrateAsync(gage.Id, CalibrationResult.Fail, Now.AddDays(-2));

        record.NextCalibrationDue.Should().BeNull();
        var saved = await _db.Gages.AsNoTracking().SingleAsync(g => g.Id == gage.Id);
        saved.Status.Should().Be(GageStatus.OutOfService);
        saved.NextCalibrationDue.Should().Be(Today);
    }

    [Fact]
    public async Task New_gage_without_a_certificate_date_is_due_for_calibration_today()
    {
        var created = await new CreateGageHandler(_db, _clock).Handle(
            new CreateGageCommand(new CreateGageRequestModel { Description = "Micrometer", CalibrationIntervalDays = 180 }),
            CancellationToken.None);

        created.Status.Should().Be(GageStatus.DueForCalibration);
        created.NextCalibrationDue.Should().Be(Today);
        created.LastCalibratedAt.Should().BeNull();
    }

    [Fact]
    public async Task New_gage_with_a_certificate_date_is_in_service_until_that_date_plus_the_interval()
    {
        var certified = Now.AddDays(-10);

        var created = await new CreateGageHandler(_db, _clock).Handle(
            new CreateGageCommand(new CreateGageRequestModel
            {
                Description = "Micrometer", CalibrationIntervalDays = 180, LastCalibratedAt = certified,
            }),
            CancellationToken.None);

        created.Status.Should().Be(GageStatus.InService);
        created.LastCalibratedAt.Should().Be(certified);
        created.NextCalibrationDue.Should().Be(Today.AddDays(-10).AddDays(180));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public void Gage_interval_is_bounded_on_create_and_update(int days, bool valid)
    {
        new CreateGageValidator(_clock)
            .Validate(new CreateGageCommand(new CreateGageRequestModel { Description = "Gage", CalibrationIntervalDays = days }))
            .IsValid.Should().Be(valid);
        new UpdateGageValidator()
            .Validate(new UpdateGageCommand(1, new UpdateGageRequestModel { CalibrationIntervalDays = days }))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void New_gage_certificate_date_cannot_be_in_the_future()
    {
        new CreateGageValidator(_clock)
            .Validate(new CreateGageCommand(new CreateGageRequestModel { Description = "Gage", LastCalibratedAt = Now.AddDays(1) }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Capa_advance_from_root_cause_names_the_missing_fields()
    {
        var capa = new CorrectiveAction
        {
            CapaNumber = "CAPA-00001", Title = "Burrs", ProblemDescription = "Burrs on edge",
            OwnerId = UserId, Status = CapaStatus.RootCauseAnalysis, DueDate = Now.AddDays(30),
        };
        _db.CorrectiveActions.Add(capa);
        await _db.SaveChangesAsync();
        var service = new NcrCapaService(_db, _clock);

        var act = () => service.AdvanceCapaPhaseAsync(capa.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Missing: root cause analysis, root cause method.");
        (await service.GetCapaAdvanceBlockersAsync(capa.Id, CancellationToken.None))
            .Should().Equal("root cause analysis", "root cause method");

        capa.RootCauseAnalysis = "Worn deburr tool";
        capa.RootCauseMethod = RootCauseMethod.FiveWhy;
        await _db.SaveChangesAsync();

        (await service.AdvanceCapaPhaseAsync(capa.Id, CancellationToken.None)).Status.Should().Be(CapaStatus.ActionPlanning);
    }

    [Fact]
    public async Task Capa_owner_must_be_an_existing_user()
    {
        var result = await new CreateCapaValidator(_db).ValidateAsync(new CreateCapaCommand(new CreateCapaRequestModel
        {
            Title = "Burrs", ProblemDescription = "Burrs on edge", OwnerId = 999_999, Priority = 3, DueDate = Now,
        }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("existing user"));
    }

    private async Task<NonConformance> NcrAsync(NcrStatus status = NcrStatus.Open)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}"[..10], Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var ncr = new NonConformance
        {
            NcrNumber = $"NCR-{Guid.NewGuid():N}"[..12],
            Type = NcrType.Internal,
            PartId = part.Id,
            DetectedById = 1,
            Description = "Burr on edge",
            AffectedQuantity = 10,
            Status = status,
        };
        _db.NonConformances.Add(ncr);
        await _db.SaveChangesAsync();
        return ncr;
    }

    [Fact]
    public async Task Ncr_total_cost_is_the_sum_of_the_rounded_costs()
    {
        var ncr = await NcrAsync();

        await new UpdateNcrHandler(_db, _clock, _accessor).Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { MaterialCost = 1.005m, LaborCost = 2.005m }),
            CancellationToken.None);

        var saved = await _db.NonConformances.AsNoTracking().SingleAsync(n => n.Id == ncr.Id);
        saved.MaterialCost.Should().Be(1.01m);
        saved.LaborCost.Should().Be(2.01m);
        saved.TotalCostImpact.Should().Be(3.02m);
    }

    [Fact]
    public void Ncr_defective_quantity_cannot_be_negative()
    {
        new CreateNcrValidator()
            .Validate(new CreateNcrCommand(new CreateNcrRequestModel
            {
                PartId = 1, Description = "Burr", AffectedQuantity = 5, DefectiveQuantity = -1,
            }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rework_instructions_are_kept_only_for_a_rework_disposition()
    {
        var ncr = await NcrAsync();

        await new DispositionNcrHandler(_db, _clock, _accessor).Handle(
            new DispositionNcrCommand(ncr.Id, new DispositionNcrRequestModel
            {
                Code = NcrDispositionCode.ReturnToVendor, ReworkInstructions = "Grind the edge",
            }),
            CancellationToken.None);

        (await _db.NonConformances.AsNoTracking().SingleAsync(n => n.Id == ncr.Id)).ReworkInstructions.Should().BeNull();
    }

    [Fact]
    public void Ncr_patch_is_guarded_by_if_match()
    {
        typeof(NcrCapaController).GetMethod(nameof(NcrCapaController.UpdateNcr))!
            .GetCustomAttribute<IfMatchAttribute>()!.EntityType.Should().Be(typeof(NonConformance));
    }

    [Fact]
    public async Task Ncr_detail_carries_its_row_version()
    {
        var ncr = await NcrAsync();
        ncr.Version = 4;
        await _db.SaveChangesAsync();

        var detail = await new GetNcrByIdHandler(_db).Handle(new GetNcrByIdQuery(ncr.Id), CancellationToken.None);

        detail.RowVersion.Should().Be(ncr.Version);
    }

    [Fact]
    public async Task Qc_inspection_needs_something_to_inspect()
    {
        var act = () => new CreateQcInspectionHandler(_db, _accessor).Handle(
            new CreateQcInspectionCommand(new CreateQcInspectionRequestModel(null, null, null, "  ", "Notes only")),
            CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "subject");
    }

    [Fact]
    public async Task Qc_inspection_cannot_pass_with_no_results()
    {
        var created = await new CreateQcInspectionHandler(_db, _accessor).Handle(
            new CreateQcInspectionCommand(new CreateQcInspectionRequestModel(null, null, null, "LOT-9", null)),
            CancellationToken.None);

        var act = () => new UpdateQcInspectionHandler(_db, Mock.Of<MediatR.IMediator>(), _accessor, _clock).Handle(
            new UpdateQcInspectionCommand(created.Id, new UpdateQcInspectionRequestModel("Passed", null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one check*");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("Supplier lot cleared", true)]
    public void Recall_resolution_needs_notes(string? notes, bool valid)
    {
        new ResolveRecallValidator()
            .Validate(new ResolveRecallCommand(1, new ResolveRecallRequestModel(notes)))
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("Part", "{\"revision\":\"B\"}", true)]
    [InlineData("Part", "not json", false)]
    [InlineData("Widget", null, false)]
    public async Task Eco_affected_item_checks_type_entity_and_json(string entityType, string? newValue, bool valid)
    {
        var part = new Part { PartNumber = "BRKT-1", Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var result = await new AddEcoAffectedItemValidator(_db).ValidateAsync(new AddEcoAffectedItemCommand(1,
            new CreateEcoAffectedItemRequestModel
            {
                EntityType = entityType, EntityId = part.Id, ChangeDescription = "Rev bump", NewValue = newValue,
            }));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Eco_affected_item_must_point_at_an_existing_entity()
    {
        var result = await new AddEcoAffectedItemValidator(_db).ValidateAsync(new AddEcoAffectedItemCommand(1,
            new CreateEcoAffectedItemRequestModel { EntityType = "Part", EntityId = 424242, ChangeDescription = "Rev bump" }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("does not exist"));
    }
}
