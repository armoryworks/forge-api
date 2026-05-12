using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class OutlookCalendarService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<OutlookCalendarService> logger) : ICalendarIntegrationService
{
    public string ProviderId => "outlook_calendar";

    public async Task<string> PushEventAsync(int userId, int integrationId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            subject = calendarEvent.Title,
            body = new { contentType = "text", content = calendarEvent.Description ?? "" },
            start = new { dateTime = calendarEvent.StartTime.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = calendarEvent.EndTime.ToString("o"), timeZone = "UTC" },
            location = new { displayName = calendarEvent.Location ?? "" },
        };

        var response = await client.PostAsJsonAsync(
            "https://graph.microsoft.com/v1.0/me/events", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var externalId = result.GetProperty("id").GetString()!;

        logger.LogInformation("Outlook Calendar: Created event {ExternalId} for user {UserId}", externalId, userId);
        return externalId;
    }

    public async Task UpdateEventAsync(int userId, int integrationId, string externalEventId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            subject = calendarEvent.Title,
            body = new { contentType = "text", content = calendarEvent.Description ?? "" },
            start = new { dateTime = calendarEvent.StartTime.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = calendarEvent.EndTime.ToString("o"), timeZone = "UTC" },
            location = new { displayName = calendarEvent.Location ?? "" },
        };

        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"https://graph.microsoft.com/v1.0/me/events/{externalEventId}")
        {
            Content = JsonContent.Create(body),
        };

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Outlook Calendar: Updated event {ExternalId} for user {UserId}", externalEventId, userId);
    }

    public async Task DeleteEventAsync(int userId, int integrationId, string externalEventId, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var response = await client.DeleteAsync(
            $"https://graph.microsoft.com/v1.0/me/events/{externalEventId}", ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Outlook Calendar: Deleted event {ExternalId} for user {UserId}", externalEventId, userId);
    }

    public async Task<List<CalendarFreeBusy>> GetFreeBusyAsync(int userId, int integrationId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            schedules = new[] { "me" },
            startTime = new { dateTime = start.ToString("o"), timeZone = "UTC" },
            endTime = new { dateTime = end.ToString("o"), timeZone = "UTC" },
        };

        var response = await client.PostAsJsonAsync(
            "https://graph.microsoft.com/v1.0/me/calendar/getSchedule", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var busyPeriods = new List<CalendarFreeBusy>();

        if (result.TryGetProperty("value", out var schedules))
        {
            foreach (var schedule in schedules.EnumerateArray())
            {
                if (schedule.TryGetProperty("scheduleItems", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        busyPeriods.Add(new CalendarFreeBusy(
                            DateTimeOffset.Parse(item.GetProperty("start").GetProperty("dateTime").GetString()!),
                            DateTimeOffset.Parse(item.GetProperty("end").GetProperty("dateTime").GetString()!),
                            true));
                    }
                }
            }
        }

        return busyPeriods;
    }

    public Task<CalendarSyncResult> SyncEventsAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        logger.LogInformation("Outlook Calendar: SyncEvents not yet implemented for user {UserId}", userId);
        return Task.FromResult(new CalendarSyncResult(0, 0, 0, null));
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var client = await CreateAuthenticatedClient(userId, integrationId, ct);
            var response = await client.GetAsync(
                "https://graph.microsoft.com/v1.0/me/calendar", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outlook Calendar: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<HttpClient> CreateAuthenticatedClient(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for this integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        var accessToken = creds.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token in credentials");

        var client = httpClientFactory.CreateClient("OutlookCalendar");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
