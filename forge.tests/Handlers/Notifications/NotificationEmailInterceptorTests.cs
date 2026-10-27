using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Notifications;

public class NotificationEmailInterceptorTests
{
    private readonly Mock<IClock> _clock = new();

    public NotificationEmailInterceptorTests() => _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

    private AppDbContext CreateDb(bool notificationsOn = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendBaseUrl"] = "https://forge.example.test" })
            .Build();
        var interceptor = new NotificationEmailInterceptor(
            notificationsOn ? new StubCapabilitySnapshotProvider(NotificationEmailInterceptor.CapabilityCode) : StubCapabilitySnapshotProvider.Off,
            _clock.Object,
            configuration,
            NullLogger<NotificationEmailInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        return new TestAppDbContext(options);
    }

    private static ApplicationUser Seed(AppDbContext db, string emailEnabled = "true", string? smtpMode = null)
    {
        var user = new ApplicationUser { UserName = "ada@example.test", Email = "ada@example.test", FirstName = "Ada", LastName = "Lane" };
        db.Users.Add(user);
        db.SystemSettings.Add(new SystemSetting { Key = NotificationEmailInterceptor.EmailEnabledSettingKey, Value = emailEnabled });
        db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Acme Test" });
        if (smtpMode is not null)
            db.SystemSettings.Add(new SystemSetting { Key = SmtpSettings.KeyMode, Value = smtpMode });
        db.SaveChanges();
        return user;
    }

    private static Notification Assignment(int userId, int jobId = 7) => new()
    {
        UserId = userId, Type = "assignment", Severity = "info", Source = "assignments",
        Title = "Job J-7 assigned to you", Message = "Job J-7 — Bracket is now assigned to you.",
        EntityType = "Job", EntityId = jobId,
    };

    private static List<IntegrationOutboxEntry> Emails(AppDbContext db) =>
        db.IntegrationOutboxEntries.AsNoTracking().Where(e => e.Provider == IntegrationProvider.Email).ToList();

    [Fact]
    public async Task An_assignment_queues_one_email_with_a_link_to_the_record()
    {
        using var db = CreateDb();
        var ada = Seed(db);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        var email = Emails(db).Should().ContainSingle().Subject;
        email.EntityType.Should().Be("Job");
        email.OperationKey.Should().StartWith($"notif-email:{ada.Id}:job:7:");
        var message = JsonSerializer.Deserialize<EmailMessage>(email.Payload)!;
        message.To.Should().Be("ada@example.test");
        message.Subject.Should().Be("[Acme Test] Job J-7 assigned to you");
        message.HtmlBody.Should().Contain("https://forge.example.test/open/Job/7").And.Contain("Hi Ada,");
    }

    [Fact]
    public async Task A_second_notification_for_the_same_record_within_five_minutes_is_coalesced()
    {
        using var db = CreateDb();
        var ada = Seed(db);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();
        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().ContainSingle();
    }

    [Fact]
    public async Task After_the_window_the_same_record_emails_again()
    {
        using var db = CreateDb();
        var ada = Seed(db);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow.AddMinutes(6));
        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().HaveCount(2);
    }

    [Fact]
    public async Task Spanish_users_get_the_spanish_email_frame()
    {
        using var db = CreateDb();
        var ada = Seed(db);
        db.UserPreferences.Add(new UserPreference { UserId = ada.Id, Key = NotificationEmailPlanner.LanguagePreferenceKey, ValueJson = "\"es\"" });
        db.SaveChanges();

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        var message = JsonSerializer.Deserialize<EmailMessage>(Emails(db).Single().Payload)!;
        message.HtmlBody.Should().Contain("Hola, Ada:").And.Contain("Administrar preferencias");
    }

    [Fact]
    public async Task Nothing_is_queued_while_company_email_notifications_are_off()
    {
        using var db = CreateDb();
        var ada = Seed(db, emailEnabled: "false");

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().BeEmpty();
        db.Notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task Nothing_is_queued_while_outbound_email_is_disabled()
    {
        using var db = CreateDb();
        var ada = Seed(db, smtpMode: IntegrationModeChoices.Disabled);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().BeEmpty();
    }

    [Fact]
    public async Task Real_mode_without_a_host_queues_nothing()
    {
        using var db = CreateDb();
        var ada = Seed(db, smtpMode: IntegrationModeChoices.Real);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_queued_while_notifications_are_off()
    {
        using var db = CreateDb(notificationsOn: false);
        var ada = Seed(db);

        db.Notifications.Add(Assignment(ada.Id));
        await db.SaveChangesAsync();

        Emails(db).Should().BeEmpty();
    }

    [Fact]
    public void The_synchronous_save_path_queues_too()
    {
        using var db = CreateDb();
        var ada = Seed(db);

        db.Notifications.Add(new Notification { UserId = ada.Id, Type = "alert", Severity = "critical", Title = "Line down", Message = "Press 4 stopped" });
        db.SaveChanges();

        Emails(db).Should().ContainSingle()
            .Which.OperationKey.Should().StartWith($"notif-email:{ada.Id}:none:alert:");
    }
}
