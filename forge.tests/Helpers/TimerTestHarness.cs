using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;

namespace Forge.Tests.Helpers;

/// <summary>
/// Real <see cref="StopActiveTimerHandler"/> over an InMemory context, with a
/// connected accounting provider and a linked employee so the QuickBooks
/// enqueue is reachable, and a mediator mock that routes
/// <see cref="StopActiveTimerCommand"/> to it.
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
    public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);

    public TimerTestHarness()
    {
        Repo = new TimeTrackingRepository(Db);
        Jobs = new JobRepository(Db);
        Clock.Setup(c => c.UtcNow).Returns(() => Now);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(UserGroup.Object);
        TimerHub.Setup(h => h.Clients).Returns(clients.Object);

        Mediator
            .Setup(m => m.Send(It.IsAny<StopActiveTimerCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<StoppedTimerResponseModel?> command, CancellationToken ct) =>
                StopActiveTimerHandler().Handle((StopActiveTimerCommand)command, ct));
    }

    public StopActiveTimerHandler StopActiveTimerHandler()
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
            .ReturnsAsync((string id) => new ApplicationUser { Id = int.Parse(id), AccountingEmployeeId = "EMP-1" });

        return new StopActiveTimerHandler(
            Repo,
            Db,
            TimerHub.Object,
            SyncQueue.Object,
            providers.Object,
            userManager.Object,
            Jobs,
            Mock.Of<ICustomerRepository>(),
            Mock.Of<ILogger<StopActiveTimerHandler>>());
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

    public async Task<TimeEntry> AddRunningTimerAsync(int userId, int? jobId, DateTimeOffset start)
    {
        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = jobId,
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
