using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Notifications;

[Collection(PostgresCollection.Name)]
public sealed class NotificationEmailPostgresTests(PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var interceptor = new NotificationEmailInterceptor(
            new StubCapabilitySnapshotProvider(NotificationEmailInterceptor.CapabilityCode),
            new SystemClock(),
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationEmailInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseVector())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, new SystemClock());
    }

    private static IHubContext<NotificationHub> Hub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private static async Task<ApplicationUser> AddUserAsync(AppDbContext db, string first)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser { UserName = $"{first}{tag}@t.test", Email = $"{first}{tag}@t.test", FirstName = first, LastName = "Notify" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task EnableEmailAsync(AppDbContext db)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == NotificationEmailInterceptor.EmailEnabledSettingKey);
        if (row is null)
            db.SystemSettings.Add(new SystemSetting { Key = NotificationEmailInterceptor.EmailEnabledSettingKey, Value = "true" });
        else
            row.Value = "true";
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_assignment_queues_exactly_one_outbox_email_in_the_same_save()
    {
        await using var db = CreateContext();
        await EnableEmailAsync(db);
        var manager = await AddUserAsync(db, "Mona");
        var worker = await AddUserAsync(db, "Wes");
        db.CurrentUserId = manager.Id;

        var handler = new CreateNotificationHandler(new NotificationRepository(db), Hub());
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreateNotificationCommand c, CancellationToken ct) => handler.Handle(c, ct));
        var notifier = new AssignmentNotifier(db, sender.Object);
        var jobId = 900_000 + Random.Shared.Next(1, 99_999);

        await notifier.JobAssignedAsync(jobId, "J-PG", "Bracket", worker.Id, CancellationToken.None);
        await notifier.JobAssignedAsync(jobId, "J-PG", "Bracket", worker.Id, CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var emails = await verify.IntegrationOutboxEntries.AsNoTracking()
            .Where(e => e.Provider == IntegrationProvider.Email && e.EntityType == "Job" && e.EntityId == jobId)
            .ToListAsync();
        emails.Should().ContainSingle()
            .Which.OperationKey.Should().StartWith($"notif-email:{worker.Id}:job:{jobId}:");
        (await verify.Notifications.CountAsync(n => n.UserId == worker.Id && n.Type == "assignment")).Should().Be(2);
    }
}
