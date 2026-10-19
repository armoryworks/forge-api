using System.Text.Json;


namespace Forge.Api.Features.Reports;

public static class ReportScheduleRecipients
{
    public static string[] Parse(string? recipientEmailsJson)
    {
        if (string.IsNullOrWhiteSpace(recipientEmailsJson)) return [];

        try
        {
            return (JsonSerializer.Deserialize<string[]>(recipientEmailsJson) ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool IsValid(string? recipientEmailsJson)
    {
        var recipients = Parse(recipientEmailsJson);
        return recipients.Length > 0 && recipients.All(IsEmail);
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
    }
}
