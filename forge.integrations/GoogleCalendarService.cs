using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class GoogleCalendarService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<GoogleCalendarService> logger) : ICalendarIntegrationService
{
    public string ProviderId => "google_calendar";

    public async Task<string> PushEventAsync(int userId, int integrationId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            summary = calendarEvent.Title,
            description = calendarEvent.Description,
            location = calendarEvent.Location,
            start = new { dateTime = calendarEvent.StartTime.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = calendarEvent.EndTime.ToString("o"), timeZone = "UTC" },
        };

        var response = await client.PostAsJsonAsync(
            "https://www.googleapis.com/calendar/v3/calendars/primary/events", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var externalId = result.GetProperty("id").GetString()!;

        logger.LogInformation("Google Calendar: Created event {ExternalId} for user {UserId}", externalId, userId);
        return externalId;
    }

    public async Task UpdateEventAsync(int userId, int integrationId, string externalEventId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            summary = calendarEvent.Title,
            description = calendarEvent.Description,
            location = calendarEvent.Location,
            start = new { dateTime = calendarEvent.StartTime.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = calendarEvent.EndTime.ToString("o"), timeZone = "UTC" },
        };

        var response = await client.PutAsJsonAsync(
            $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{externalEventId}", body, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Google Calendar: Updated event {ExternalId} for user {UserId}", externalEventId, userId);
    }

    public async Task DeleteEventAsync(int userId, int integrationId, string externalEventId, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var response = await client.DeleteAsync(
            $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{externalEventId}", ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Google Calendar: Deleted event {ExternalId} for user {UserId}", externalEventId, userId);
    }

    public async Task<List<CalendarFreeBusy>> GetFreeBusyAsync(int userId, int integrationId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClient(userId, integrationId, ct);

        var body = new
        {
            timeMin = start.ToString("o"),
            timeMax = end.ToString("o"),
            items = new[] { new { id = "primary" } },
        };

        var response = await client.PostAsJsonAsync(
            "https://www.googleapis.com/calendar/v3/freeBusy", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var busyPeriods = new List<CalendarFreeBusy>();

        if (result.TryGetProperty("calendars", out var calendars)
            && calendars.TryGetProperty("primary", out var primary)
            && primary.TryGetProperty("busy", out var busy))
        {
            foreach (var period in busy.EnumerateArray())
            {
                busyPeriods.Add(new CalendarFreeBusy(
                    DateTimeOffset.Parse(period.GetProperty("start").GetString()!),
                    DateTimeOffset.Parse(period.GetProperty("end").GetString()!),
                    true));
            }
        }

        return busyPeriods;
    }

    public Task<CalendarSyncResult> SyncEventsAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        logger.LogInformation("Google Calendar: SyncEvents not yet implemented for user {UserId}", userId);
        return Task.FromResult(new CalendarSyncResult(0, 0, 0, null));
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var client = await CreateAuthenticatedClient(userId, integrationId, ct);
            var response = await client.GetAsync(
                "https://www.googleapis.com/calendar/v3/calendars/primary", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Calendar: TestConnection failed for user {UserId}", userId);
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

        var client = httpClientFactory.CreateClient("GoogleCalendar");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
