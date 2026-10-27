using System.Text.Json;

using Forge.Core.Entities;

namespace Forge.Api.Features.Notifications;

public static class NotificationEmailPlanner
{
    public const string CriticalPreferenceKey = "notif:email_critical";
    public const string AssignmentPreferenceKey = "notif:email_assignment";
    public const string MentionPreferenceKey = "notif:email_mention";
    public const string LanguagePreferenceKey = "ui:language";
    public const string AssignmentType = "assignment";
    public const string MentionType = "mention";
    public const string OperationKeyPrefix = "notif-email:";
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(5);

    public static readonly IReadOnlyList<string> PreferenceKeys =
        [CriticalPreferenceKey, AssignmentPreferenceKey, MentionPreferenceKey, LanguagePreferenceKey];

    private static readonly IReadOnlySet<string> SupportedLanguages = new HashSet<string>(StringComparer.Ordinal) { "en", "es" };

    public static NotificationEmailClass? Classify(Notification notification)
    {
        if (string.Equals(notification.Severity, "critical", StringComparison.OrdinalIgnoreCase))
            return NotificationEmailClass.Critical;
        if (string.Equals(notification.Type, AssignmentType, StringComparison.OrdinalIgnoreCase))
            return NotificationEmailClass.Assignment;
        if (string.Equals(notification.Type, MentionType, StringComparison.OrdinalIgnoreCase))
            return NotificationEmailClass.Mention;
        return null;
    }

    public static string PreferenceKeyFor(NotificationEmailClass emailClass) => emailClass switch
    {
        NotificationEmailClass.Critical => CriticalPreferenceKey,
        NotificationEmailClass.Assignment => AssignmentPreferenceKey,
        _ => MentionPreferenceKey,
    };

    public static string CoalesceKeyFor(Notification notification) =>
        notification.EntityType is { Length: > 0 } entityType && notification.EntityId is int entityId
            ? $"{OperationKeyPrefix}{notification.UserId}:{entityType.ToLowerInvariant()}:{entityId}:"
            : $"{OperationKeyPrefix}{notification.UserId}:none:{notification.Type.ToLowerInvariant()}:";

    public static bool IsOptedIn(string? valueJson) =>
        valueJson is null || !string.Equals(valueJson.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    public static string LanguageFrom(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
            return "en";

        var trimmed = valueJson.Trim();
        var code = trimmed.StartsWith('"') ? JsonSerializer.Deserialize<string>(trimmed) : trimmed;
        code = code?.Trim().ToLowerInvariant();
        return code is not null && SupportedLanguages.Contains(code) ? code : "en";
    }

    public static List<PlannedNotificationEmail> Plan(
        IEnumerable<Notification> notifications,
        IReadOnlyDictionary<int, NotificationEmailRecipient> recipients,
        IReadOnlyDictionary<(int UserId, string Key), string> preferences,
        IEnumerable<string> recentOperationKeys)
    {
        var sent = recentOperationKeys.ToList();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<PlannedNotificationEmail>();

        foreach (var notification in notifications)
        {
            if (Classify(notification) is not NotificationEmailClass emailClass)
                continue;
            if (!recipients.TryGetValue(notification.UserId, out var recipient)
                || !recipient.IsActive
                || string.IsNullOrWhiteSpace(recipient.Email))
                continue;
            if (!IsOptedIn(preferences.GetValueOrDefault((notification.UserId, PreferenceKeyFor(emailClass)))))
                continue;

            var key = CoalesceKeyFor(notification);
            if (claimed.Contains(key) || sent.Any(k => k.StartsWith(key, StringComparison.Ordinal)))
                continue;

            claimed.Add(key);
            planned.Add(new PlannedNotificationEmail(
                notification,
                recipient,
                emailClass,
                key,
                LanguageFrom(preferences.GetValueOrDefault((notification.UserId, LanguagePreferenceKey)))));
        }

        return planned;
    }
}
