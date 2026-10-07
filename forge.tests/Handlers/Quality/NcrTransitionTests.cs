using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;
using FluentValidation;
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

public class NcrTransitionTests
{
    private const int UserId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IHttpContextAccessor _httpContextAccessor;

    public NcrTransitionTests()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        _httpContextAccessor = accessor.Object;
    }

    private async Task<NonConformance> SeedNcrAsync(NcrStatus status, decimal affected = 10, decimal? defective = 2)
    {
        var ncr = new NonConformance
        {
            NcrNumber = $"NCR-{Guid.NewGuid():N}"[..12],
            Type = NcrType.Internal,
            PartId = 1,
            DetectedById = 1,
            Description = "Burr on edge",
            AffectedQuantity = affected,
            DefectiveQuantity = defective,
            Status = status,
        };
        _db.NonConformances.Add(ncr);
        await _db.SaveChangesAsync();
        return ncr;
    }

    private async Task<NonConformance> ReloadAsync(int id) =>
        await _db.NonConformances.AsNoTracking().SingleAsync(n => n.Id == id);

    private Task<List<ActivityLog>> ActivityFor(int id) =>
        _db.ActivityLogs.Where(a => a.EntityType == "NonConformance" && a.EntityId == id).ToListAsync();

    private UpdateNcrHandler UpdateHandler() => new(_db, _clock, _httpContextAccessor);

    [Fact]
    public async Task Update_PayloadWithStatus_DoesNotChangeStatus()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var request = JsonSerializer.Deserialize<UpdateNcrRequestModel>(
            """{"status":"Closed","materialCost":12.5}""", options)!;

        await UpdateHandler().Handle(new UpdateNcrCommand(ncr.Id, request), CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Open);
        saved.MaterialCost.Should().Be(12.5m);
        saved.TotalCostImpact.Should().Be(12.5m);
    }

    [Fact]
    public async Task Update_DescriptionOnDispositionedNcr_Throws()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);

        var act = () => UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { Description = "Rewritten finding" }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*description*");
        (await ReloadAsync(ncr.Id)).Description.Should().Be("Burr on edge");
    }

    [Theory]
    [InlineData(NcrStatus.Dispositioned)]
    [InlineData(NcrStatus.Closed)]
    public async Task Update_QuantityOrTypeOnLockedNcr_Throws(NcrStatus status)
    {
        var ncr = await SeedNcrAsync(status);

        var request = new UpdateNcrRequestModel { AffectedQuantity = 20, Type = NcrType.Supplier };

        var act = () => UpdateHandler().Handle(new UpdateNcrCommand(ncr.Id, request), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*type*affectedQuantity*");
    }

    [Fact]
    public async Task Update_CostsOnDispositionedNcr_Allowed()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);

        await UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel
            {
                MaterialCost = 40,
                LaborCost = 15,
                Description = "Burr on edge",
            }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.TotalCostImpact.Should().Be(55m);
        saved.Status.Should().Be(NcrStatus.Dispositioned);
        var activity = await ActivityFor(ncr.Id);
        activity.Should().ContainSingle(a => a.Action == "updated")
            .Which.Description.Should().Be("Updated 2 fields: materialCost, laborCost");
    }

    [Fact]
    public async Task Update_ContainmentActions_StampsContainment()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open);

        await UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { ContainmentActions = "Quarantined lot 42" }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.ContainmentActions.Should().Be("Quarantined lot 42");
        saved.ContainmentById.Should().Be(UserId);
        saved.ContainmentAt.Should().Be(Now);
    }

    [Fact]
    public async Task Update_DefectiveAboveStoredAffected_ThrowsValidation()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open, affected: 5, defective: 1);

        var act = () => UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { DefectiveQuantity = 6 }),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await ReloadAsync(ncr.Id)).DefectiveQuantity.Should().Be(1);
    }

    [Fact]
    public async Task Update_CostsOnLegacyRowWithDefectiveAboveAffected_Allowed()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned, affected: 2, defective: 5);

        await UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { MaterialCost = 12 }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.MaterialCost.Should().Be(12m);
        saved.DefectiveQuantity.Should().Be(5);
    }

    [Fact]
    public async Task Update_ContainmentActionsAfterContainment_KeepsOriginalStamp()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);
        var containedAt = Now.AddDays(-3);
        ncr.ContainmentActions = "Quarantined lot 42";
        ncr.ContainmentById = 3;
        ncr.ContainmentAt = containedAt;
        await _db.SaveChangesAsync();

        await UpdateHandler().Handle(
            new UpdateNcrCommand(ncr.Id, new UpdateNcrRequestModel { ContainmentActions = "Quarantined lot 42; lot 43 sorted" }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.ContainmentActions.Should().Be("Quarantined lot 42; lot 43 sorted");
        saved.ContainmentById.Should().Be(3);
        saved.ContainmentAt.Should().Be(containedAt);
        (await ActivityFor(ncr.Id)).Should().ContainSingle(a => a.Action == "updated");
    }

    [Fact]
    public void UpdateValidator_RejectsNegativeCostsAndDefectiveAboveAffected()
    {
        var validator = new UpdateNcrValidator();

        var result = validator.Validate(new UpdateNcrCommand(1, new UpdateNcrRequestModel
        {
            AffectedQuantity = 3,
            DefectiveQuantity = 4,
            MaterialCost = -1,
            LaborCost = -2,
        }));

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            "Request.DefectiveQuantity", "Request.MaterialCost", "Request.LaborCost");
    }

    [Theory]
    [InlineData(NcrStatus.Open)]
    [InlineData(NcrStatus.UnderReview)]
    public async Task Contain_FromOpenOrUnderReview_MovesToContainedAndStamps(NcrStatus from)
    {
        var ncr = await SeedNcrAsync(from);

        await new ContainNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new ContainNcrCommand(ncr.Id, new ContainNcrRequestModel { ContainmentActions = "Sorted WIP" }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Contained);
        saved.ContainmentActions.Should().Be("Sorted WIP");
        saved.ContainmentById.Should().Be(UserId);
        saved.ContainmentAt.Should().Be(Now);
        (await ActivityFor(ncr.Id)).Should().ContainSingle(a => a.Action == "contained");
    }

    [Fact]
    public async Task Contain_WithNoContainmentActions_ThrowsValidation()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open);

        var act = () => new ContainNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new ContainNcrCommand(ncr.Id, new ContainNcrRequestModel { ContainmentActions = " " }), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Open);
        saved.ContainmentAt.Should().BeNull();
    }

    [Fact]
    public async Task Contain_UsesStoredContainmentActionsWhenRequestHasNone()
    {
        var ncr = await SeedNcrAsync(NcrStatus.UnderReview);
        ncr.ContainmentActions = "Held at receiving";
        await _db.SaveChangesAsync();

        await new ContainNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new ContainNcrCommand(ncr.Id, new ContainNcrRequestModel()), CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Contained);
        saved.ContainmentActions.Should().Be("Held at receiving");
        saved.ContainmentAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(NcrStatus.Contained)]
    [InlineData(NcrStatus.Dispositioned)]
    [InlineData(NcrStatus.Closed)]
    public async Task Contain_FromOtherStatus_Throws(NcrStatus from)
    {
        var ncr = await SeedNcrAsync(from);

        var act = () => new ContainNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new ContainNcrCommand(ncr.Id, new ContainNcrRequestModel()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ReloadAsync(ncr.Id)).Status.Should().Be(from);
    }

    [Theory]
    [InlineData(NcrStatus.Open)]
    [InlineData(NcrStatus.UnderReview)]
    [InlineData(NcrStatus.Contained)]
    public async Task Disposition_FromActiveStatus_MovesToDispositioned(NcrStatus from)
    {
        var ncr = await SeedNcrAsync(from);

        await new DispositionNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new DispositionNcrCommand(ncr.Id, new DispositionNcrRequestModel { Code = NcrDispositionCode.Scrap }),
            CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Dispositioned);
        saved.DispositionCode.Should().Be(NcrDispositionCode.Scrap);
        saved.DispositionById.Should().Be(UserId);
        saved.DispositionAt.Should().Be(Now);
        (await ActivityFor(ncr.Id)).Should().ContainSingle(a => a.Action == "dispositioned");
    }

    [Fact]
    public async Task Disposition_SecondTime_ThrowsAndKeepsFirstDisposition()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open);
        var handler = new DispositionNcrHandler(_db, _clock, _httpContextAccessor);
        await handler.Handle(
            new DispositionNcrCommand(ncr.Id, new DispositionNcrRequestModel { Code = NcrDispositionCode.Scrap }),
            CancellationToken.None);

        var act = () => handler.Handle(
            new DispositionNcrCommand(ncr.Id, new DispositionNcrRequestModel
            {
                Code = NcrDispositionCode.UseAsIs,
                Notes = "Customer accepted",
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be dispositioned again*");
        (await ReloadAsync(ncr.Id)).DispositionCode.Should().Be(NcrDispositionCode.Scrap);
    }

    [Fact]
    public async Task Disposition_ClosedNcr_Throws()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Closed);

        var act = () => new DispositionNcrHandler(_db, _clock, _httpContextAccessor).Handle(
            new DispositionNcrCommand(ncr.Id, new DispositionNcrRequestModel { Code = NcrDispositionCode.Scrap }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(NcrDispositionCode.UseAsIs, null, false)]
    [InlineData(NcrDispositionCode.Reject, "  ", false)]
    [InlineData(NcrDispositionCode.UseAsIs, "Within customer deviation D-12", true)]
    [InlineData(NcrDispositionCode.Scrap, null, true)]
    public void DispositionValidator_RequiresNotesForUseAsIsAndReject(
        NcrDispositionCode code, string? notes, bool valid)
    {
        var result = new DispositionNcrValidator().Validate(
            new DispositionNcrCommand(1, new DispositionNcrRequestModel { Code = code, Notes = notes }));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Close_FromDispositioned_ClosesAndRecordsNotes()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);

        await new CloseNcrHandler(_db).Handle(
            new CloseNcrCommand(ncr.Id, new CloseNcrRequestModel { Notes = "Scrap verified" }),
            CancellationToken.None);

        (await ReloadAsync(ncr.Id)).Status.Should().Be(NcrStatus.Closed);
        var activity = await ActivityFor(ncr.Id);
        activity.Should().ContainSingle(a => a.Action == "closed")
            .Which.Description.Should().Contain("Scrap verified");
    }

    [Theory]
    [InlineData(NcrStatus.Open)]
    [InlineData(NcrStatus.Contained)]
    [InlineData(NcrStatus.Closed)]
    public async Task Close_FromNonDispositioned_Throws(NcrStatus from)
    {
        var ncr = await SeedNcrAsync(from);

        var act = () => new CloseNcrHandler(_db).Handle(
            new CloseNcrCommand(ncr.Id, new CloseNcrRequestModel()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ReloadAsync(ncr.Id)).Status.Should().Be(from);
    }

    [Fact]
    public async Task Reopen_FromClosed_ReturnsToDispositionedWithReason()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Closed);

        await new ReopenNcrHandler(_db).Handle(
            new ReopenNcrCommand(ncr.Id, new ReopenNcrRequestModel { Reason = "Scrap tag missing" }),
            CancellationToken.None);

        (await ReloadAsync(ncr.Id)).Status.Should().Be(NcrStatus.Dispositioned);
        (await ActivityFor(ncr.Id)).Should().ContainSingle(a => a.Action == "reopened")
            .Which.Description.Should().Contain("Scrap tag missing");
    }

    [Fact]
    public async Task Reopen_FromDispositioned_Throws()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);

        var act = () => new ReopenNcrHandler(_db).Handle(
            new ReopenNcrCommand(ncr.Id, new ReopenNcrRequestModel { Reason = "x" }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ReopenValidator_RequiresReason()
    {
        new ReopenNcrValidator().Validate(new ReopenNcrCommand(1, new ReopenNcrRequestModel()))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task CreateCapa_OnDispositionedNcr_KeepsStatus()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Dispositioned);
        _db.Parts.Add(new Part { Id = 1, PartNumber = "P-1" });
        await _db.SaveChangesAsync();

        await new NcrCapaService(_db, _clock).CreateCapaFromNcrAsync(ncr.Id, UserId, CancellationToken.None);

        var saved = await ReloadAsync(ncr.Id);
        saved.Status.Should().Be(NcrStatus.Dispositioned);
        saved.CapaId.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateCapa_OnOpenNcr_MovesToUnderReview()
    {
        var ncr = await SeedNcrAsync(NcrStatus.Open);
        _db.Parts.Add(new Part { Id = 1, PartNumber = "P-1" });
        await _db.SaveChangesAsync();

        await new NcrCapaService(_db, _clock).CreateCapaFromNcrAsync(ncr.Id, UserId, CancellationToken.None);

        (await ReloadAsync(ncr.Id)).Status.Should().Be(NcrStatus.UnderReview);
    }
}
