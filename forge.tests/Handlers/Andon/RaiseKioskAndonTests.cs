using System.Reflection;
using System.Security.Claims;

using FluentAssertions;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;
using Forge.Api.Features.Andon;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Andon;

public class RaiseKioskAndonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IMediator> _alertsReader = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly Mock<IJobOperationService> _operations = new();
    private readonly Mock<IClock> _clock = new();
    private JobStage _floor = null!;

    public RaiseKioskAndonTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _operations.Setup(o => o.IsTrackingEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _alertsReader
            .Setup(m => m.Send(It.IsAny<GetAndonAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _db.AndonAlerts.AsNoTracking().Select(a => new AndonAlertResponseModel
            {
                Id = a.Id,
                WorkCenterId = a.WorkCenterId,
                Type = a.Type,
                Status = a.Status,
                Notes = a.Notes,
                JobId = a.JobId,
            }).ToList());
        _mediator
            .Setup(m => m.Send(It.IsAny<CreateAndonAlertCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreateAndonAlertCommand command, CancellationToken ct) =>
                new CreateAndonAlertHandler(_db, _clock.Object, _httpContext.Object, _alertsReader.Object).Handle(command, ct));
    }

    [Fact]
    public async Task Handle_JobAtAWorkCenter_RaisesTheAlertThereAndLogsIt()
    {
        await SeedStagesAsync();
        var worker = await AddWorkerAsync();
        var lathe = await AddWorkCenterAsync("Lathe");
        var part = await AddPartAsync("PN-1");
        var turn = await AddStepAsync(part, 10, "Turn", lathe);
        await AddStepAsync(part, 20, "Deburr");
        var job = await AddJobAsync("J-1", part.Id);

        var result = await Handler().Handle(
            new RaiseKioskAndonCommand(job.Id, AndonAlertType.Stoppage, "  Spindle stalled  ", worker.Id),
            CancellationToken.None);

        var alert = await _db.AndonAlerts.AsNoTracking().SingleAsync();
        alert.WorkCenterId.Should().Be(lathe.Id);
        alert.JobId.Should().Be(job.Id);
        alert.Type.Should().Be(AndonAlertType.Stoppage);
        alert.RequestedById.Should().Be(worker.Id);
        alert.RequestedAt.Should().Be(Now);
        alert.Notes.Should().Be("Spindle stalled");

        var log = await _db.JobActivityLogs.AsNoTracking().SingleAsync(l => l.JobId == job.Id);
        log.Action.Should().Be(ActivityAction.AndonRaised);
        log.UserId.Should().Be(worker.Id);
        log.OperationId.Should().Be(turn.Id);
        log.WorkCenterId.Should().Be(lathe.Id);
        log.CreatedAt.Should().Be(Now);
        log.Description.Should().Be("Raised a stoppage andon at Lathe on operation 10 Turn.");

        result.AlertId.Should().Be(alert.Id);
        result.WorkCenterId.Should().Be(lathe.Id);
        result.WorkCenterName.Should().Be("Lathe");
        result.OperationId.Should().Be(turn.Id);
        result.OperationStepNumber.Should().Be(10);
        result.OperationTitle.Should().Be("Turn");
        result.JobNumber.Should().Be("J-1");
    }

    [Fact]
    public async Task Handle_WithTracking_UsesTheOperationStillOpen()
    {
        _operations.Setup(o => o.IsTrackingEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await SeedStagesAsync();
        var worker = await AddWorkerAsync();
        var lathe = await AddWorkCenterAsync("Lathe");
        var mill = await AddWorkCenterAsync("Mill");
        var part = await AddPartAsync("PN-1");
        var turn = await AddStepAsync(part, 10, "Turn", lathe);
        var face = await AddStepAsync(part, 20, "Face", mill);
        var job = await AddJobAsync("J-1", part.Id);
        _db.JobOperations.Add(new JobOperation { JobId = job.Id, OperationId = turn.Id, Status = JobOperationStatus.Complete });
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(
            new RaiseKioskAndonCommand(job.Id, AndonAlertType.Material, null, worker.Id), CancellationToken.None);

        result.OperationId.Should().Be(face.Id);
        (await _db.AndonAlerts.AsNoTracking().SingleAsync()).WorkCenterId.Should().Be(mill.Id);
    }

    [Fact]
    public async Task Handle_CurrentOperationHasNoWorkCenter_IsRefusedWithoutAnAlert()
    {
        await SeedStagesAsync();
        var worker = await AddWorkerAsync();
        var part = await AddPartAsync("PN-1");
        await AddStepAsync(part, 10, "Deburr");
        var job = await AddJobAsync("J-1", part.Id);

        var act = () => Handler().Handle(
            new RaiseKioskAndonCommand(job.Id, AndonAlertType.Quality, null, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("J-1 has no work center*");
        (await _db.AndonAlerts.CountAsync()).Should().Be(0);
        (await _db.JobActivityLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_JobWithoutRouting_IsRefused()
    {
        await SeedStagesAsync();
        var worker = await AddWorkerAsync();
        var job = await AddJobAsync("J-BARE", null);

        var act = () => Handler().Handle(
            new RaiseKioskAndonCommand(job.Id, AndonAlertType.Quality, null, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("J-BARE has no work center*");
    }

    [Fact]
    public async Task Handle_UnknownJob_ThrowsNotFound()
    {
        var act = () => Handler().Handle(
            new RaiseKioskAndonCommand(999, AndonAlertType.Quality, null, 1), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(AndonAlertType.Stoppage, true)]
    [InlineData(AndonAlertType.Quality, true)]
    [InlineData(AndonAlertType.Material, true)]
    [InlineData(AndonAlertType.Help, false)]
    [InlineData(AndonAlertType.Maintenance, false)]
    [InlineData(AndonAlertType.Safety, false)]
    public void Validator_AcceptsOnlyTheThreeTerminalTypes(AndonAlertType type, bool valid)
    {
        var result = new RaiseKioskAndonValidator().Validate(new RaiseKioskAndonCommand(1, type, null, 1));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RejectsNotesLongerThanTheColumn()
    {
        var result = new RaiseKioskAndonValidator().Validate(
            new RaiseKioskAndonCommand(1, AndonAlertType.Quality, new string('x', 2001), 1));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Endpoint_IsGatedOnShopFloorAndAndonAndNeedsASignedInWorker()
    {
        var controller = typeof(ShopFloorAndonController);
        var action = controller.GetMethod(nameof(ShopFloorAndonController.RaiseAndon))!;

        controller.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be("CAP-MFG-SHOPFLOOR");
        action.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be("CAP-EXT-ANDON");
        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        action.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    private RaiseKioskAndonHandler Handler() => new(_db, _mediator.Object, _operations.Object, _clock.Object);

    private async Task SeedStagesAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsDefault = true, IsShopFloor = true };
        _floor = new JobStage { Name = "Machining", Code = "machining", SortOrder = 1, IsShopFloor = true, TrackType = track };
        _db.TrackTypes.Add(track);
        _db.JobStages.Add(_floor);
        await _db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> AddWorkerAsync()
    {
        var user = new ApplicationUser
        {
            FirstName = "Pat",
            LastName = "Lathe",
            UserName = "pat.lathe@example.com",
            Email = "pat.lathe@example.com",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "Test"));
        _httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return user;
    }

    private async Task<WorkCenter> AddWorkCenterAsync(string name)
    {
        var workCenter = new WorkCenter { Name = name, Code = name.ToUpperInvariant() };
        _db.WorkCenters.Add(workCenter);
        await _db.SaveChangesAsync();
        return workCenter;
    }

    private async Task<Part> AddPartAsync(string partNumber)
    {
        var part = new Part { PartNumber = partNumber, Name = partNumber };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Operation> AddStepAsync(Part part, int stepNumber, string title, WorkCenter? workCenter = null)
    {
        var operation = new Operation { PartId = part.Id, StepNumber = stepNumber, Title = title, WorkCenterId = workCenter?.Id };
        _db.Operations.Add(operation);
        await _db.SaveChangesAsync();
        return operation;
    }

    private async Task<Job> AddJobAsync(string jobNumber, int? partId)
    {
        var job = new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            TrackTypeId = _floor.TrackTypeId,
            CurrentStageId = _floor.Id,
            PartId = partId,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }
}
