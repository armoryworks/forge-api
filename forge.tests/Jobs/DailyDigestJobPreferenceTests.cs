using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Terminology;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class DailyDigestJobPreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static void SeedUserWithOverdueJob(AppDbContext db, int userId, string email)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = email,
            UserName = email,
            FirstName = "Sam",
            LastName = "Worker",
            IsActive = true,
        });
        db.Jobs.Add(new Job
        {
            JobNumber = $"J-{userId}",
            Title = "Bracket run",
            TrackTypeId = 1,
            CurrentStageId = 1,
            AssigneeId = userId,
            DueDate = Now.AddDays(-2),
        });
    }

    private static async Task<List<string>> RunDigestAsync(AppDbContext db)
    {
        var recipients = new List<string>();
        var outbox = new Mock<IIntegrationOutboxService>();
        outbox
            .Setup(o => o.EnqueueEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, string?, int?, CancellationToken>((_, message, _, _, _) => recipients.Add(message.To))
            .ReturnsAsync(new IntegrationOutboxEntry());
        var terminology = new Mock<ITerminologyLabels>();
        terminology.Setup(t => t.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TermLabels.Defaults);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FrontendBaseUrl.SettingKey] = "https://forge.example.test" })
            .Build();

        var job = new DailyDigestJob(
            db,
            outbox.Object,
            Mock.Of<ISystemSettingRepository>(),
            new FixedClock(Now),
            terminology.Object,
            configuration,
            NullLogger<DailyDigestJob>.Instance);

        await job.SendDailyDigestAsync();
        return recipients;
    }

    [Fact]
    public async Task Users_without_a_digest_preference_receive_the_digest()
    {
        using var db = TestDbContextFactory.Create();
        SeedUserWithOverdueJob(db, 41, "sam@forge.local");
        await db.SaveChangesAsync();

        var recipients = await RunDigestAsync(db);

        recipients.Should().Equal("sam@forge.local");
    }

    [Fact]
    public async Task Users_who_turned_the_digest_off_are_skipped()
    {
        using var db = TestDbContextFactory.Create();
        SeedUserWithOverdueJob(db, 41, "sam@forge.local");
        SeedUserWithOverdueJob(db, 42, "lee@forge.local");
        db.UserPreferences.Add(new UserPreference { UserId = 42, Key = DailyDigestJob.DigestPreferenceKey, ValueJson = "false" });
        await db.SaveChangesAsync();

        var recipients = await RunDigestAsync(db);

        recipients.Should().Equal("sam@forge.local");
    }

    [Fact]
    public async Task Turning_the_digest_back_on_resumes_it()
    {
        using var db = TestDbContextFactory.Create();
        SeedUserWithOverdueJob(db, 42, "lee@forge.local");
        db.UserPreferences.Add(new UserPreference { UserId = 42, Key = DailyDigestJob.DigestPreferenceKey, ValueJson = "true" });
        await db.SaveChangesAsync();

        var recipients = await RunDigestAsync(db);

        recipients.Should().Equal("lee@forge.local");
    }
}
