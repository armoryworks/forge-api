using FluentAssertions;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Notifications;

public class NotificationEmailPlannerTests
{
    private const int AdaId = 4;
    private const int BenId = 5;

    private static readonly Dictionary<int, NotificationEmailRecipient> Recipients = new()
    {
        [AdaId] = new NotificationEmailRecipient(AdaId, "ada@example.test", "Ada", true),
        [BenId] = new NotificationEmailRecipient(BenId, "ben@example.test", "Ben", true),
    };

    private static Notification Note(int userId, string type = "assignment", string severity = "info", string? entityType = "Job", int? entityId = 7) =>
        new() { UserId = userId, Type = type, Severity = severity, Title = "T", Message = "M", EntityType = entityType, EntityId = entityId };

    private static List<PlannedNotificationEmail> Plan(
        IEnumerable<Notification> notes,
        Dictionary<(int, string), string>? prefs = null,
        IEnumerable<string>? recent = null,
        Dictionary<int, NotificationEmailRecipient>? recipients = null) =>
        NotificationEmailPlanner.Plan(notes, recipients ?? Recipients, prefs ?? [], recent ?? []);

    [Theory]
    [InlineData("alert", "critical", NotificationEmailClass.Critical)]
    [InlineData("assignment", "critical", NotificationEmailClass.Critical)]
    [InlineData("assignment", "info", NotificationEmailClass.Assignment)]
    [InlineData("mention", "info", NotificationEmailClass.Mention)]
    [InlineData("Mention", "warning", NotificationEmailClass.Mention)]
    public void Critical_assignment_and_mention_notifications_are_emailed(string type, string severity, NotificationEmailClass expected)
    {
        NotificationEmailPlanner.Classify(Note(AdaId, type, severity)).Should().Be(expected);
    }

    [Theory]
    [InlineData("alert", "warning")]
    [InlineData("expenses", "info")]
    [InlineData("approval_escalated", "warning")]
    public void Other_notifications_stay_in_app(string type, string severity)
    {
        NotificationEmailPlanner.Classify(Note(AdaId, type, severity)).Should().BeNull();
    }

    [Fact]
    public void Every_class_is_on_until_the_user_turns_it_off()
    {
        var notes = new[] { Note(AdaId), Note(BenId, "mention", entityId: 8) };
        var prefs = new Dictionary<(int, string), string>
        {
            [(BenId, NotificationEmailPlanner.MentionPreferenceKey)] = "false",
            [(AdaId, NotificationEmailPlanner.MentionPreferenceKey)] = "false",
        };

        Plan(notes, prefs).Select(p => p.Recipient.UserId).Should().Equal(AdaId);
    }

    [Fact]
    public void Turning_off_critical_email_stops_critical_alerts_only()
    {
        var notes = new[] { Note(AdaId, "alert", "critical", entityId: 1), Note(AdaId, entityId: 2) };
        var prefs = new Dictionary<(int, string), string> { [(AdaId, NotificationEmailPlanner.CriticalPreferenceKey)] = " false " };

        Plan(notes, prefs).Select(p => p.Class).Should().Equal(NotificationEmailClass.Assignment);
    }

    [Fact]
    public void Inactive_users_and_users_without_an_email_get_nothing()
    {
        var recipients = new Dictionary<int, NotificationEmailRecipient>
        {
            [AdaId] = new(AdaId, "ada@example.test", "Ada", false),
            [BenId] = new(BenId, "  ", "Ben", true),
        };

        Plan([Note(AdaId), Note(BenId), Note(99)], recipients: recipients).Should().BeEmpty();
    }

    [Fact]
    public void Duplicates_for_one_user_and_record_coalesce_within_the_batch()
    {
        var planned = Plan([Note(AdaId), Note(AdaId, "mention"), Note(AdaId, entityId: 8), Note(BenId)]);

        planned.Select(p => (p.Recipient.UserId, p.Notification.EntityId)).Should().Equal((AdaId, 7), (AdaId, 8), (BenId, 7));
    }

    [Fact]
    public void An_email_already_queued_for_the_user_and_record_suppresses_the_next()
    {
        var recent = new[] { NotificationEmailPlanner.CoalesceKeyFor(Note(AdaId)) + "abc" };

        Plan([Note(AdaId), Note(BenId)], recent: recent).Select(p => p.Recipient.UserId).Should().Equal(BenId);
    }

    [Fact]
    public void Notifications_without_a_record_coalesce_by_type()
    {
        var planned = Plan([Note(AdaId, "alert", "critical", null, null), Note(AdaId, "alert", "critical", null, null), Note(AdaId, "mention", entityType: null, entityId: null)]);

        planned.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("\"es\"", "es")]
    [InlineData("es", "es")]
    [InlineData("\"fr\"", "en")]
    [InlineData(null, "en")]
    public void The_email_uses_the_language_the_user_last_chose(string? stored, string expected)
    {
        var prefs = stored is null
            ? new Dictionary<(int, string), string>()
            : new Dictionary<(int, string), string> { [(AdaId, NotificationEmailPlanner.LanguagePreferenceKey)] = stored };

        Plan([Note(AdaId)], prefs).Single().Language.Should().Be(expected);
    }
}
