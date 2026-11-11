using AwesomeAssertions;
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

[Collection(PostgresCollection.Name)]
public sealed class DailyDigestJobPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<ApplicationUser> AddUserWithOverdueJobAsync(AppDbContext db, TrackType track, JobStage stage)
    {
        var email = $"{Unique("digest")}@t.com";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FirstName = "Sam", LastName = "Worker", Initials = "SW", AvatarColor = "#888", IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Jobs.Add(new Job
        {
            JobNumber = Unique("J"), Title = "Bracket run", TrackTypeId = track.Id, CurrentStageId = stage.Id,
            AssigneeId = user.Id, DueDate = Now.AddDays(-2),
        });
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task The_digest_runs_on_postgres_and_skips_a_user_who_turned_it_off()
    {
        await using var db = fixture.CreateContext();
        var track = new TrackType { Name = "Digest", Code = Unique("dg"), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Open", Code = "o", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        var subscribed = await AddUserWithOverdueJobAsync(db, track, stage);
        var optedOut = await AddUserWithOverdueJobAsync(db, track, stage);
        db.UserPreferences.Add(new UserPreference { UserId = optedOut.Id, Key = DailyDigestJob.DigestPreferenceKey, ValueJson = "false" });
        await db.SaveChangesAsync();

        var recipients = new List<string>();
        var outbox = new Mock<IIntegrationOutboxService>();
        outbox
            .Setup(o => o.EnqueueEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, string?, int?, CancellationToken>((_, message, _, _, _) => recipients.Add(message.To))
            .ReturnsAsync(new IntegrationOutboxEntry());
        var terminology = new Mock<ITerminologyLabels>();
        terminology.Setup(t => t.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TermLabels.Defaults);
        var job = new DailyDigestJob(
            db, outbox.Object, Mock.Of<ISystemSettingRepository>(), new FixedClock(Now), terminology.Object,
            new ConfigurationBuilder().Build(), NullLogger<DailyDigestJob>.Instance);

        await job.SendDailyDigestAsync();

        recipients.Should().Contain(subscribed.Email!);
        recipients.Should().NotContain(optedOut.Email!);
    }
}
