using System.Text.Json;

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Reports;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class ReportScheduleJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IMediator> _mediator = new();

    public ReportScheduleJobTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _mediator
            .Setup(m => m.Send(It.IsAny<ExportReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExportReportResult([1, 2, 3], "text/csv", "Weekly jobs.csv"));
    }

    private ReportScheduleSender Sender() =>
        new(_db, _mediator.Object, new IntegrationOutboxService(_db, _clock.Object, NullLogger<IntegrationOutboxService>.Instance), _clock.Object);

    private ReportScheduleJob Job(params string[] capabilities) =>
        new(_db, Sender(), _clock.Object, NullLogger<ReportScheduleJob>.Instance,
            new StubCapabilitySnapshotProvider(capabilities.Length == 0 ? [ReportScheduleJob.CapabilityCode] : capabilities));

    private ReportSchedule AddSchedule(DateTimeOffset? nextRunAt, string cron = "0 7 * * 1")
    {
        var report = new SavedReport { Name = "Weekly jobs", EntitySource = "Jobs", ColumnsJson = """["JobNumber"]""", UserId = 1 };
        _db.SavedReports.Add(report);
        _db.SaveChanges();
        var schedule = new ReportSchedule
        {
            SavedReportId = report.Id,
            CronExpression = cron,
            RecipientEmailsJson = """["lena@example.test","Marco@example.test","lena@example.test"]""",
            Format = ReportExportFormat.Csv,
            SubjectTemplate = "Weekly jobs",
            NextRunAt = nextRunAt,
        };
        _db.ReportSchedules.Add(schedule);
        _db.SaveChanges();
        return schedule;
    }

    [Fact]
    public async Task A_due_schedule_emails_each_recipient_with_the_export_and_moves_to_its_next_run()
    {
        var schedule = AddSchedule(Now.AddMinutes(-3));

        await Job().RunDueAsync(CancellationToken.None);

        var emails = _db.IntegrationOutboxEntries.ToList()
            .Select(e => JsonSerializer.Deserialize<EmailMessage>(e.Payload)!)
            .ToList();
        emails.Select(e => e.To).Should().BeEquivalentTo(["lena@example.test", "Marco@example.test"]);
        emails.Should().OnlyContain(e => e.Subject == "Weekly jobs" && e.Attachments!.Single().FileName == "Weekly jobs.csv");
        _mediator.Verify(m => m.Send(
            It.Is<ExportReportQuery>(q => q.ReportId == schedule.SavedReportId && q.Format == ReportExportFormat.Csv && q.RequestingUserId == null),
            It.IsAny<CancellationToken>()), Times.Once);

        var stored = _db.ReportSchedules.Single();
        stored.LastSentAt.Should().Be(Now);
        stored.NextRunAt.Should().Be(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_due_schedule_enqueues_one_email_with_the_attachment_and_the_dispatcher_sends_it()
    {
        var schedule = AddSchedule(Now.AddMinutes(-1));
        schedule.RecipientEmailsJson = """["lena@example.test"]""";
        _db.SaveChanges();
        var email = new Mock<IEmailService>();
        var sent = new List<EmailMessage>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback((EmailMessage m, CancellationToken _) => sent.Add(m))
            .Returns(Task.CompletedTask);

        await Job().RunDueAsync(CancellationToken.None);
        _db.IntegrationOutboxEntries.Should().ContainSingle(e => e.Provider == IntegrationProvider.Email && e.EntityType == "ReportSchedule");

        await new IntegrationOutboxDispatcherJob(
            _db, email.Object, _clock.Object, NullLogger<IntegrationOutboxDispatcherJob>.Instance,
            Mock.Of<Forge.Api.Features.Webhooks.IWebhookDeliveryService>(), []).DispatchPendingAsync(CancellationToken.None);

        var message = sent.Should().ContainSingle().Subject;
        message.To.Should().Be("lena@example.test");
        message.Attachments.Should().ContainSingle(a => a.FileName == "Weekly jobs.csv" && a.Content.SequenceEqual(new byte[] { 1, 2, 3 }));
        _db.IntegrationOutboxEntries.Single().Status.Should().Be(OutboxStatus.Sent);
    }

    [Fact]
    public async Task A_schedule_that_is_not_due_sends_nothing()
    {
        AddSchedule(Now.AddDays(2));

        await Job().RunDueAsync(CancellationToken.None);

        _db.IntegrationOutboxEntries.Should().BeEmpty();
        _db.ReportSchedules.Single().LastSentAt.Should().BeNull();
    }

    [Fact]
    public async Task A_schedule_without_a_next_run_is_given_one_without_sending()
    {
        AddSchedule(null);

        await Job().RunDueAsync(CancellationToken.None);

        _db.IntegrationOutboxEntries.Should().BeEmpty();
        _db.ReportSchedules.Single().NextRunAt.Should().Be(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Running_twice_for_the_same_occurrence_does_not_send_twice()
    {
        AddSchedule(Now.AddMinutes(-3));
        await Job().RunDueAsync(CancellationToken.None);
        _db.ReportSchedules.Single().NextRunAt = Now.AddMinutes(-3);
        await _db.SaveChangesAsync();

        await Job().RunDueAsync(CancellationToken.None);

        _db.IntegrationOutboxEntries.Count().Should().Be(2);
    }

    [Fact]
    public async Task Nothing_runs_while_the_dashboards_capability_is_off()
    {
        AddSchedule(Now.AddMinutes(-3));

        await Job("CAP-SOMETHING-ELSE").RunDueAsync(CancellationToken.None);

        _db.IntegrationOutboxEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_now_emails_immediately_and_logs_the_activity()
    {
        var schedule = AddSchedule(Now.AddDays(5));
        var handler = new RunReportScheduleNowHandler(_db, Sender(), _clock.Object);

        var recipients = await handler.Handle(new RunReportScheduleNowCommand(schedule.Id), CancellationToken.None);

        recipients.Should().Be(2);
        _db.IntegrationOutboxEntries.Count().Should().Be(2);
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "ReportSchedule" && a.EntityId == schedule.Id && a.Action == "sent");
        _db.ReportSchedules.Single().NextRunAt.Should().Be(Now.AddDays(5));
    }

    [Theory]
    [InlineData("0 7 * * 1", true)]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 0 7 * * MON", true)]
    [InlineData("every monday", false)]
    [InlineData("61 7 * * 1", false)]
    [InlineData("", false)]
    public void Cron_expressions_are_validated(string cron, bool valid)
    {
        ReportScheduleCron.IsValid(cron).Should().Be(valid);
    }

    [Theory]
    [InlineData("""["a@example.test"]""", true)]
    [InlineData("""["a@example.test","b@example.test"]""", true)]
    [InlineData("""[]""", false)]
    [InlineData("""["not-an-email"]""", false)]
    [InlineData("a@example.test", false)]
    public void Recipient_lists_are_validated(string json, bool valid)
    {
        ReportScheduleRecipients.IsValid(json).Should().Be(valid);
    }
}
