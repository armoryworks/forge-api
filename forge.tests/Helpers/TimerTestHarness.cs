using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;

namespace Forge.Tests.Helpers;

/// <summary>
/// Real <see cref="StopActiveTimerHandler"/> (over a real <see cref="TimerStopService"/>)
/// and <see cref="ResumeStoppedTimerHandler"/> over an InMemory context, with a
/// connected accounting provider and a linked employee so the QuickBooks
/// enqueue is reachable (each enqueue also lands a real sync-queue row), a
/// mediator mock that routes both commands to them, and the seeded clock
/// event types plus two custom ones.
/// </summary>
public sealed class TimerTestHarness
{
    public AppDbContext Db { get; } = TestDbContextFactory.Create();
    public TimeTrackingRepository Repo { get; }
    public JobRepository Jobs { get; }
    public Mock<ISyncQueueRepository> SyncQueue { get; } = new();
    public Mock<IHubContext<TimerHub>> TimerHub { get; } = new();
    public Mock<IClientProxy> UserGroup { get; } = new();
    public Mock<IMediator> Mediator { get; } = new();
    public Mock<IClock> Clock { get; } = new();
    public Mock<IClockEventTypeService> ClockEventTypes { get; } = new();
    public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);

    public TimerTestHarness()
    {
        Repo = new TimeTrackingRepository(Db);
        Jobs = new JobRepository(Db, Clock.Object);
        Clock.Setup(c => c.UtcNow).Returns(() => Now);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(UserGroup.Object);
        TimerHub.Setup(h => h.Clients).Returns(clients.Object);

        SyncQueue
            .Setup(q => q.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string entityType, int entityId, string operation, string? payload, CancellationToken ct) =>
                new SyncQueueRepository(Db).EnqueueAsync(entityType, entityId, operation, payload, ct));

        Mediator
            .Setup(m => m.Send(It.IsAny<StopActiveTimerCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<StoppedTimerResponseModel?> command, CancellationToken ct) =>
                StopActiveTimerHandler().Handle((StopActiveTimerCommand)command, ct));
        Mediator
            .Setup(m => m.Send(It.IsAny<ResumeStoppedTimerCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<TimeEntryResponseModel?> command, CancellationToken ct) =>
                new ResumeStoppedTimerHandler(Repo, Db, TimerHub.Object).Handle((ResumeStoppedTimerCommand)command, ct));

        var definitions = new List<ClockEventTypeDefinition>
        {
            new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
            new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
            new("LunchStart", "Start Lunch", "OnLunch", "LunchEnd", "lunch", true, true, "restaurant", "#f97316"),
            new("shift_end", "End Shift", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            new("smoke_break", "Smoke Break", "OnBreak", "ClockIn", "break", true, true, "pause", "#f59e0b"),
            new("return_from_errand", "Back", "In", null, "work", true, false, "login", "#22c55e"),
        };
        ClockEventTypes.Setup(t => t.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => definitions.FirstOrDefault(d => d.Code == code));
    }

    public IHttpContextAccessor SignedIn(int userId)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
        });
        return accessor.Object;
    }

    public StopActiveTimerHandler StopActiveTimerHandler() => new(Repo, TimerStopService());

    public TimerStopService TimerStopService()
    {
        var accounting = new Mock<IAccountingService>();
        accounting.Setup(a => a.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountingSyncStatus(true, null, 0, 0));
        var providers = new Mock<IAccountingProviderFactory>();
        providers.Setup(p => p.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounting.Object);

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new ApplicationUser { Id = int.Parse(id), AccountingEmployeeId = $"EMP-{id}" });

        return new TimerStopService(
            Repo,
            Db,
            TimerHub.Object,
            SyncQueue.Object,
            providers.Object,
            userManager.Object,
            Jobs,
            Mock.Of<ICustomerRepository>(),
            Mock.Of<ILogger<TimerStopService>>());
    }

    public async Task<ApplicationUser> AddUserAsync()
    {
        var user = new ApplicationUser
        {
            FirstName = "Pat",
            LastName = "Machinist",
            UserName = $"pat{Guid.NewGuid():N}@example.com",
            Email = "pat@example.com",
            IsActive = true,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    public async Task<Job> AddJobAsync(string jobNumber, bool isArchived = false, JobDisposition? disposition = null)
    {
        var job = new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            TrackTypeId = 1,
            CurrentStageId = 1,
            IsArchived = isArchived,
            Disposition = disposition,
        };
        Db.Jobs.Add(job);
        await Db.SaveChangesAsync();
        return job;
    }

    public async Task<JobOperation> AddJobOperationAsync(int jobId, int stepNumber)
    {
        var row = new JobOperation
        {
            JobId = jobId,
            OperationId = 1000 + stepNumber,
            StepNumber = stepNumber,
            Title = $"Step {stepNumber}",
            Status = JobOperationStatus.InProgress,
        };
        Db.JobOperations.Add(row);
        await Db.SaveChangesAsync();
        return row;
    }

    public Task<TimeEntry> AddRunningOperationTimerAsync(int userId, JobOperation row, DateTimeOffset start)
        => AddRunningTimerAsync(userId, row.JobId, start, row.OperationId, row.Id);

    public async Task<TimeEntry> AddRunningTimerAsync(
        int userId, int? jobId, DateTimeOffset start, int? operationId = null, int? jobOperationId = null)
    {
        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = jobId,
            OperationId = operationId,
            JobOperationId = jobOperationId,
            Date = DateOnly.FromDateTime(start.UtcDateTime),
            TimerStart = start,
            DurationMinutes = 0,
            IsManual = false,
        };
        Db.TimeEntries.Add(entry);
        await Db.SaveChangesAsync();
        return entry;
    }
}
