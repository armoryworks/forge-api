using System.Text.Json;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// A DocuSeal webhook reduced to what Forge acts on. <c>form.*</c> events carry
/// the submitter (<c>data.id</c> is the submitter, <c>data.submission.id</c> the
/// submission); <c>submission.*</c> events carry the submission (<c>data.id</c>).
/// The older <c>submitter.completed</c> shape (<c>data.submission_id</c>) and a
/// <c>form.completed</c> without a nested submission are still read.
/// </summary>
public record DocuSealWebhookEvent(
    int SubmissionId,
    int? SubmitterId,
    string Status,
    DateTimeOffset? CompletedAt,
    bool IsSubmitterEvent,
    bool AllSigned)
{
    public static DocuSealWebhookEvent? Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return Parse(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DocuSealWebhookEvent? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("event_type", out var eventProp) || eventProp.ValueKind != JsonValueKind.String)
            return null;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;

        var eventType = eventProp.GetString();
        var completedAt = ReadDate(data, "completed_at");

        switch (eventType)
        {
            case "submission.completed":
                return ReadInt(data, "id") is int submissionId
                    ? new DocuSealWebhookEvent(submissionId, null, "completed", completedAt, false, true)
                    : null;

            case "submission.expired":
                return ReadInt(data, "id") is int expiredId
                    ? new DocuSealWebhookEvent(expiredId, null, "expired", null, false, false)
                    : null;

            case "form.completed":
            case "form.declined":
            case "submitter.completed":
                break;

            default:
                return null;
        }

        var dataId = ReadInt(data, "id");
        int? nestedSubmissionId = null;
        string? submissionStatus = null;
        if (data.TryGetProperty("submission", out var submission) && submission.ValueKind == JsonValueKind.Object)
        {
            nestedSubmissionId = ReadInt(submission, "id");
            submissionStatus = ReadString(submission, "status");
        }

        if (eventType == "form.declined")
        {
            var declinedId = nestedSubmissionId ?? ReadInt(data, "submission_id");
            return declinedId is int id
                ? new DocuSealWebhookEvent(id, dataId, "declined", null, true, false)
                : null;
        }

        if (eventType == "form.completed" && nestedSubmissionId is null && ReadInt(data, "submission_id") is null)
        {
            return dataId is int legacyId
                ? new DocuSealWebhookEvent(legacyId, legacyId, ReadString(data, "status") ?? "completed", completedAt, false, true)
                : null;
        }

        var resolvedSubmissionId = nestedSubmissionId ?? ReadInt(data, "submission_id");
        if (resolvedSubmissionId is not int resolved)
            return null;

        var allSigned = string.Equals(submissionStatus, "completed", StringComparison.OrdinalIgnoreCase);
        return new DocuSealWebhookEvent(
            resolved, dataId, ReadString(data, "status") ?? "completed", completedAt, true, allSigned);
    }

    private static int? ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            && prop.TryGetDateTimeOffset(out var value)
            ? value
            : null;
}
